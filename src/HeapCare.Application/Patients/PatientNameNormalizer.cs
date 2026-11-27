using System.Text;
using System.Text.RegularExpressions;

namespace HeapCare.Application.Patients;

/// <summary>
/// Deterministic, locale-independent normalization for patient name comparison.
/// Version 1 applies Unicode compatibility normalization, invariant uppercase,
/// and whitespace collapsing. It intentionally preserves punctuation and
/// diacritics; phonetic/transliteration matching is a separate future decision.
/// </summary>
public static partial class PatientNameNormalizer
{
    public const string Version = "NFKC-INVARIANT-UPPER-WHITESPACE-v1";

    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var compatibilityNormalized = value.Normalize(NormalizationForm.FormKC);
        var collapsedWhitespace = WhitespacePattern().Replace(compatibilityNormalized, " ").Trim();
        return collapsedWhitespace.Length == 0
            ? null
            : collapsedWhitespace.ToUpperInvariant();
    }

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespacePattern();
}
