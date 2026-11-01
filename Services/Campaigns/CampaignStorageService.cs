using System.Collections.Concurrent;
using System.Text.Encodings.Web;
using System.Text.Json;
using PixousDocIntelligence.Application.Models;
using PixousDocIntelligence.Domain.Planning;

namespace PixousDocIntelligence.Services.Campaigns;

public interface ICampaignStorageService
{
    Task InitializeCampaignAsync(string campaignId, string campaignName, int totalDocuments, ExtractionPlan? plan, CancellationToken cancellationToken = default);
    Task RegisterBidQueuedAsync(string campaignId, string bidId, string jobId, string documentId, string sourceFileName, CancellationToken cancellationToken = default);
    Task SaveIsolatedBidResultAsync(string campaignId, string bidId, DocumentProcessingResult result, string sourceFileName, CancellationToken cancellationToken = default);
    Task MarkBidFailedAsync(string campaignId, string bidId, string errorMessage, CancellationToken cancellationToken = default);
    Task<CampaignStatusResponse?> GetCampaignStatusAsync(string campaignId, CancellationToken cancellationToken = default);
    Task<List<IsolatedBidRecord>> GetCampaignBidsAsync(string campaignId, CancellationToken cancellationToken = default);
    Task<IsolatedBidRecord?> GetBidRecordAsync(string campaignId, string bidId, CancellationToken cancellationToken = default);
    string GetCampaignDirectory(string campaignId);
}

