using System.Globalization;

namespace PixousDocIntelligence.Pipeline.Normalization;

/// <summary>
/// Compares two display values for semantic (not just literal string) equality, so that the same
/// fact written differently across overlapping batches — "May 15, 2026" vs "05/15/2026", or
/// "USD 840,000" vs "$840,000.00" — is recognized as a match during stitching instead of being
/// treated as a spurious conflict.
/// </summary>
public static class SemanticValueComparer
{
    public static bool AreEquivalent(string? a, string? b)
    {
        string normalizedA = (a ?? string.Empty).Trim();
        string normalizedB = (b ?? string.Empty).Trim();

        if (string.Equals(normalizedA, normalizedB, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (DateTime.TryParse(normalizedA, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateA) &&
            DateTime.TryParse(normalizedB, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateB))
        {
            return dateA.Date == dateB.Date;
        }

        decimal? amountA = TryExtractNumericAmount(normalizedA);
        decimal? amountB = TryExtractNumericAmount(normalizedB);
        if (amountA.HasValue && amountB.HasValue)
        {
            return Math.Abs(amountA.Value - amountB.Value) < 0.005m;
        }

        return false;
    }

    private static decimal? TryExtractNumericAmount(string s)
    {
        string digitsOnly = new(s.Where(c => char.IsDigit(c) || c == '.').ToArray());
        return !string.IsNullOrEmpty(digitsOnly) &&
            decimal.TryParse(digitsOnly, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount)
                ? amount
                : null;
    }
}
