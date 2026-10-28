using System.Collections.Concurrent;
using PixousDocIntelligence.DocumentGeneration.Models;

namespace PixousDocIntelligence.DocumentGeneration.Repositories;

public interface IDocumentDraftRepository
{
    Task<DocumentDraftRecord?> GetDraftAsync(string documentId, CancellationToken cancellationToken = default);
    Task SaveDraftAsync(DocumentDraftRecord draft, CancellationToken cancellationToken = default);
    Task SaveGeneratedFileAsync(string documentId, string format, byte[] fileBytes, CancellationToken cancellationToken = default);
    Task<byte[]?> GetGeneratedFileAsync(string documentId, string format, CancellationToken cancellationToken = default);
}

public class DocumentDraftRepository : IDocumentDraftRepository
{
    private readonly ConcurrentDictionary<string, DocumentDraftRecord> _drafts = new();
    private readonly string _storageDir;

    public DocumentDraftRepository()
    {
        _storageDir = Path.Combine(Directory.GetCurrentDirectory(), "output", "documents");
        Directory.CreateDirectory(_storageDir);
    }

    public Task<DocumentDraftRecord?> GetDraftAsync(string documentId, CancellationToken cancellationToken = default)
    {
        _drafts.TryGetValue(documentId, out var draft);
        return Task.FromResult(draft);
    }

    public Task SaveDraftAsync(DocumentDraftRecord draft, CancellationToken cancellationToken = default)
    {
        draft.UpdatedAt = DateTime.UtcNow;
        _drafts[draft.DocumentId] = draft;
        return Task.CompletedTask;
    }

    public async Task SaveGeneratedFileAsync(string documentId, string format, byte[] fileBytes, CancellationToken cancellationToken = default)
    {
        string fileName = $"{documentId}.{format.ToLowerInvariant()}";
        string filePath = Path.Combine(_storageDir, fileName);
        await File.WriteAllBytesAsync(filePath, fileBytes, cancellationToken);

        if (_drafts.TryGetValue(documentId, out var draft))
        {
            draft.GeneratedFilePaths[format.ToLowerInvariant()] = filePath;
            draft.UpdatedAt = DateTime.UtcNow;
        }
    }

    public async Task<byte[]?> GetGeneratedFileAsync(string documentId, string format, CancellationToken cancellationToken = default)
    {
        string fileName = $"{documentId}.{format.ToLowerInvariant()}";
        string filePath = Path.Combine(_storageDir, fileName);

        if (File.Exists(filePath))
        {
            return await File.ReadAllBytesAsync(filePath, cancellationToken);
        }

        return null;
    }
}