/// <summary>
/// Manages campaign persistence, bounded concurrency isolation, and per-bid JSON artifact generation.
/// Stores each bid in output/campaigns/{campaignId}/bids/{bidId}.json.
/// </summary>
public class CampaignStorageService : ICampaignStorageService
{
    private readonly string _baseDirectory;
    private readonly ConcurrentDictionary<string, CampaignStatusResponse> _campaignCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, IsolatedBidRecord>> _bidCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _fileLock = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNameCaseInsensitive = true
    };

    public CampaignStorageService(string? baseDirectory = null)
    {
        _baseDirectory = baseDirectory ?? Path.Combine(Directory.GetCurrentDirectory(), "output", "campaigns");
        Directory.CreateDirectory(_baseDirectory);
    }

    public string GetCampaignDirectory(string campaignId) => Path.Combine(_baseDirectory, campaignId);
    public string GetBidsDirectory(string campaignId) => Path.Combine(GetCampaignDirectory(campaignId), "bids");

    public async Task InitializeCampaignAsync(
        string campaignId, 
        string campaignName, 
        int totalDocuments, 
        ExtractionPlan? plan, 
        CancellationToken cancellationToken = default)
    {
        string campaignDir = GetCampaignDirectory(campaignId);
        string bidsDir = GetBidsDirectory(campaignId);
        Directory.CreateDirectory(bidsDir);

        var campaignStatus = new CampaignStatusResponse
        {
            CampaignId = campaignId,
            CampaignName = campaignName,
            Status = "Processing",
            TotalBids = totalDocuments,
            CompletedCount = 0,
            FailedCount = 0,
            QueuedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _campaignCache[campaignId] = campaignStatus;
        _bidCache[campaignId] = new ConcurrentDictionary<string, IsolatedBidRecord>(StringComparer.OrdinalIgnoreCase);

        // Save frozen schema.json if plan exists
        if (plan != null)
        {
            string schemaPath = Path.Combine(campaignDir, "schema.json");
            await File.WriteAllTextAsync(schemaPath, JsonSerializer.Serialize(plan, JsonOptions), cancellationToken);
        }

        await PersistManifestAsync(campaignId, cancellationToken);
    }

    public Task RegisterBidQueuedAsync(
        string campaignId, 
        string bidId, 
        string jobId, 
        string documentId, 
        string sourceFileName, 
        CancellationToken cancellationToken = default)
    {
        if (_campaignCache.TryGetValue(campaignId, out var campaign))
        {
            lock (campaign)
            {
                campaign.Bids.Add(new BidItemSummary
                {
                    BidId = bidId,
                    JobId = jobId,
                    DocumentId = documentId,
                    SourceFileName = sourceFileName,
                    Status = "Queued",
                    JsonPath = Path.Combine("output", "campaigns", campaignId, "bids", $"{bidId}.json")
                });
                campaign.UpdatedAt = DateTime.UtcNow;
            }
        }

        return Task.CompletedTask;
    }

    public async Task SaveIsolatedBidResultAsync(
        string campaignId, 
        string bidId, 
        DocumentProcessingResult result, 
        string sourceFileName, 
        CancellationToken cancellationToken = default)
    {
        string bidsDir = GetBidsDirectory(campaignId);
        Directory.CreateDirectory(bidsDir);

        // 1. Infer organization name from extracted items
        string orgName = InferOrganizationName(result, sourceFileName);

        // 2. Build isolated record
        var record = new IsolatedBidRecord
        {
            BidId = bidId,
            CampaignId = campaignId,
            JobId = result.JobId,
            DocumentId = result.DocumentId,
            OrganizationName = orgName,
            SourceFileName = sourceFileName,
            ProcessedAt = DateTime.UtcNow,
            ExecutionDurationMs = result.ExecutionDurationMs,
            Status = result.Status,
            PlanId = result.Plan.PlanId
        };

        // Populate fields
        foreach (var item in result.StitchedEnvelope.Items)
        {
            record.ExtractedFields[item.FieldKey] = new ExtractedFieldDetail
            {
                Value = item.Value,
                Confidence = item.Confidence.Value,
                ValidationStatus = item.ValidationStatus,
                PageNumber = item.PrimaryPageNumber,
                SourceQuote = item.PrimarySourceText
            };
        }

        // Populate tables
        foreach (var table in result.StitchedEnvelope.Tables)
        {
            var tableDetail = new ExtractedTableDetail
            {
                Headers = table.Headers,
                PageNumber = table.PageNumber
            };

            foreach (var row in table.Rows)
            {
                var rowDict = new Dictionary<string, object?>();
                foreach (var kvp in row)
                {
                    rowDict[kvp.Key] = kvp.Value.Value;
                }
                tableDetail.Rows.Add(rowDict);
            }
            record.Tables.Add(tableDetail);
        }

        // Populate verification summary
        record.VerificationSummary = new VerificationSummaryDetail
        {
            TotalFields = result.StitchedEnvelope.Items.Count,
            VerifiedFields = result.VerificationReport.VerifiedCount,
            UnverifiedFields = result.VerificationReport.UnverifiedEvidenceCount,
            ReviewTasksCount = result.VerificationReport.ReviewTasks.Count
        };

        // Cache in-memory
        if (_bidCache.TryGetValue(campaignId, out var bidsMap))
        {
            bidsMap[bidId] = record;
        }

        // Write isolated JSON artifact: output/campaigns/{campaignId}/bids/{bidId}.json
        string bidFilePath = Path.Combine(bidsDir, $"{bidId}.json");
        string jsonContent = JsonSerializer.Serialize(record, JsonOptions);
        await File.WriteAllTextAsync(bidFilePath, jsonContent, cancellationToken);

        // Update campaign status
        if (_campaignCache.TryGetValue(campaignId, out var campaign))
        {
            lock (campaign)
            {
                campaign.CompletedCount++;
                var summary = campaign.Bids.FirstOrDefault(b => b.BidId.Equals(bidId, StringComparison.OrdinalIgnoreCase));
                if (summary != null)
                {
                    summary.Status = result.Status;
                    summary.OrganizationName = orgName;
                    summary.DurationMs = result.ExecutionDurationMs;
                }
                if (campaign.CompletedCount + campaign.FailedCount >= campaign.TotalBids)
                {
                    campaign.Status = campaign.FailedCount > 0 ? "PartialFailures" : "Completed";
                }
                campaign.UpdatedAt = DateTime.UtcNow;
            }

            await PersistManifestAsync(campaignId, cancellationToken);
        }
    }

    public async Task MarkBidFailedAsync(string campaignId, string bidId, string errorMessage, CancellationToken cancellationToken = default)
    {
        if (_campaignCache.TryGetValue(campaignId, out var campaign))
        {
            lock (campaign)
            {
                campaign.FailedCount++;
                var summary = campaign.Bids.FirstOrDefault(b => b.BidId.Equals(bidId, StringComparison.OrdinalIgnoreCase));
                if (summary != null)
                {
                    summary.Status = "Failed";
                }
                if (campaign.CompletedCount + campaign.FailedCount >= campaign.TotalBids)
                {
                    campaign.Status = "PartialFailures";
                }
                campaign.UpdatedAt = DateTime.UtcNow;
            }

            await PersistManifestAsync(campaignId, cancellationToken);
        }
    }

    public async Task<CampaignStatusResponse?> GetCampaignStatusAsync(string campaignId, CancellationToken cancellationToken = default)
    {
        if (_campaignCache.TryGetValue(campaignId, out var cached))
        {
            return cached;
        }

        // Try load from disk
        string manifestPath = Path.Combine(GetCampaignDirectory(campaignId), "campaign_manifest.json");
        if (File.Exists(manifestPath))
        {
            string json = await File.ReadAllTextAsync(manifestPath, cancellationToken);
            var loaded = JsonSerializer.Deserialize<CampaignStatusResponse>(json, JsonOptions);
            if (loaded != null)
            {
                _campaignCache[campaignId] = loaded;
                return loaded;
            }
        }

        return null;
    }

    public async Task<List<IsolatedBidRecord>> GetCampaignBidsAsync(string campaignId, CancellationToken cancellationToken = default)
    {
        if (_bidCache.TryGetValue(campaignId, out var map) && map.Count > 0)
        {
            return map.Values.OrderBy(b => b.BidId).ToList();
        }

        string bidsDir = GetBidsDirectory(campaignId);
        var records = new List<IsolatedBidRecord>();

        if (Directory.Exists(bidsDir))
        {
            foreach (var file in Directory.GetFiles(bidsDir, "*.json"))
            {
                try
                {
                    string json = await File.ReadAllTextAsync(file, cancellationToken);
                    var record = JsonSerializer.Deserialize<IsolatedBidRecord>(json, JsonOptions);
                    if (record != null) records.Add(record);
                }
                catch { /* ignore corrupted single record */ }
            }
        }

        return records.OrderBy(r => r.BidId).ToList();
    }

    public async Task<IsolatedBidRecord?> GetBidRecordAsync(string campaignId, string bidId, CancellationToken cancellationToken = default)
    {
        if (_bidCache.TryGetValue(campaignId, out var map) && map.TryGetValue(bidId, out var cached))
        {
            return cached;
        }

        string bidFilePath = Path.Combine(GetBidsDirectory(campaignId), $"{bidId}.json");
        if (File.Exists(bidFilePath))
        {
            string json = await File.ReadAllTextAsync(bidFilePath, cancellationToken);
            return JsonSerializer.Deserialize<IsolatedBidRecord>(json, JsonOptions);
        }

        return null;
    }

    private async Task PersistManifestAsync(string campaignId, CancellationToken cancellationToken)
    {
        if (_campaignCache.TryGetValue(campaignId, out var campaign))
        {
            await _fileLock.WaitAsync(cancellationToken);
            try
            {
                string manifestPath = Path.Combine(GetCampaignDirectory(campaignId), "campaign_manifest.json");
                string json = JsonSerializer.Serialize(campaign, JsonOptions);
                await File.WriteAllTextAsync(manifestPath, json, cancellationToken);
            }
            finally
            {
                _fileLock.Release();
            }
        }
    }

    private static string InferOrganizationName(DocumentProcessingResult result, string fallbackFileName)
    {
        // Try finding 'vendor', 'bidder', 'organization', or 'parties'
        var vendorItem = result.StitchedEnvelope.Items.FirstOrDefault(i => 
            i.FieldKey.Contains("vendor", StringComparison.OrdinalIgnoreCase) || 
            i.FieldKey.Contains("bidder", StringComparison.OrdinalIgnoreCase) || 
            i.FieldKey.Contains("organization", StringComparison.OrdinalIgnoreCase) ||
            i.FieldKey.Contains("company", StringComparison.OrdinalIgnoreCase));

        if (vendorItem != null && vendorItem.Value != null)
        {
            string val = vendorItem.Value.ToString()?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(val)) return val;
        }

        var partyItem = result.StitchedEnvelope.Items.FirstOrDefault(i => i.FieldKey.Equals("parties", StringComparison.OrdinalIgnoreCase));
        if (partyItem != null && partyItem.Value != null)
        {
            if (partyItem.Value is JsonElement elem && elem.ValueKind == JsonValueKind.Array)
            {
                var names = new List<string>();
                foreach (var e in elem.EnumerateArray()) names.Add(e.GetString() ?? e.ToString());
                if (names.Count > 0) return string.Join(" & ", names);
            }
            return partyItem.Value.ToString() ?? Path.GetFileNameWithoutExtension(fallbackFileName);
        }

        return Path.GetFileNameWithoutExtension(fallbackFileName);
    }
}
