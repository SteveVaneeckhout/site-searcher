namespace SiteSearcher;

public sealed record CrawlOptions
{
    public required Uri StartUrl { get; init; }
    public required string Keyword { get; init; }

    /// <summary>Maximum number of pages to fetch; null means unlimited.</summary>
    public int? MaxPages { get; init; }

    public int MaxConcurrency { get; init; } = 8;
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(10);
    public string UserAgent { get; init; } =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:151.0) Gecko/20100101 Firefox/151.0";
}

public sealed record CrawlResult(
    IReadOnlyList<string> MatchingUrls,
    int PagesCrawled,
    int PagesSucceeded,
    int PagesFailed,
    bool MaxPagesReached,
    bool Interrupted);

public readonly record struct CrawlProgress(int Crawled, int Pending, int Matches);
