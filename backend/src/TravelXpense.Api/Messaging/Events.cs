using System.Text.Json;
using System.Threading.Channels;
using Azure.Messaging.ServiceBus;
using Microsoft.EntityFrameworkCore;
using TravelXpense.Api.Ai;
using TravelXpense.Api.Data;
using TravelXpense.Api.Domain;
using TravelXpense.Api.Policy;

namespace TravelXpense.Api.Messaging;

public record EventEnvelope(string MessageId, string Type, string Payload);
public record ExpenseSubmittedEvent(Guid ReportId);
public record ExpenseReviewedEvent(Guid ReportId, bool Approved);

public static class EventTypes
{
    public const string ExpenseSubmitted = "ExpenseSubmitted";
    public const string ExpenseReviewed = "ExpenseReviewed";
}

public interface IEventPublisher
{
    Task PublishAsync(string type, object payload, CancellationToken ct = default);
}

// ---------- Local mode: an in-memory queue, no Azure needed ----------
public class InProcessBus : IEventPublisher
{
    private readonly Channel<EventEnvelope> _channel = Channel.CreateUnbounded<EventEnvelope>();
    public ChannelReader<EventEnvelope> Reader => _channel.Reader;

    public async Task PublishAsync(string type, object payload, CancellationToken ct = default) =>
        await _channel.Writer.WriteAsync(
            new EventEnvelope(Guid.NewGuid().ToString("N"), type, JsonSerializer.Serialize(payload)), ct);
}

public class InProcessConsumer(InProcessBus bus, IServiceScopeFactory scopes, ILogger<InProcessConsumer> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await foreach (var envelope in bus.Reader.ReadAllAsync(ct))
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<ExpenseEventHandler>().HandleAsync(envelope, ct);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Failed to process {Type} message {Id}", envelope.Type, envelope.MessageId);
            }
        }
    }
}

// ---------- Cloud mode: Azure Service Bus queue ----------
public class ServiceBusPublisher(ServiceBusClient client, IConfiguration cfg) : IEventPublisher, IAsyncDisposable
{
    private readonly ServiceBusSender _sender = client.CreateSender(cfg["ServiceBus:QueueName"] ?? "expense-events");

    public Task PublishAsync(string type, object payload, CancellationToken ct = default) =>
        _sender.SendMessageAsync(new ServiceBusMessage(JsonSerializer.Serialize(payload))
        {
            MessageId = Guid.NewGuid().ToString("N"),
            Subject = type,
            ContentType = "application/json"
        }, ct);

    public ValueTask DisposeAsync() => _sender.DisposeAsync();
}

public class ServiceBusConsumer(ServiceBusClient client, IServiceScopeFactory scopes, IConfiguration cfg, ILogger<ServiceBusConsumer> log)
    : BackgroundService
{
    private ServiceBusProcessor? _processor;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // AutoCompleteMessages: success => message removed. Exception => message is retried,
        // and after the queue's MaxDeliveryCount it lands in the dead-letter queue automatically.
        _processor = client.CreateProcessor(cfg["ServiceBus:QueueName"] ?? "expense-events",
            new ServiceBusProcessorOptions { MaxConcurrentCalls = 4, AutoCompleteMessages = true });

        _processor.ProcessMessageAsync += async args =>
        {
            var envelope = new EventEnvelope(args.Message.MessageId, args.Message.Subject, args.Message.Body.ToString());
            using var scope = scopes.CreateScope();
            await scope.ServiceProvider.GetRequiredService<ExpenseEventHandler>().HandleAsync(envelope, args.CancellationToken);
        };
        _processor.ProcessErrorAsync += args =>
        {
            log.LogError(args.Exception, "Service Bus error from {Source}", args.ErrorSource);
            return Task.CompletedTask;
        };
        await _processor.StartProcessingAsync(ct);
    }

    public override async Task StopAsync(CancellationToken ct)
    {
        if (_processor is not null)
        {
            await _processor.StopProcessingAsync(ct);
            await _processor.DisposeAsync();
        }
        await base.StopAsync(ct);
    }
}

// ---------- The actual business reaction to events (same code in both modes) ----------
public class ExpenseEventHandler(AppDbContext db, IExpenseCategorizer categorizer, PolicyService policy, ILogger<ExpenseEventHandler> log)
{
    public async Task HandleAsync(EventEnvelope envelope, CancellationToken ct)
    {
        // Idempotency: queues can deliver the same message twice. Skip if we already handled it.
        if (await db.ProcessedMessages.AnyAsync(m => m.MessageId == envelope.MessageId, ct))
        {
            log.LogInformation("Duplicate message {Id} skipped", envelope.MessageId);
            return;
        }

        switch (envelope.Type)
        {
            case EventTypes.ExpenseSubmitted:
                await OnSubmittedAsync(JsonSerializer.Deserialize<ExpenseSubmittedEvent>(envelope.Payload)!, ct);
                break;
            case EventTypes.ExpenseReviewed:
                OnReviewed(JsonSerializer.Deserialize<ExpenseReviewedEvent>(envelope.Payload)!);
                break;
            default:
                log.LogWarning("Unknown event type {Type}", envelope.Type);
                break;
        }

        // Business changes + "processed" marker are saved together in ONE SaveChanges (one transaction).
        db.ProcessedMessages.Add(new ProcessedMessage
        {
            MessageId = envelope.MessageId, Handler = nameof(ExpenseEventHandler), ProcessedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct);
    }

    private async Task OnSubmittedAsync(ExpenseSubmittedEvent e, CancellationToken ct)
    {
        var report = await db.ExpenseReports.Include(r => r.Items).FirstOrDefaultAsync(r => r.Id == e.ReportId, ct);
        if (report is null || report.Status != ReportStatus.Submitted) return; // nothing to do / already processed

        // 1) Auto-categorize lines the employee left blank (AI or keyword rules)
        foreach (var item in report.Items.Where(i => i.Category == ExpenseCategory.Uncategorized))
        {
            item.Category = await categorizer.CategorizeAsync(item.Merchant, item.Description, ct);
            item.AutoCategorized = true;
        }

        // 2) Check company policy
        var rules = await db.PolicyRules.Where(r => r.Active).ToListAsync(ct);
        var findings = policy.Evaluate(report.Items, rules);
        foreach (var f in findings)
        {
            db.PolicyViolations.Add(new PolicyViolation
            {
                Id = Guid.NewGuid(), ReportId = report.Id, ExpenseItemId = f.ItemId,
                Message = f.Message, CreatedAt = DateTime.UtcNow
            });
        }

        // 3) Route: clean reports wait for approval, problem reports are flagged for the manager's attention
        report.Status = findings.Count == 0 ? ReportStatus.PendingApproval : ReportStatus.Flagged;

        db.AuditLogs.Add(new AuditLog
        {
            UserId = report.EmployeeId, Action = "ReportProcessed", EntityType = nameof(ExpenseReport),
            EntityId = report.Id.ToString(), Details = $"Status={report.Status}; Violations={findings.Count}", At = DateTime.UtcNow
        });
    }

    private void OnReviewed(ExpenseReviewedEvent e)
    {
        // Placeholder for "send email / queue reimbursement". Here we only write an audit entry.
        db.AuditLogs.Add(new AuditLog
        {
            Action = e.Approved ? "ReimbursementQueued" : "RejectionNotified", EntityType = nameof(ExpenseReport),
            EntityId = e.ReportId.ToString(), At = DateTime.UtcNow
        });
    }
}
