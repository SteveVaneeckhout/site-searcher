namespace SiteSearcher.Tests;

/// <summary>Unit tests for the opt-in fuzzy keyword matcher.</summary>
[TestClass]
public sealed class FuzzyMatchTests
{
    [TestMethod]
    [DataRow("<p>I saw a wombat today</p>")]   // exact still matches
    [DataRow("<p>I saw a wombta today</p>")]   // adjacent transposition (distance 2, within tol 2)
    [DataRow("<p>I saw a wombatt today</p>")]  // extra letter (distance 1)
    [DataRow("<p>I saw a wombet today</p>")]   // substitution (distance 1)
    public void ContainsFuzzy_MatchesSingleCharTypos(string html)
        => Assert.IsTrue(FuzzyMatcher.ContainsFuzzy(html, "wombat"));

    [TestMethod]
    public void ContainsFuzzy_IsCaseInsensitive()
        => Assert.IsTrue(FuzzyMatcher.ContainsFuzzy("<p>A WOMBTA appeared</p>", "wombat"));

    [TestMethod]
    public void ContainsFuzzy_MatchesInsideRawHtmlLikeScripts()
        => Assert.IsTrue(FuzzyMatcher.ContainsFuzzy("<script>var a=\"wombta\";</script>", "wombat"));

    [TestMethod]
    [DataRow("<p>a kangaroo hopped by</p>")]   // unrelated word
    [DataRow("<p>a wmt blur</p>")]             // distance 3, beyond the tol-2 budget for "wombat"
    public void ContainsFuzzy_RejectsWordsBeyondThreshold(string html)
        => Assert.IsFalse(FuzzyMatcher.ContainsFuzzy(html, "wombat"));

    [TestMethod]
    public void ContainsFuzzy_ShortTermsRequireExactMatch()
    {
        // "go" has length 2, so the allowed distance is 0: a typo must not match.
        Assert.IsTrue(FuzzyMatcher.ContainsFuzzy("<p>let's go now</p>", "go"));
        Assert.IsFalse(FuzzyMatcher.ContainsFuzzy("<p>the god of war</p>", "go"));
    }

    [TestMethod]
    public void ContainsFuzzy_MultiWordKeywordRequiresAllTerms()
    {
        // Both terms present (each within tolerance), order-independent.
        Assert.IsTrue(FuzzyMatcher.ContainsFuzzy("<p>a hungry wombta in the garden</p>", "hungry wombat"));
        // Only one term present -> no match.
        Assert.IsFalse(FuzzyMatcher.ContainsFuzzy("<p>a sleepy wombat</p>", "hungry wombat"));
    }

    [TestMethod]
    public void ContainsFuzzy_MatchesAcrossEntityEncoding()
        => Assert.IsTrue(FuzzyMatcher.ContainsFuzzy("<p>caf&eacute; latte</p>", "cafe"));

    [TestMethod]
    [DataRow(1, 0)]   // length 1 -> exact
    [DataRow(2, 0)]   // length 2 -> exact
    [DataRow(3, 1)]
    [DataRow(5, 1)]
    [DataRow(6, 2)]
    [DataRow(12, 2)]
    public void MaxDistanceFor_ScalesWithLength(int length, int expected)
        => Assert.AreEqual(expected, FuzzyMatcher.MaxDistanceFor(new string('x', length)));
}
