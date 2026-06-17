using System.Net;
using System.Net.Sockets;

namespace SiteSearcher.Tests;

/// <summary>
/// End-to-end tests that run the real sitesearcher binary as a child process
/// against the Kestrel-served fixture site.
/// </summary>
[TestClass]
public sealed class CliTests
{
    private static string BaseUrl => TestServers.Fixture.BaseUrl;
    private static string StartUrl => $"{BaseUrl}/index.html";

    private static string[] ExpectedMatches =>
    [
        $"{BaseUrl}/index.html",
        $"{BaseUrl}/contact.html",
        $"{BaseUrl}/decoy.html",
        $"{BaseUrl}/blog/post1.html",
    ];

    [TestMethod]
    [Timeout(120_000)]
    public async Task ArgMode_ExportsExactlyFourUrls()
    {
        var export = Path.Combine(Path.GetTempPath(), $"sitesearcher-test-{Guid.NewGuid():N}.txt");
        try
        {
            var run = await AppProcess.RunAsync([StartUrl, "wombat", "-o", export]);

            Assert.AreEqual(0, run.ExitCode, run.Stderr);
            CollectionAssert.AreEquivalent(ExpectedMatches, File.ReadAllLines(export));
            StringAssert.Contains(run.Stdout, "Found 4 page(s) containing \"wombat\"");
            StringAssert.Contains(run.Stdout, "Searched 8 url's (1 failed)");
            foreach (var url in ExpectedMatches)
                StringAssert.Contains(run.Stdout, url); // matches are streamed to the console live
        }
        finally
        {
            File.Delete(export);
        }
    }

    [TestMethod]
    [Timeout(120_000)]
    public async Task PromptMode_ReadsUrlAndKeywordFromStdin()
    {
        var run = await AppProcess.RunAsync([], stdin: $"{StartUrl}\nwombat\n");

        Assert.AreEqual(0, run.ExitCode, run.Stderr);
        StringAssert.Contains(run.Stdout, $"{BaseUrl}/contact.html");
    }

    [TestMethod]
    [Timeout(120_000)]
    public async Task PromptMode_RepromptsOnInvalidUrl()
    {
        var run = await AppProcess.RunAsync([], stdin: $"not a url\n{StartUrl}\nwombat\n");

        Assert.AreEqual(0, run.ExitCode, run.Stderr);
        StringAssert.Contains(run.Stdout, "Please enter a valid http(s) URL.");
        StringAssert.Contains(run.Stdout, $"{BaseUrl}/contact.html");
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task PromptMode_EofDuringPrompts_Exits2()
    {
        var run = await AppProcess.RunAsync([], stdin: "");

        Assert.AreEqual(2, run.ExitCode);
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task InvalidUrlArgument_Exits2()
    {
        var run = await AppProcess.RunAsync([":::bad", "wombat"]);

        Assert.AreEqual(2, run.ExitCode);
        StringAssert.Contains(run.Stderr, "not a valid http(s) URL");
    }

    [TestMethod]
    [Timeout(120_000)]
    public async Task UnreachableSite_Exits1()
    {
        // Grab a free port and release it again: connecting to it is then refused.
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        var run = await AppProcess.RunAsync([$"http://127.0.0.1:{port}/", "wombat"]);

        Assert.AreEqual(1, run.ExitCode);
        StringAssert.Contains(run.Stderr, "Could not retrieve any page");
    }

    [TestMethod]
    [Timeout(120_000)]
    public async Task ZeroMatches_Exits0()
    {
        var run = await AppProcess.RunAsync([StartUrl, "zzznosuchword"]);

        Assert.AreEqual(0, run.ExitCode, run.Stderr);
        StringAssert.Contains(run.Stdout, "No pages found containing");
    }

    [TestMethod]
    [Timeout(120_000)]
    public async Task Fuzzy_FindsMisspelledKeyword()
    {
        // "wombatt" is not a substring on any page, so exact search finds nothing,
        // but fuzzy (--fuzzy) matches the real "wombat"/"WOMBAT" pages within edit distance.
        var exact = await AppProcess.RunAsync([StartUrl, "wombatt"]);
        Assert.AreEqual(0, exact.ExitCode, exact.Stderr);
        StringAssert.Contains(exact.Stdout, "No pages found containing");

        var fuzzy = await AppProcess.RunAsync([StartUrl, "wombatt", "--fuzzy"]);
        Assert.AreEqual(0, fuzzy.ExitCode, fuzzy.Stderr);
        StringAssert.Contains(fuzzy.Stdout, "(fuzzy)");
        StringAssert.Contains(fuzzy.Stdout, $"{BaseUrl}/contact.html");
    }

    [TestMethod]
    [Timeout(60_000)]
    public async Task Help_ListsFuzzyOption()
    {
        var run = await AppProcess.RunAsync(["--help"]);

        Assert.AreEqual(0, run.ExitCode, run.Stderr);
        StringAssert.Contains(run.Stdout, "--fuzzy");
    }

    [TestMethod]
    [Timeout(120_000)]
    public async Task MaxPages_PrintsLimitNotice()
    {
        var run = await AppProcess.RunAsync([StartUrl, "wombat", "--max-pages", "2"]);

        Assert.AreEqual(0, run.ExitCode, run.Stderr);
        StringAssert.Contains(run.Stdout, "--max-pages limit (2)");
    }
}
