using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using PixousDocIntelligence.Domain.Contracts;
using PixousDocIntelligence.Domain.Planning;

namespace PixousDocIntelligence.Pipeline.Normalization;

/// <summary>
/// Applies the plan's NormalizationRules to extracted values without discarding the original
/// verbatim text: every item and table cell keeps its raw <c>value</c> plus an optional
/// <c>normalized_value</c> computed from it — a reformatted date string, or a structured
/// {amount, currency_code} for a currency amount.
/// </summary>
public static class ValueNormalizer
{
    private static readonly string[] MonthNames =
    {
        "January", "February", "March", "April", "May", "June", "July",
        "August", "September", "October", "November", "December",
        "Jan", "Feb", "Mar", "Apr", "Jun", "Jul", "Aug", "Sep", "Sept", "Oct", "Nov", "Dec"
    };

    private static readonly Regex NumericDatePattern = new(@"\b\d{1,4}[\/\-\.]\d{1,2}[\/\-\.]\d{1,4}\b", RegexOptions.Compiled);

    private static readonly Dictionary<string, string> CurrencySymbols = new()
    {
        ["$"] = "USD",
        ["€"] = "EUR",
        ["£"] = "GBP",
        ["₹"] = "INR",
        ["¥"] = "JPY"
    };

    private static readonly Regex CurrencyCodeFirstPattern = new(
        @"\b(USD|EUR|GBP|INR|JPY|AUD|CAD|CHF|CNY|SGD|AED|ZAR)\b\s*([\d,]+(?:\.\d+)?)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex CurrencyAmountFirstPattern = new(
        @"([\d,]+(?:\.\d+)?)\s*\b(USD|EUR|GBP|INR|JPY|AUD|CAD|CHF|CNY|SGD|AED|ZAR)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex CurrencySymbolPattern = new(
        @"([$€£₹¥])\s*([\d,]+(?:\.\d+)?)",
        RegexOptions.Compiled);

    public static void NormalizeEnvelope(DocumentEnvelope envelope, ExtractionPlan plan)
    {
        string netDateFormat = ToNetDateFormat(plan.NormalizationRules?.DateFormat);

        foreach (var item in envelope.Items)
        {
            item.NormalizedValue = NormalizeItemValue(item.Value, netDateFormat);
        }

        foreach (var table in envelope.Tables)
        {
            foreach (var row in table.Rows)
            {
                foreach (var cell in row.Values)
                {
                    cell.NormalizedValue = NormalizeScalar(GetScalarString(cell.Value), netDateFormat);
                }
            }
        }
    }

    private static object? NormalizeItemValue(object? value, string netDateFormat)
    {
        if (value is JsonElement elem)
        {
            if (elem.ValueKind == JsonValueKind.Array)
            {
                var normalizedEntries = new List<object>();
                bool anyNormalized = false;

                foreach (var entryElem in elem.EnumerateArray())
                {
                    string raw = entryElem.ValueKind == JsonValueKind.String
                        ? entryElem.GetString() ?? string.Empty
                        : entryElem.ToString();
                    object? normalized = NormalizeScalar(raw, netDateFormat);
                    normalizedEntries.Add(normalized ?? raw);
                    if (normalized != null)
                    {
                        anyNormalized = true;
                    }
                }

                return anyNormalized ? normalizedEntries : null;
            }

            return NormalizeScalar(GetScalarString(elem), netDateFormat);
        }

        if (value is string s)
        {
            return NormalizeScalar(s, netDateFormat);
        }

        return null;
    }

    private static string? GetScalarString(object? value)
    {
        if (value is JsonElement elem)
        {
            return elem.ValueKind switch
            {
                JsonValueKind.String => elem.GetString(),
                JsonValueKind.Null or JsonValueKind.Undefined => null,
                _ => elem.ToString()
            };
        }

        return value?.ToString();
    }

    /// <summary>
    /// Attempts to normalize a single scalar value: a date (returned as a reformatted string) or a
    /// currency amount (returned as a structured <see cref="NormalizedCurrency"/>). Returns null
    /// (meaning "no normalization applies") for everything else.
    /// </summary>
    private static object? NormalizeScalar(string? raw, string netDateFormat)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (LooksLikeDate(raw) && DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            return parsed.ToString(netDateFormat, CultureInfo.InvariantCulture);
        }

        return TryNormalizeCurrency(raw);
    }

    private static bool LooksLikeDate(string s)
    {
        if (MonthNames.Any(m => s.Contains(m, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return NumericDatePattern.IsMatch(s);
    }

    /// <summary>
    /// Recognizes "USD 840,000", "840,000 USD", and "$840,000" style amounts and splits them into
    /// a numeric amount plus an ISO-4217 currency code, keeping the original formatted string as
    /// the item's verbatim <c>value</c>.
    /// </summary>
    private static NormalizedCurrency? TryNormalizeCurrency(string raw)
    {
        var codeFirst = CurrencyCodeFirstPattern.Match(raw);
        if (codeFirst.Success)
        {
            return BuildCurrency(codeFirst.Groups[2].Value, codeFirst.Groups[1].Value.ToUpperInvariant());
        }

        var amountFirst = CurrencyAmountFirstPattern.Match(raw);
        if (amountFirst.Success)
        {
            return BuildCurrency(amountFirst.Groups[1].Value, amountFirst.Groups[2].Value.ToUpperInvariant());
        }

        var symbolMatch = CurrencySymbolPattern.Match(raw);
        if (symbolMatch.Success && CurrencySymbols.TryGetValue(symbolMatch.Groups[1].Value, out var code))
        {
            return BuildCurrency(symbolMatch.Groups[2].Value, code);
        }

        return null;
    }

    private static NormalizedCurrency? BuildCurrency(string rawAmount, string currencyCode)
    {
        string cleaned = rawAmount.Replace(",", string.Empty);
        return decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount)
            ? new NormalizedCurrency { Amount = amount, CurrencyCode = currencyCode }
            : null;
    }

    /// <summary>
    /// Converts a human-readable date format token (e.g. "YYYY-MM-DD") into a .NET custom
    /// date format string (e.g. "yyyy-MM-dd"). Falls back to ISO 8601 if the plan didn't specify one.
    /// </summary>
    private static string ToNetDateFormat(string? planFormat)
    {
        if (string.IsNullOrWhiteSpace(planFormat))
        {
            return "yyyy-MM-dd";
        }

        return planFormat
            .Replace("YYYY", "yyyy")
            .Replace("DD", "dd");
    }
}

/// <summary>
/// A currency amount split into a numeric value and its ISO-4217 code, separate from the
/// original formatted string (e.g. "USD 840,000") kept in the item's verbatim value.
/// </summary>
public class NormalizedCurrency
{
    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }

    [JsonPropertyName("currency_code")]
    public string CurrencyCode { get; set; } = string.Empty;
}
