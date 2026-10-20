using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TravelXpense.Api.Contracts;
using TravelXpense.Api.Data;
using TravelXpense.Api.Domain;
using TravelXpense.Api.Messaging;
using TravelXpense.Api.Security;

namespace TravelXpense.Api.Controllers;

[ApiController, Route("api/approvals"), Authorize(Roles = "Manager,Admin")]
public class ApprovalsController(AppDbContext db, IEventPublisher bus) : ControllerBase
{
    /// <summary>Reports this reviewer may act on: never their own, and (unless Admin) only their team or unassigned staff.</summary>
    private IQueryable<ExpenseReport> Reviewable()
    {
        var uid = User.GetUserId();
        var q = db.ExpenseReports.Include(r => r.Items).Include(r => r.Violations).Include(r => r.Employee)
            .Where(r => r.EmployeeId != uid &&
                        (r.Status == ReportStatus.PendingApproval || r.Status == ReportStatus.Flagged));
        if (!User.IsInRole("Admin"))
            q = q.Where(r => r.Employee!.ManagerId == uid || r.Employee!.ManagerId == null);
        return q;
    }

    [HttpGet]
    public async Task<ActionResult<List<ReportDto>>> Pending()
    {
        var reports = await Reviewable().OrderBy(r => r.SubmittedAt).ToListAsync();
        return reports.Select(r => r.ToDto()).ToList();
    }

    [HttpPost("{id:guid}/approve")]
    public Task<ActionResult<ReportDto>> Approve(Guid id, ReviewRequest req) => ReviewAsync(id, true, req.Comment);

    [HttpPost("{id:guid}/reject")]
    public Task<ActionResult<ReportDto>> Reject(Guid id, ReviewRequest req) => ReviewAsync(id, false, req.Comment);

    private async Task<ActionResult<ReportDto>> ReviewAsync(Guid id, bool approve, string? comment)
    {
        if (!approve && string.IsNullOrWhiteSpace(comment))
            return BadRequest(new ProblemDetails { Title = "Please give a reason when rejecting." });

        var report = await Reviewable().FirstOrDefaultAsync(r => r.Id == id);
        if (report is null) return NotFound();

        report.Status = approve ? ReportStatus.Approved : ReportStatus.Rejected;
        report.ReviewedById = User.GetUserId();
        report.ReviewedAt = DateTime.UtcNow;
        report.ReviewerComment = comment?.Trim();
        db.AuditLogs.Add(new AuditLog
        {
            UserId = User.GetUserId(), Action = approve ? "ReportApproved" : "ReportRejected",
            EntityType = nameof(ExpenseReport), EntityId = report.Id.ToString(), Details = comment, At = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        await bus.PublishAsync(EventTypes.ExpenseReviewed, new ExpenseReviewedEvent(report.Id, approve));
        return Ok(report.ToDto());
    }
}
