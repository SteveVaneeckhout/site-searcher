# sitesearcher

A small .NET 10 command-line tool that crawls a website and lists every page
on that site whose HTML contains a search word.

Matching URLs are printed **the moment they are found**, with a live status
line at the bottom, so you can watch the results come in and quit whenever
you have seen enough:

```text
> sitesearcher.exe https://example.com searchword

Searching for "searchword" on https://example.com/
Matching URL's are printed as they are found; press Ctrl+C to quit at any time.

https://example.com/
https://example.com/about
https://example.com/blog/some-post
200/436 url's searched for 'searchword'
```

When the scan finishes:

```text
436/436 url's searched for 'searchword'

Found 9 page(s) containing "searchword".
Searched 436 url's (3 failed).
Export the results to a .txt file? (y/N)
```

## Usage

```text
sitesearcher [<url> <searchword>] [options]

Arguments:
  <url>         Start URL; the crawl stays on this site (https:// assumed if omitted)
  <searchword>  Word to look for in each page's HTML (case-insensitive)

Options:
  -o, --output <file>   Write each matching URL to <file> (one per line, written as found)
  --max-pages <n>       Stop after fetching <n> pages (default: unlimited)
  -h, --help            Show help
```

When the URL or search word is not passed on the command line, the app asks
for them at startup:

```text
> sitesearcher.exe
Website URL (e.g. https://example.com): example.com
Search word: searchword
```

### Quitting early

The crawl runs until it has visited every reachable page on the site. Since
every match is printed (and exported) as soon as it is found, pressing
**Ctrl+C** simply quits — everything found up to that point is already on
screen, and the status line shows how far the scan got.

### Exporting results

- Pass `-o results.txt` (or `--output results.txt`) to write matching URLs to
  a file. The file is written live, one URL per line (UTF-8), so it also
  contains everything found so far if you quit mid-scan.
- Without `-o`, the app offers to export after a completed scan when anything
  was found (default file name: `sitesearcher-results.txt`).

## How it works

- Breadth-first crawl starting at the given URL, following `<a href>` links.
- Stays on the start URL's site: same host, with `www.` ignored
  (`example.com` and `www.example.com` are treated as the same site).
- The search word is matched **case-insensitively against the raw HTML** of
  each page, so occurrences in markup, attributes and scripts count too.
- Links to files with a known non-HTML extension (`.pdf`, `.jpg`, `.png`,
  `.doc`/`.docx`, `.xls`/`.xlsx` and other images, Office documents, archives,
  audio/video, css/js, …) are skipped without even being requested.
- Everything else is fetched, but only `text/html` / `application/xhtml+xml`
  responses are scanned; other content types are skipped.
- Redirects are followed; pages that redirect off the site are ignored.
- `#fragment` links are treated as the same page; `mailto:`, `tel:`,
  `javascript:` and `data:` links are ignored.
- 8 concurrent requests, 10 seconds timeout per request; pages that fail to
  load are skipped and counted in the summary.
- Requests are sent with a Firefox browser User-Agent.

Limitations: `robots.txt` is not consulted, JavaScript is not executed
(content rendered client-side is not seen), and `<base href>` is not handled.

### Exit codes

| Code | Meaning |
|------|---------|
| 0    | Scan completed (with or without matches) |
| 1    | No page could be retrieved from the given URL |
| 2    | Invalid arguments or input |

## Building

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```bash
dotnet build
dotnet run -- https://example.com searchword
```

### Publishing a Windows executable

Self-contained single file (no .NET runtime needed on the target machine,
~70 MB):

```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
# -> bin/Release/net10.0/win-x64/publish/sitesearcher.exe
```

Framework-dependent (small, requires the .NET 10 runtime on the target
machine):

```bash
dotnet publish -c Release -r win-x64 --self-contained false
```

Use `-r linux-x64` or `-r osx-arm64` for other platforms.

## Tests

`tests/SiteSearcher.Tests` is an MSTest project that covers the crawl engine
in-process and the CLI end-to-end (the real binary is run as a child
process). The fixture website it crawls is hosted by an in-process Kestrel
server on a dynamic localhost port — no external tools needed, everything
runs on Windows, macOS and Linux:

```bash
dotnet test tests/SiteSearcher.Tests
```

## License

MIT — see [LICENSE](LICENSE).
