using Microsoft.EntityFrameworkCore;
using PortFlow.Shared.Domain;
using PortFlow.Shared.Events;
using PortFlow.Shared.Id;
using PortFlow.Shared.Persistence;

namespace PortFlow.Tests.Support.Fixture;

/// <summary>Aggregate chỉ dùng cho integration test của F00 (bảng <c>test_fixture.widget</c>).</summary>
public sealed class TestWidget : AggregateRoot<Guid>
{
    private TestWidget()
    {
    }

    public string Name { get; private set; } = null!;

    internal static TestWidget Create(string name, EventMetadata metadata)
    {
        var widget = new TestWidget { Id = UuidV7.Next(), Name = name };
        widget.Raise(new TestWidgetCreated(metadata, widget.Id, name));
        return widget;
    }

    internal void Rename(string newName) => Name = newName;
}

[Externalized("portflow.events::test.widget-created")]
public sealed record TestWidgetCreated(EventMetadata Metadata, Guid WidgetId, string Name) : IDomainEvent;

public sealed class TestFixtureDbContext(DbContextOptions<TestFixtureDbContext> options) : DbContext(options)
{
    public DbSet<TestWidget> Widgets => Set<TestWidget>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TestWidget>(widget =>
        {
            widget.ToTable("widget", "test_fixture");
            widget.HasKey(w => w.Id);
        });
        modelBuilder.ApplyAggregateRootConventions();
    }
}
