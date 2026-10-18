using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using PortFlow.Workflow.Api;

namespace PortFlow.Tests.Workflow.Testing;

/// <summary>= TestApprovableDocuments (profile test): hai loại hồ sơ giả có handler ghi lại mọi callback.</summary>
public static class TestApprovableDocuments
{
    public const string Type = "TEST_DOC";

    public const string TypeAllApprovers = "TEST_DOC_ALL";

    public static readonly RecordingDocumentHandler Handler = new(Type);

    public static readonly RecordingDocumentHandler AllApproversHandler = new(TypeAllApprovers);

    public static void Register(IServiceCollection services)
    {
        services.AddSingleton<IApprovableDocumentHandler>(Handler);
        services.AddSingleton<IApprovableDocumentHandler>(AllApproversHandler);
    }
}

public sealed class RecordingDocumentHandler(string documentType) : IApprovableDocumentHandler
{
    private readonly ConcurrentDictionary<Guid, ConcurrentQueue<string>> _calls = new();
    private readonly ConcurrentDictionary<Guid, bool> _failOnApprove = new();

    public string DocumentType => documentType;

    public Task OnStepStartedAsync(Guid documentId, int stepOrder, CancellationToken ct) =>
        Record(documentId, "STEP_STARTED:" + stepOrder);

    public Task OnApprovedAsync(Guid documentId, ApprovalOutcome outcome, CancellationToken ct)
    {
        if (_failOnApprove.ContainsKey(documentId))
        {
            throw new InvalidOperationException("Simulated handler failure for " + documentId);
        }
        return Record(documentId, "APPROVED:eSign=" + outcome.RequiresESign.ToString().ToLowerInvariant());
    }

    public Task OnRejectedAsync(Guid documentId, string? comment, CancellationToken ct) =>
        Record(documentId, "REJECTED:" + comment);

    public Task OnRevisionRequestedAsync(Guid documentId, string? comment, CancellationToken ct) =>
        Record(documentId, "REVISION_REQUESTED:" + comment);

    public Task<DocumentSummary> SummarizeAsync(Guid documentId, CancellationToken ct) =>
        Task.FromResult(new DocumentSummary("HD-" + documentId.ToString()[..8], "Dịch vụ xếp dỡ – Cty XYZ", "XYZ",
            1200000000m, "VND"));

    public IReadOnlyList<string> Calls(Guid documentId) =>
        _calls.TryGetValue(documentId, out var calls) ? calls.ToList() : [];

    public void FailOnApprove(Guid documentId) => _failOnApprove[documentId] = true;

    private Task Record(Guid documentId, string call)
    {
        _calls.GetOrAdd(documentId, _ => new ConcurrentQueue<string>()).Enqueue(call);
        return Task.CompletedTask;
    }
}
