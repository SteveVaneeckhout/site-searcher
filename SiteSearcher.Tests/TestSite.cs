using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;

namespace SiteSearcher.Tests;

/// <summary>
/// In-process Kestrel server that serves the static fixture site from the test
/// output directory on a dynamic localhost port.
/// </summary>
public sealed class TestSite : IAsyncDisposable
{
    private readonly WebApplication _app;

    private TestSite(WebApplication app) => _app = app;

    /// <summary>Base address including the bound port, e.g. "http://127.0.0.1:43521".</summary>
    public string BaseUrl { get; private set; } = "";

    public static async Task<TestSite> StartFixtureAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(o => o.Listen(IPAddress.Loopback, 0));

        var app = builder.Build();
        // Same semantics the crawler meets in the wild: .html -> text/html,
        // .txt -> text/plain, unknown paths -> 404.
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(Path.Combine(AppContext.BaseDirectory, "fixture")),
        });

        var site = new TestSite(app);
        await app.StartAsync();
        site.BaseUrl = app.Urls.Single().TrimEnd('/');
        return site;
    }

    public async ValueTask DisposeAsync() => await _app.DisposeAsync();
}

[TestClass]
public sealed class TestServers
{
    public static TestSite Fixture { get; private set; } = null!;

    [AssemblyInitialize]
    public static async Task Init(TestContext _) => Fixture = await TestSite.StartFixtureAsync();

    [AssemblyCleanup]
    public static async Task Cleanup() => await Fixture.DisposeAsync();
}
