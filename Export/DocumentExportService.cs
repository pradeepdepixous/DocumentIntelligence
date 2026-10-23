using System.Text;
using PixousDocIntelligence.Domain.Contracts;
using PixousDocIntelligence.Pipeline.Verification;

namespace PixousDocIntelligence.Export;

/// <summary>
/// Service that exports stitched records and review queues into CSV and tabular formats.
/// </summary>
public static class DocumentExportService
{
    /// <summary>
    /// Exports the stitched DocumentEnvelope items to a standard RFC 4180 CSV file.
    /// </summary>
    public static string ExportToCsv(DocumentEnvelope envelope)
    {
        var sb = new StringBuilder();
        sb.AppendLine("FieldKey,FieldLabel,Value,ValueType,PageNumber,Confidence,ValidationStatus,SourceEvidence");

        foreach (var item in envelope.Items)
        {
            sb.AppendLine(string.Join(",",
                EscapeCsv(item.FieldKey),
                EscapeCsv(item.FieldLabel),
                EscapeCsv(item.GetDisplayValue()),
                EscapeCsv(item.ValueType),
                EscapeCsv(string.Join(";", item.Sources.Select(s => s.PageNumber))),
                item.Confidence.Value.ToString("F3"),
                EscapeCsv(item.ValidationStatus),
                EscapeCsv(item.PrimarySourceText)
            ));
        }

        return sb.ToString();
    }

    /// <summary>
    /// Exports review tasks to CSV for human-in-the-loop workflows.
    /// </summary>
    public static string ExportReviewQueueToCsv(IEnumerable<ReviewTask> tasks)
    {
        var sb = new StringBuilder();
        sb.AppendLine("TaskId,JobId,FieldKey,PageNumber,Reason,ReportedConfidence,ReportedValue,ReviewerNotes,Status");

        foreach (var task in tasks)
        {
            string valStr = task.ReportedValue?.ToString() ?? string.Empty;
            sb.AppendLine(string.Join(",",
                EscapeCsv(task.TaskId),
                EscapeCsv(task.JobId),
                EscapeCsv(task.FieldKey),
                task.PageNumber,
                EscapeCsv(task.Reason),
                task.ReportedConfidence.ToString("F3"),
                EscapeCsv(valStr),
                EscapeCsv(task.ReviewerNotes ?? string.Empty),
                EscapeCsv(task.Status)
            ));
        }

        return sb.ToString();
    }

    private static string EscapeCsv(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "\"\"";
        string escaped = value.Replace("\"", "\"\"").Replace("\r", " ").Replace("\n", " ");
        return $"\"{escaped}\"";
    }
}
