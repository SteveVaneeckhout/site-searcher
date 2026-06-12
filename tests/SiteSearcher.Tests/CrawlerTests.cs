using System.Collections.Concurrent;

namespace SiteSearcher.Tests;

/// <summary>
/// In-process tests of the crawl engine against the Kestrel-served fixture site.
/// </summary>
[TestClass]
public sealed class CrawlerTests
{
    private static string BaseUrl => TestServers.Fixture.BaseUrl;

    private static string[] ExpectedMatches =>
    [
        $"{BaseUrl}/index.html",        // keyword in body text
        $"{BaseUrl}/contact.html",      // uppercase WOMBAT (case-insensitive match)
        $"{BaseUrl}/decoy.html",        // keyword only inside <script> (raw-HTML rule)
        $"{BaseUrl}/blog/post1.html",   // reached via subdirectory, links back with ../
    ];

    private static CrawlOptions Options(int? maxPages = null) => new()
    {
        StartUrl = new Uri($"{BaseUrl}/index.html"),
        Keyword = "wombat",
        MaxPages = maxPages,
    };

    [TestMethod]
    [Timeout(60_000)]
    public async Task FixtureCrawl_FindsExactlyFourMatches()
    {
        using var http = new HttpClient();
        var progress = new ConcurrentQueue<CrawlProgress>();

        var result = await new Crawler(http, Options(), progress.Enqueue).CrawlAsync();

        CollectionAssert.AreEquivalent(ExpectedMatches, result.MatchingUrls.ToArray());
        // The .pdf/.png/.XLSX links are skipped by extension without a request; if that
        // filter regresses they would be fetched (404) and all three counts change.
        Assert.AreEqual(8, result.PagesCrawled);   // 7 fixture files + missing.html
        Assert.AreEqual(7, result.PagesSucceeded); // includes notes.txt (fetched, skipped by content type)
        Assert.AreEqual(1, result.PagesFailed);    // missing.html -> 404
        Assert.IsFalse(result.MaxPagesReached);
        Assert.AreEqual(new CrawlProgress(Searched: 8, Discovered: 8, Matches: 4), progress.Last());
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task MaxPages_StopsAtCap()
    {
        using var http = new HttpClient();

        var result = await new Crawler(http, Options(maxPages: 2)).CrawlAsync();

        Assert.IsTrue(result.MaxPagesReached);
        Assert.AreEqual(2, result.PagesCrawled); // index.html + about.html, third URL trips the cap
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task OnMatch_StreamsEveryMatch()
    {
        using var http = new HttpClient();
        var streamed = new ConcurrentQueue<string>();

        var result = await new Crawler(http, Options(), onProgress: null, onMatch: streamed.Enqueue).CrawlAsync();

        CollectionAssert.AreEquivalent(result.MatchingUrls.ToArray(), streamed.ToArray());
        Assert.HasCount(4, streamed);
    }
}
