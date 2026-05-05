using System.Text;
using System.Text.Json;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Checkpointing;
using Microsoft.Extensions.Logging;

namespace Ticketing.PurchasingAgent.Services;

/// <summary>
/// Persists MAF workflow checkpoints to Azure Blob storage so a workflow can
/// suspend in one Azure Functions invocation and resume in another. Layout:
/// <list type="bullet">
///   <item>{sessionId}/checkpoints/{checkpointId}.json — checkpoint payload</item>
///   <item>{sessionId}/index/{checkpointId}.json — { checkpointId, parentCheckpointId? }</item>
///   <item>{sessionId}/latest.json — pointer to the most recently created checkpoint</item>
/// </list>
/// Implements <see cref="ICheckpointStore{JsonElement}"/> for the runtime; exposes a
/// <see cref="TryGetLatestAsync"/> helper so the resume function can find the suspension
/// point without enumerating the full index.
/// </summary>
public sealed class BlobWorkflowCheckpointStore : ICheckpointStore<JsonElement>
{
    public const string ContainerName = "purchasing-workflow-checkpoints";

    private readonly BlobContainerClient _container;
    private readonly ILogger<BlobWorkflowCheckpointStore> _logger;
    private bool _initialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    public BlobWorkflowCheckpointStore(
        BlobServiceClient serviceClient,
        ILogger<BlobWorkflowCheckpointStore> logger)
    {
        _container = serviceClient.GetBlobContainerClient(ContainerName);
        _logger = logger;
    }

    public async ValueTask<CheckpointInfo> CreateCheckpointAsync(
        string sessionId,
        JsonElement value,
        CheckpointInfo? parent)
    {
        await EnsureContainerAsync();

        var info = new CheckpointInfo(sessionId, Guid.NewGuid().ToString("N"));

        var payloadBytes = Encoding.UTF8.GetBytes(value.GetRawText());
        await _container.GetBlobClient(CheckpointBlob(info)).UploadAsync(
            new BinaryData(payloadBytes), overwrite: true);

        var indexEntry = new IndexEntry(info.CheckpointId, parent?.CheckpointId);
        var indexBytes = JsonSerializer.SerializeToUtf8Bytes(indexEntry);
        await _container.GetBlobClient(IndexBlob(info)).UploadAsync(
            new BinaryData(indexBytes), overwrite: true);

        var latestBytes = JsonSerializer.SerializeToUtf8Bytes(
            new LatestPointer(info.SessionId, info.CheckpointId));
        await _container.GetBlobClient(LatestBlob(sessionId)).UploadAsync(
            new BinaryData(latestBytes), overwrite: true);

        _logger.LogDebug(
            "Saved checkpoint {CheckpointId} for session {SessionId} (parent {ParentId})",
            info.CheckpointId, sessionId, parent?.CheckpointId ?? "<none>");

        return info;
    }

    public async ValueTask<JsonElement> RetrieveCheckpointAsync(string sessionId, CheckpointInfo key)
    {
        await EnsureContainerAsync();
        var blob = _container.GetBlobClient(CheckpointBlob(key));
        var response = await blob.DownloadContentAsync();
        using var doc = JsonDocument.Parse(response.Value.Content.ToMemory());
        return doc.RootElement.Clone();
    }

    public async ValueTask<IEnumerable<CheckpointInfo>> RetrieveIndexAsync(
        string sessionId,
        CheckpointInfo? withParent)
    {
        await EnsureContainerAsync();
        var prefix = $"{sessionId}/index/";
        var results = new List<CheckpointInfo>();
        await foreach (var blobItem in _container.GetBlobsAsync(BlobTraits.None, BlobStates.None, prefix, CancellationToken.None))
        {
            var blob = _container.GetBlobClient(blobItem.Name);
            var content = await blob.DownloadContentAsync();
            var entry = JsonSerializer.Deserialize<IndexEntry>(content.Value.Content.ToMemory().Span);
            if (entry is null) continue;
            if (withParent is not null && entry.ParentCheckpointId != withParent.CheckpointId) continue;
            results.Add(new CheckpointInfo(sessionId, entry.CheckpointId));
        }
        return results;
    }

    /// <summary>
    /// Returns the most recently created checkpoint for the session, or null if no
    /// suspended workflow exists.
    /// </summary>
    public async Task<CheckpointInfo?> TryGetLatestAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        await EnsureContainerAsync();
        var blob = _container.GetBlobClient(LatestBlob(sessionId));
        try
        {
            var response = await blob.DownloadContentAsync(cancellationToken);
            var pointer = JsonSerializer.Deserialize<LatestPointer>(response.Value.Content.ToMemory().Span);
            return pointer is null ? null : new CheckpointInfo(pointer.SessionId, pointer.CheckpointId);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    /// <summary>
    /// Removes all checkpoint blobs for a session — call after the workflow
    /// terminates so we don't accumulate state.
    /// </summary>
    public async Task DeleteSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        await EnsureContainerAsync();
        var prefix = $"{sessionId}/";
        await foreach (var blobItem in _container.GetBlobsAsync(BlobTraits.None, BlobStates.None, prefix, cancellationToken))
        {
            await _container.DeleteBlobIfExistsAsync(blobItem.Name, cancellationToken: cancellationToken);
        }
        _logger.LogInformation("Deleted all checkpoint blobs for session {SessionId}", sessionId);
    }

    private async ValueTask EnsureContainerAsync()
    {
        if (_initialized) return;
        await _initLock.WaitAsync();
        try
        {
            if (_initialized) return;
            await _container.CreateIfNotExistsAsync(PublicAccessType.None);
            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    private static string CheckpointBlob(CheckpointInfo info) =>
        $"{info.SessionId}/checkpoints/{info.CheckpointId}.json";

    private static string IndexBlob(CheckpointInfo info) =>
        $"{info.SessionId}/index/{info.CheckpointId}.json";

    private static string LatestBlob(string sessionId) =>
        $"{sessionId}/latest.json";

    private sealed record IndexEntry(string CheckpointId, string? ParentCheckpointId);
    private sealed record LatestPointer(string SessionId, string CheckpointId);
}
