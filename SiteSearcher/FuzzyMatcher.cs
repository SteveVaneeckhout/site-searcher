using HtmlAgilityPack;

namespace SiteSearcher;

/// <summary>
/// Opt-in fuzzy keyword matching, used when <c>--fuzzy</c> is set. Exact search
/// (<see cref="Crawler.ContainsKeyword"/>) treats the page as one blob and looks for a
/// substring; edit distance has no meaning against a whole document, so it needs word
/// boundaries instead. Each whitespace-separated term of the keyword is compared against
/// every word (a maximal run of letters/digits) in the page within a length-based
/// Levenshtein tolerance. Like the exact matcher, the raw HTML is searched — so matches in
/// markup, attributes and scripts still count — falling back to an entity-decoded copy.
/// </summary>
public static class FuzzyMatcher
{
    /// <summary>
    /// True when every term of <paramref name="keyword"/> fuzzy-matches some word in the
    /// page. A single-word keyword therefore matches if any page word is close enough; a
    /// multi-word keyword requires each term to be found (order-independent).
    /// </summary>
    public static bool ContainsFuzzy(string html, string keyword)
    {
        var terms = keyword.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (terms.Length == 0)
            return false;

        if (MatchesAllTerms(html, terms))
            return true;

        // Mirror ContainsKeyword's second pass; skip it when decoding changed nothing.
        var decoded = HtmlEntity.DeEntitize(html);
        return decoded is not null
            && !string.Equals(decoded, html, StringComparison.Ordinal)
            && MatchesAllTerms(decoded, terms);
    }

    /// <summary>
    /// Allowed edit distance for a term: short terms stay strict so fuzzing does not turn
    /// into noise (a 2-letter term must match exactly), longer terms tolerate more typos.
    /// </summary>
    public static int MaxDistanceFor(string term) => term.Length switch
    {
        <= 2 => 0,
        <= 5 => 1,
        _ => 2,
    };

    private static bool MatchesAllTerms(string text, string[] terms)
    {
        var matched = new bool[terms.Length];
        var remaining = terms.Length;

        var i = 0;
        while (i < text.Length)
        {
            if (!char.IsLetterOrDigit(text[i]))
            {
                i++;
                continue;
            }

            var start = i;
            while (i < text.Length && char.IsLetterOrDigit(text[i]))
                i++;
            var word = text.AsSpan(start, i - start);

            for (var t = 0; t < terms.Length; t++)
            {
                if (matched[t] || !WithinDistance(word, terms[t], MaxDistanceFor(terms[t])))
                    continue;

                matched[t] = true;
                if (--remaining == 0)
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Case-insensitive Levenshtein distance with an early exit: returns true as soon as the
    /// distance between <paramref name="word"/> and <paramref name="term"/> is known to be at
    /// most <paramref name="max"/>. The DP rows are sized by the (short) keyword term.
    /// </summary>
    private static bool WithinDistance(ReadOnlySpan<char> word, ReadOnlySpan<char> term, int max)
    {
        if (max == 0)
            return word.Equals(term, StringComparison.OrdinalIgnoreCase);

        if (Math.Abs(word.Length - term.Length) > max)
            return false;

        Span<int> prev = stackalloc int[term.Length + 1];
        Span<int> curr = stackalloc int[term.Length + 1];
        for (var j = 0; j <= term.Length; j++)
            prev[j] = j;

        for (var i = 1; i <= word.Length; i++)
        {
            curr[0] = i;
            var rowMin = curr[0];
            var wc = char.ToLowerInvariant(word[i - 1]);

            for (var j = 1; j <= term.Length; j++)
            {
                var cost = wc == char.ToLowerInvariant(term[j - 1]) ? 0 : 1;
                var v = Math.Min(Math.Min(prev[j] + 1, curr[j - 1] + 1), prev[j - 1] + cost);
                curr[j] = v;
                if (v < rowMin)
                    rowMin = v;
            }

            if (rowMin > max)
                return false;

            var tmp = prev;
            prev = curr;
            curr = tmp;
        }

        return prev[term.Length] <= max;
    }
}
