using System.Diagnostics.CodeAnalysis;
using System.Net;

namespace SiteSearcher;

internal static class Program
{
    private const string DefaultExportFile = "sitesearcher-results.txt";

    private static async Task<int> Main(string[] args)
    {
        var parsed = ParseArgs(args, out var error);
        if (parsed is null)
        {
            Console.Error.WriteLine(error);
            PrintUsage(Console.Error);
            return 2;
        }
        if (parsed.ShowHelp)
        {
            PrintUsage(Console.Out);
            return 0;
        }

        var url = parsed.Url ?? PromptForUrl();
        if (url is null)
        {
            Console.Error.WriteLine("No URL provided.");
            return 2;
        }

        var keyword = parsed.Keyword ?? PromptForKeyword();
        if (keyword is null)
        {
            Console.Error.WriteLine("No search word provided.");
            return 2;
        }

        var options = new CrawlOptions
        {
            StartUrl = url,
            Keyword = keyword,
            MaxPages = parsed.MaxPages,
        };

        using var cts = new CancellationTokenSource();
        var interruptRequested = false;
        Console.CancelKeyPress += (_, e) =>
        {
            if (interruptRequested)
                return; // second Ctrl+C terminates immediately
            interruptRequested = true;
            e.Cancel = true;
            cts.Cancel();
        };

        var showProgress = !Console.IsOutputRedirected;
        Action<CrawlProgress>? onProgress = null;
        if (showProgress)
        {
            var progressLock = new object();
            onProgress = p =>
            {
                var line = $"Searched {p.Crawled} page(s) | pending {p.Pending} | {p.Matches} match(es)";
                lock (progressLock)
                {
                    Console.Write("\r" + line.PadRight(78));
                }
            };
        }

        Console.WriteLine($"Searching for \"{keyword}\" on {url}");
        Console.WriteLine("Press Ctrl+C to stop and show the results found so far.");
        Console.WriteLine();

        using var http = CreateHttpClient(options);
        var crawler = new Crawler(http, options, onProgress);
        var result = await crawler.CrawlAsync(cts.Token);

        if (showProgress)
            Console.WriteLine();

        if (result.Interrupted)
            Console.WriteLine("Scan interrupted — results so far:");

        if (result.PagesSucceeded == 0)
        {
            Console.Error.WriteLine(
                $"Could not retrieve any page from {url} ({result.PagesFailed} request(s) failed).");
            return 1;
        }

        Console.WriteLine();
        if (result.MatchingUrls.Count == 0)
        {
            Console.WriteLine($"No pages found containing \"{keyword}\".");
        }
        else
        {
            Console.WriteLine($"Found {result.MatchingUrls.Count} page(s) containing \"{keyword}\":");
            foreach (var match in result.MatchingUrls)
                Console.WriteLine(match);
        }

        Console.WriteLine();
        Console.WriteLine($"Searched {result.PagesCrawled} page(s) ({result.PagesFailed} failed).");
        if (result.MaxPagesReached)
            Console.WriteLine($"Stopped at the --max-pages limit ({parsed.MaxPages}).");

        ExportResults(parsed.OutputFile, result.MatchingUrls);
        return 0;
    }

    private static void ExportResults(string? outputFile, IReadOnlyList<string> urls)
    {
        if (outputFile is null)
        {
            // Offer the export interactively, but never block when stdin is a pipe/file.
            if (urls.Count == 0 || Console.IsInputRedirected)
                return;

            Console.Write("Export the results to a .txt file? (y/N) ");
            var answer = Console.ReadLine();
            if (answer is null || !answer.Trim().StartsWith('y') && !answer.Trim().StartsWith('Y'))
                return;

            Console.Write($"File name [{DefaultExportFile}]: ");
            var name = Console.ReadLine()?.Trim();
            outputFile = string.IsNullOrEmpty(name) ? DefaultExportFile : name;
        }

        try
        {
            ResultExporter.WriteTo(outputFile, urls);
            Console.WriteLine($"Results written to {Path.GetFullPath(outputFile)}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Could not write \"{outputFile}\": {ex.Message}");
        }
    }

    private static HttpClient CreateHttpClient(CrawlOptions options)
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 10,
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(10),
        };
        var http = new HttpClient(handler) { Timeout = options.RequestTimeout };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent);
        http.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,*/*;q=0.8");
        return http;
    }

    private sealed record CliArgs(Uri? Url, string? Keyword, string? OutputFile, int? MaxPages, bool ShowHelp);

    private static CliArgs? ParseArgs(string[] args, out string? error)
    {
        error = null;
        string? output = null;
        int? maxPages = null;
        var positionals = new List<string>();

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-h" or "--help" or "/?":
                    return new CliArgs(null, null, null, null, ShowHelp: true);

                case "-o" or "--output":
                    if (++i >= args.Length)
                    {
                        error = $"Missing file name after {args[i - 1]}.";
                        return null;
                    }
                    output = args[i];
                    break;

                case "--max-pages":
                    if (++i >= args.Length || !int.TryParse(args[i], out var cap) || cap < 1)
                    {
                        error = "--max-pages expects a positive number.";
                        return null;
                    }
                    maxPages = cap;
                    break;

                case var flag when flag.StartsWith('-') && flag.Length > 1:
                    error = $"Unknown option: {flag}";
                    return null;

                default:
                    positionals.Add(args[i]);
                    break;
            }
        }

        if (positionals.Count > 2)
        {
            error = "Too many arguments.";
            return null;
        }

        Uri? url = null;
        if (positionals.Count >= 1 && !TryParseUrl(positionals[0], out url))
        {
            error = $"\"{positionals[0]}\" is not a valid http(s) URL.";
            return null;
        }

        var keyword = positionals.Count == 2 ? positionals[1] : null;
        return new CliArgs(url, keyword, output, maxPages, ShowHelp: false);
    }

    private static bool TryParseUrl(string input, [NotNullWhen(true)] out Uri? uri)
    {
        uri = null;
        input = input.Trim();
        if (input.Length == 0)
            return false;
        if (!input.Contains("://", StringComparison.Ordinal))
            input = "https://" + input;

        return Uri.TryCreate(input, UriKind.Absolute, out uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            && uri.Host.Length > 0;
    }

    private static Uri? PromptForUrl()
    {
        while (true)
        {
            Console.Write("Website URL (e.g. https://example.com): ");
            var line = Console.ReadLine();
            if (line is null)
                return null; // stdin closed
            if (TryParseUrl(line, out var uri))
                return uri;
            Console.WriteLine("Please enter a valid http(s) URL.");
        }
    }

    private static string? PromptForKeyword()
    {
        while (true)
        {
            Console.Write("Search word: ");
            var line = Console.ReadLine();
            if (line is null)
                return null; // stdin closed
            line = line.Trim();
            if (line.Length > 0)
                return line;
            Console.WriteLine("Please enter a non-empty search word.");
        }
    }

    private static void PrintUsage(TextWriter writer)
    {
        writer.WriteLine("""
            sitesearcher — find all pages on a website that contain a search word

            Usage:
              sitesearcher [<url> <searchword>] [options]

            Arguments:
              <url>         Start URL; the crawl stays on this site (https:// assumed if omitted)
              <searchword>  Word to look for in each page's HTML (case-insensitive)
              Both are asked for interactively when not passed on the command line.

            Options:
              -o, --output <file>   Write the matching URLs to a text file, one per line
              --max-pages <n>       Stop after fetching <n> pages (default: unlimited)
              -h, --help            Show this help

            Press Ctrl+C while scanning to stop and show the results found so far.
            """);
    }
}
