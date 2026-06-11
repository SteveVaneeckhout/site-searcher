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

        // With -o, matches are written to the file the moment they are found, so the
        // file is as complete as the console even when the user quits mid-scan.
        TextWriter? exportWriter = null;
        if (parsed.OutputFile is not null)
        {
            try
            {
                exportWriter = TextWriter.Synchronized(new StreamWriter(parsed.OutputFile) { AutoFlush = true });
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Could not open \"{parsed.OutputFile}\" for writing: {ex.Message}");
                return 2;
            }
        }

        Console.WriteLine($"Searching for \"{keyword}\" on {url}");
        Console.WriteLine("Matching URL's are printed as they are found; press Ctrl+C to quit at any time.");
        Console.WriteLine();

        var status = Console.IsOutputRedirected ? null : new StatusPrinter();

        Action<string> onMatch = match =>
        {
            if (status is not null)
                status.WriteLineAbove(match);
            else
                Console.WriteLine(match);
            exportWriter?.WriteLine(match);
        };

        Action<CrawlProgress>? onProgress = status is null
            ? null
            : p => status.Update($"{p.Searched}/{p.Discovered} url's searched for '{keyword}'");

        status?.Update($"0/1 url's searched for '{keyword}'");

        var options = new CrawlOptions { StartUrl = url, Keyword = keyword, MaxPages = parsed.MaxPages };
        using var http = CreateHttpClient(options);
        var result = await new Crawler(http, options, onProgress, onMatch).CrawlAsync();

        status?.Finish();
        exportWriter?.Dispose();

        if (result.PagesSucceeded == 0)
        {
            Console.Error.WriteLine(
                $"Could not retrieve any page from {url} ({result.PagesFailed} request(s) failed).");
            return 1;
        }

        Console.WriteLine();
        Console.WriteLine(result.MatchingUrls.Count == 0
            ? $"No pages found containing \"{keyword}\"."
            : $"Found {result.MatchingUrls.Count} page(s) containing \"{keyword}\".");
        Console.WriteLine($"Searched {result.PagesCrawled} url's ({result.PagesFailed} failed).");
        if (result.MaxPagesReached)
            Console.WriteLine($"Stopped at the --max-pages limit ({parsed.MaxPages}).");

        if (parsed.OutputFile is not null)
            Console.WriteLine($"Results written to {Path.GetFullPath(parsed.OutputFile)}");
        else
            OfferInteractiveExport(result.MatchingUrls);

        return 0;
    }

    /// <summary>
    /// Keeps a single rewritten status line at the bottom of the terminal while
    /// match URLs are printed as normal lines above it.
    /// </summary>
    private sealed class StatusPrinter
    {
        private readonly object _gate = new();
        private string _status = "";

        public void Update(string newStatus)
        {
            lock (_gate)
            {
                Console.Write("\r" + newStatus.PadRight(_status.Length));
                _status = newStatus;
            }
        }

        public void WriteLineAbove(string line)
        {
            lock (_gate)
            {
                Console.Write("\r" + line.PadRight(_status.Length) + Environment.NewLine + _status);
            }
        }

        public void Finish()
        {
            lock (_gate)
            {
                if (_status.Length > 0)
                    Console.WriteLine();
                _status = "";
            }
        }
    }

    private static void OfferInteractiveExport(IReadOnlyList<string> urls)
    {
        if (urls.Count == 0 || Console.IsInputRedirected)
            return;

        Console.Write("Export the results to a .txt file? (y/N) ");
        var answer = Console.ReadLine()?.Trim();
        if (answer is null || (!answer.StartsWith('y') && !answer.StartsWith('Y')))
            return;

        Console.Write($"File name [{DefaultExportFile}]: ");
        var name = Console.ReadLine()?.Trim();
        var path = string.IsNullOrEmpty(name) ? DefaultExportFile : name;

        try
        {
            ResultExporter.WriteTo(path, urls);
            Console.WriteLine($"Results written to {Path.GetFullPath(path)}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Could not write \"{path}\": {ex.Message}");
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
              -o, --output <file>   Write each matching URL to <file> (one per line, written as found)
              --max-pages <n>       Stop after fetching <n> pages (default: unlimited)
              -h, --help            Show this help

            Matching URL's are printed the moment they are found, so it is safe to quit
            with Ctrl+C at any time — everything found so far is already on screen.
            """);
    }
}
