using System.Net;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PortFlow.Shared.Attachment;
using PortFlow.Shared.Domain;
using PortFlow.Shared.Events;
using PortFlow.Shared.Idempotency;
using PortFlow.Shared.Messaging;
using PortFlow.Shared.Persistence;

namespace PortFlow.Tests.Support.Fixture;

/// <summary>Mini-module giả lập một module nghiệp vụ để kiểm thử toàn bộ hạ tầng F00 end-to-end.</summary>
public static class TestWidgetFixture
{
    public const string Queue = "q.test-widgets";
    public const string Consumer = "test-consumer";

    public static readonly ErrorCode WidgetDuplicate =
        new("WIDGET_DUPLICATE", HttpStatusCode.Conflict, "Widget name already exists");

    public static void Register(IServiceCollection services)
    {
        services.AddPortFlowDbContext<TestFixtureDbContext>();
        services.AddScoped<TestWidgetService>();
        services.AddControllers().AddApplicationPart(typeof(TestWidgetController).Assembly);
        services.AddConsumerQueue(Queue, "test.#");
        services.AddRabbitListener<TestWidgetCreated, TestWidgetConsumer>(Queue, idempotentConsumer: Consumer);
        services.AddSingleton<IConstraintErrorMapping, TestConstraintErrors>();
        services.AddSingleton<IAttachmentAccessPolicy, TestDocAttachmentPolicy>();
    }

    private sealed class TestConstraintErrors : IConstraintErrorMapping
    {
        public IReadOnlyDictionary<string, IErrorCode> Mappings { get; } =
            new Dictionary<string, IErrorCode> { ["ux_widget_name"] = WidgetDuplicate };
    }

    private sealed class TestDocAttachmentPolicy : IAttachmentAccessPolicy
    {
        public string EntityType => "TEST_DOC";

        public Task CheckCanUploadAsync(Guid entityId, CancellationToken ct) => Task.CompletedTask;

        public Task CheckCanViewAsync(Guid entityId, CancellationToken ct) => Task.CompletedTask;
    }
}

public sealed record CreateWidget(string Name);

public sealed record WidgetResponse(Guid Id, string Name, long Version);

public sealed class TestWidgetService(
    TestFixtureDbContext db,
    ITransactionRunner tx,
    DomainEventPublisher events,
    EventMetadataFactory metadata)
{
    public Task<TestWidget> CreateAsync(string name, bool failAfterSave, CancellationToken ct) =>
        tx.ExecuteAsync(async token =>
        {
            var widget = TestWidget.Create(name, metadata.Create());
            db.Widgets.Add(widget);
            await db.SaveChangesAsync(token);
            await events.PublishFromAsync(widget, token);
            if (failAfterSave)
            {
                throw new DomainException(SysErrorCode.SYS_VALIDATION, "Simulated business error after save");
            }
            return widget;
        }, ct);

    public Task<TestWidget> RenameAsync(Guid id, string newName, long expectedVersion, CancellationToken ct) =>
        tx.ExecuteAsync(async token =>
        {
            var widget = await db.Widgets.SingleAsync(w => w.Id == id, token);
            if (widget.Version != expectedVersion)
            {
                throw new OptimisticLockingFailureException(typeof(TestWidget), id);
            }
            widget.Rename(newName);
            await db.SaveChangesAsync(token);
            return widget;
        }, ct);
}

[ApiController]
[Route("test/widgets")]
public sealed class TestWidgetController(TestWidgetService service) : ControllerBase
{
    [HttpPost]
    [Idempotent]
    public async Task<IActionResult> Create([FromBody] CreateWidget request, [FromQuery] bool fail = false)
    {
        var widget = await service.CreateAsync(request.Name, fail, HttpContext.RequestAborted);
        return StatusCode(201, new WidgetResponse(widget.Id, widget.Name, widget.Version!.Value));
    }

    [HttpPut("{id:guid}")]
    public async Task<WidgetResponse> Rename(Guid id, [FromQuery] long version, [FromBody] CreateWidget request)
    {
        var widget = await service.RenameAsync(id, request.Name, version, HttpContext.RequestAborted);
        return new WidgetResponse(widget.Id, widget.Name, widget.Version!.Value);
    }
}

public sealed class TestWidgetConsumer(DbSession session) : IEventConsumer<TestWidgetCreated>
{
    public async Task HandleAsync(TestWidgetCreated @event, CancellationToken cancellationToken)
    {
        var connection = await session.OpenAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition("""
            INSERT INTO test_fixture.received_event (event_id, widget_id, consumer)
            VALUES (@eventId, @widgetId, @consumer)
            """, new { eventId = @event.Metadata.EventId, widgetId = @event.WidgetId, consumer = TestWidgetFixture.Consumer },
            session.Transaction, cancellationToken: cancellationToken));
    }
}
