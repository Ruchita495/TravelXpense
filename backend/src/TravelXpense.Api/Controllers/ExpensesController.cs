using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TravelXpense.Api.Contracts;
using TravelXpense.Api.Data;
using TravelXpense.Api.Domain;
using TravelXpense.Api.Messaging;
using TravelXpense.Api.Security;

namespace TravelXpense.Api.Controllers;

[ApiController, Route("api/expenses"), Authorize]
public class ExpensesController(AppDbContext db, IEventPublisher bus) : ControllerBase
{
    private IQueryable<ExpenseReport> Reports =>
        db.ExpenseReports.Include(r => r.Items).Include(r => r.Violations).Include(r => r.Employee);

    [HttpGet]
    public async Task<ActionResult<List<ReportDto>>> List()
    {
        var uid = User.GetUserId();
        var reports = await Reports.Where(r => r.EmployeeId == uid).OrderByDescending(r => r.CreatedAt).ToListAsync();
        return reports.Select(r => r.ToDto()).ToList();
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ReportDto>> Get(Guid id)
    {
        var uid = User.GetUserId();
        var report = await Reports.FirstOrDefaultAsync(r => r.Id == id && r.EmployeeId == uid);
        // 404 (not 403) for someone else's report: we don't even confirm it exists (prevents IDOR probing).
        if (report is null) return NotFound();
        return report.ToDto();
    }

    [HttpPost]
    public async Task<ActionResult<ReportDto>> Create(CreateReportRequest req)
    {
        var uid = User.GetUserId();
        var tripOk = await db.Trips.AnyAsync(t => t.Id == req.TripId && t.EmployeeId == uid);
        if (!tripOk) return NotFound(new ProblemDetails { Title = "Trip not found." });

        var report = new ExpenseReport
        {
            Id = Guid.NewGuid(), TripId = req.TripId, EmployeeId = uid, Title = req.Title.Trim(), CreatedAt = DateTime.UtcNow
        };
        db.ExpenseReports.Add(report);
        await db.SaveChangesAsync();
        return Ok((await Reports.FirstAsync(r => r.Id == report.Id)).ToDto());
    }

    [HttpPost("{id:guid}/items")]
    public async Task<ActionResult<ReportDto>> AddItem(Guid id, AddExpenseItemRequest req)
    {
        var uid = User.GetUserId();
        var report = await Reports.FirstOrDefaultAsync(r => r.Id == id && r.EmployeeId == uid);
        if (report is null) return NotFound();
        if (report.Status != ReportStatus.Draft)
            return Conflict(new ProblemDetails { Title = "Only draft reports can be edited." });

        db.ExpenseItems.Add(new ExpenseItem
        {
            Id = Guid.NewGuid(), ReportId = report.Id, Date = req.Date, Merchant = req.Merchant.Trim(),
            Description = req.Description?.Trim() ?? "", Amount = req.Amount, Currency = req.Currency.ToUpperInvariant(),
            Category = req.Category ?? ExpenseCategory.Uncategorized, ReceiptUrl = req.ReceiptUrl
        });
        await db.SaveChangesAsync();

        report.Total = await db.ExpenseItems.Where(i => i.ReportId == id).SumAsync(i => i.Amount);
        await db.SaveChangesAsync();
        return Ok((await Reports.FirstAsync(r => r.Id == id)).ToDto());
    }

    [HttpPost("{id:guid}/submit")]
    public async Task<ActionResult<ReportDto>> Submit(Guid id)
    {
        var uid = User.GetUserId();
        var report = await Reports.FirstOrDefaultAsync(r => r.Id == id && r.EmployeeId == uid);
        if (report is null) return NotFound();
        if (report.Status != ReportStatus.Draft)
            return Conflict(new ProblemDetails { Title = "This report was already submitted." });
        if (report.Items.Count == 0)
            return BadRequest(new ProblemDetails { Title = "Add at least one expense before submitting." });

        report.Status = ReportStatus.Submitted;
        report.SubmittedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        // Hand the heavy work (categorize + policy check) to the background consumer via the queue.
        await bus.PublishAsync(EventTypes.ExpenseSubmitted, new ExpenseSubmittedEvent(report.Id));
        return Accepted(report.ToDto());
    }
}
