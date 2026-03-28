using System.Text.RegularExpressions;

namespace BoAliSina.IcdImporter.Services;

public interface ISymptomNormalizationService
{
    string Normalize(string symptom);
}

public class SymptomNormalizationService : ISymptomNormalizationService
{
    private static readonly Regex PunctuationRegex = new(@"[^\w\s]", RegexOptions.Compiled);
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "with", "due", "to", "and", "or", "of", "the", "a", "an", "in", "on", "at", "by", "for", "from"
    };

    public string Normalize(string symptom)
    {
        if (string.IsNullOrWhiteSpace(symptom)) return string.Empty;

        // 1. Lowercase and remove punctuation
        var clean = PunctuationRegex.Replace(symptom.ToLowerInvariant(), " ");

        // 2. Tokenize and remove stop words
        var tokens = clean.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(t => !StopWords.Contains(t));

        // 3. Rejoin and trim (Normalization for search)
        return string.Join(" ", tokens).Trim();
    }
}
