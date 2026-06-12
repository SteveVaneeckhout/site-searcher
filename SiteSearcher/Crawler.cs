using HtmlAgilityPack;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Channels;

namespace SiteSearcher;

/// <summary>
/// Breadth-first crawler that visits every reachable page on the start URL's host
/// and records the URLs of pages whose raw HTML contains the keyword. Matches are
/// reported through <paramref name="onMatch"/> the moment they are found, so the
/// caller always has the complete list so far even if the process is killed.
/// </summary>
public sealed class Crawler(
    HttpClient http,
    CrawlOptions options,
    Action<CrawlProgress>? onProgress = null,
    Action<string>? onMatch = null)
{
    private readonly Channel<Uri> _queue = Channel.CreateUnbounded<Uri>();
    private readonly ConcurrentDictionary<string, byte> _visited = new();
    private readonly ConcurrentQueue<string> _matches = new();
    private readonly string _siteHost = CanonicalHost(options.StartUrl);

    private int _scheduled;
    private int _discovered;
    private int _inFlight;
    private int _succeeded;
    private int _failed;
    private int _matchCount;
    private volatile bool _capHit;

    public async Task<CrawlResult> CrawlAsync()
    {
        TryEnqueue(StripFragment(options.StartUrl));

        var workers = Enumerable.Range(0, options.MaxConcurrency)
            .Select(_ => Task.Run(WorkerAsync))
            .ToArray();
        await Task.WhenAll(workers);

        var succeeded = Volatile.Read(ref _succeeded);
        var failed = Volatile.Read(ref _failed);
        return new CrawlResult(
            MatchingUrls: _matches.ToList(),
            PagesCrawled: succeeded + failed,
            PagesSucceeded: succeeded,
            PagesFailed: failed,
            MaxPagesReached: _capHit);
    }

    private async Task WorkerAsync()
    {
        await foreach (var url in _queue.Reader.ReadAllAsync())
        {
            try
            {
                await ProcessPageAsync(url);
            }
            catch
            {
                System.Diagnostics.Debug.WriteLine($"Failed to process page: {url}");
                // A page that cannot be fetched or parsed never stops the crawl.
                Interlocked.Increment(ref _failed);
            }
            finally
            {
                if (Interlocked.Decrement(ref _inFlight) == 0)
                    _queue.Writer.TryComplete();
                ReportProgress();
            }
        }
    }

    private async Task ProcessPageAsync(Uri url)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

        if (!response.IsSuccessStatusCode)
        {
            Interlocked.Increment(ref _failed);
            System.Diagnostics.Debug.WriteLine($"Failed to fetch page: {url}");
            return;
        }

        var mediaType = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant();
        if (mediaType is not ("text/html" or "application/xhtml+xml"))
        {
            Interlocked.Increment(ref _succeeded);
            return;
        }

        // Redirects are followed automatically; remember where we actually landed so
        // the same page is not fetched again under its final URL, and stay on the site.
        var finalUri = StripFragment(response.RequestMessage?.RequestUri ?? url);
        if (finalUri.AbsoluteUri != url.AbsoluteUri)
            _visited.TryAdd(finalUri.AbsoluteUri, 0);
        if (CanonicalHost(finalUri) != _siteHost)
        {
            Interlocked.Increment(ref _succeeded);
            return;
        }

        var html = await response.Content.ReadAsStringAsync();
        Interlocked.Increment(ref _succeeded);

        if (html.Contains(options.Keyword, StringComparison.OrdinalIgnoreCase))
        {
            _matches.Enqueue(url.AbsoluteUri);
            Interlocked.Increment(ref _matchCount);
            onMatch?.Invoke(url.AbsoluteUri);
        }

        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        foreach (var href in ExtractHrefs(doc))
        {
            if (TryNormalize(finalUri, href, out var link) && CanonicalHost(link) == _siteHost)
                TryEnqueue(link);
        }
    }

    /// <summary>
    /// Adds a URL to the crawl queue unless it was already seen or the page cap is hit.
    /// The in-flight count must be incremented before the channel write so the queue
    /// can only complete when nothing is queued and nothing is being processed.
    /// </summary>
    private bool TryEnqueue(Uri url)
    {
        if (!_visited.TryAdd(url.AbsoluteUri, 0))
            return false;

        if (options.MaxPages is int cap && Interlocked.Increment(ref _scheduled) > cap)
        {
            _capHit = true;
            return false;
        }

        Interlocked.Increment(ref _inFlight);
        Interlocked.Increment(ref _discovered);
        return _queue.Writer.TryWrite(url);
    }

    private void ReportProgress()
    {
        onProgress?.Invoke(new CrawlProgress(
            Volatile.Read(ref _succeeded) + Volatile.Read(ref _failed),
            Volatile.Read(ref _discovered),
            Volatile.Read(ref _matchCount)));
    }

    /// <summary>Host identity used for the same-site check: lowercased, "www." ignored.</summary>
    public static string CanonicalHost(Uri uri)
    {
        var host = uri.Host.ToLowerInvariant();
        return host.StartsWith("www.", StringComparison.Ordinal) ? host[4..] : host;
    }

    public static bool TryNormalize(Uri baseUri, string rawHref, [NotNullWhen(true)] out Uri? normalized)
    {
        normalized = null;

        var href = HtmlEntity.DeEntitize(rawHref)?.Trim();
        if (string.IsNullOrEmpty(href) || href.StartsWith('#'))
            return false;

        if (href.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ||
            href.StartsWith("tel:", StringComparison.OrdinalIgnoreCase) ||
            href.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase) ||
            href.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            return false;

        if (!Uri.TryCreate(baseUri, href, out var absolute))
            return false;
        if (absolute.Scheme != Uri.UriSchemeHttp && absolute.Scheme != Uri.UriSchemeHttps)
            return false;

        normalized = StripFragment(absolute);
        return true;
    }

    public static Uri StripFragment(Uri uri)
        => string.IsNullOrEmpty(uri.Fragment) ? uri : new Uri(uri.GetLeftPart(UriPartial.Query));

    public static List<string> ExtractHrefs(HtmlDocument doc)
    {
        var anchors = doc.DocumentNode.SelectNodes("//a[@href]");
        if (anchors is null)
            return [];

        return anchors
            .Select(a => a.GetAttributeValue("href", ""))
            .Where(href => href.Length > 0)
            .ToList();
    }
}
