using HtmlAgilityPack;

namespace SiteSearcher.Tests;

/// <summary>Unit tests for the crawler's URL handling rules (via InternalsVisibleTo).</summary>
[TestClass]
public sealed class UrlRulesTests
{
    private static readonly Uri Base = new("http://example.com/blog/post.html");

    [TestMethod]
    [DataRow("mailto:someone@example.com")]
    [DataRow("tel:+3212345678")]
    [DataRow("javascript:void(0)")]
    [DataRow("data:text/plain,hello")]
    [DataRow("#section")]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("ftp://example.com/file.zip")]
    public void TryNormalize_RejectsNonCrawlableLinks(string href)
        => Assert.IsFalse(Crawler.TryNormalize(Base, href, out _));

    [TestMethod]
    [DataRow("report.pdf")]
    [DataRow("/files/photo.JPG")]
    [DataRow("logo.png?v=2")]
    [DataRow("cv.docx")]
    [DataRow("old.doc")]
    [DataRow("data.xls")]
    [DataRow("Sheet.XLSX?download=1")]
    [DataRow("archive.zip")]
    [DataRow("styles.css")]
    [DataRow("video.mp4#t=10")]
    public void TryNormalize_RejectsKnownNonHtmlFileExtensions(string href)
        => Assert.IsFalse(Crawler.TryNormalize(Base, href, out _));

    [TestMethod]
    [DataRow("page.html")]
    [DataRow("page.php")]
    [DataRow("page")]
    [DataRow("page.html?id=1")]
    [DataRow("/downloads/")]
    public void TryNormalize_AcceptsHtmlAndExtensionlessPaths(string href)
        => Assert.IsTrue(Crawler.TryNormalize(Base, href, out _));

    [TestMethod]
    public void TryNormalize_ResolvesParentRelativePaths()
    {
        Assert.IsTrue(Crawler.TryNormalize(Base, "../about.html", out var uri));
        Assert.AreEqual("http://example.com/about.html", uri.AbsoluteUri);
    }

    [TestMethod]
    public void TryNormalize_ResolvesRootRelativePaths()
    {
        Assert.IsTrue(Crawler.TryNormalize(Base, "/contact.html", out var uri));
        Assert.AreEqual("http://example.com/contact.html", uri.AbsoluteUri);
    }

    [TestMethod]
    public void TryNormalize_StripsFragmentAndDecodesEntities()
    {
        Assert.IsTrue(Crawler.TryNormalize(Base, "page.html?a=1&amp;b=2#frag", out var uri));
        Assert.AreEqual("http://example.com/blog/page.html?a=1&b=2", uri.AbsoluteUri);
    }

    [TestMethod]
    public void CanonicalHost_IgnoresCaseAndWwwPrefix()
    {
        Assert.AreEqual("example.com", Crawler.CanonicalHost(new Uri("https://WWW.Example.COM/x")));
        Assert.AreEqual("example.com", Crawler.CanonicalHost(new Uri("http://example.com")));
        Assert.AreEqual("sub.example.com", Crawler.CanonicalHost(new Uri("https://sub.example.com/")));
    }

    [TestMethod]
    public void StripFragment_KeepsQueryString()
    {
        Assert.AreEqual(
            "https://example.com/p?q=1",
            Crawler.StripFragment(new Uri("https://example.com/p?q=1#sec")).AbsoluteUri);
    }

    [TestMethod]
    public void ExtractHrefs_WithoutAnchors_ReturnsEmpty()
    {
        var doc = new HtmlDocument();
        doc.LoadHtml("<html><body><p>no links here</p></body></html>");
        Assert.IsEmpty(Crawler.ExtractHrefs(doc));
    }

    [TestMethod]
    [DataRow("<p>Sailing with P&amp;O today</p>")]  // named entity
    [DataRow("<p>Sailing with P&#38;O today</p>")]  // decimal entity
    [DataRow("<p>Sailing with P&#x26;O today</p>")] // hex entity
    [DataRow("<p>Sailing with P&O today</p>")]      // literal ampersand
    public void ContainsKeyword_MatchesEntityEncodedAmpersand(string html)
        => Assert.IsTrue(Crawler.ContainsKeyword(html, "P&O"));

    [TestMethod]
    public void ContainsKeyword_IsCaseInsensitive()
        => Assert.IsTrue(Crawler.ContainsKeyword("<p>book with p&amp;o</p>", "P&O"));

    [TestMethod]
    public void ContainsKeyword_MatchesInsideRawHtmlLikeScripts()
        => Assert.IsTrue(Crawler.ContainsKeyword("<script>var a=\"wombat\";</script>", "wombat"));

    [TestMethod]
    public void ContainsKeyword_ReturnsFalseWhenAbsent()
        => Assert.IsFalse(Crawler.ContainsKeyword("<p>nothing here</p>", "P&O"));
}
