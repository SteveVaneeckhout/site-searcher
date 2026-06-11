# sitesearcher

A small .NET 10 command-line tool that crawls a website and lists every page
on that site whose HTML contains a search word.

```text
> sitesearcher.exe https://example.com searchword

Searching for "searchword" on https://example.com/
Press Ctrl+C to stop and show the results found so far.

Searched 142 page(s) | pending 0 | 9 match(es)

Found 9 page(s) containing "searchword":
https://example.com/
https://example.com/about
...

Searched 142 page(s) (3 failed).
Export the results to a .txt file? (y/N)
```

## Usage

```text
sitesearcher [<url> <searchword>] [options]

Arguments:
  <url>         Start URL; the crawl stays on this site (https:// assumed if omitted)
  <searchword>  Word to look for in each page's HTML (case-insensitive)

Options:
  -o, --output <file>   Write the matching URLs to a text file, one per line
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

### Stopping a scan

The crawl runs until it has visited every reachable page on the site. Press
**Ctrl+C** at any time to stop early — the results found up to that point are
shown, and can still be exported. A second Ctrl+C aborts immediately.

### Exporting results

- Pass `-o results.txt` (or `--output results.txt`) to write the list of
  matching URLs to a file automatically.
- Without `-o`, the app offers to export after the scan when anything was
  found (default file name: `sitesearcher-results.txt`).

The export file contains one URL per line (UTF-8), ready for further
processing.

## How it works

- Breadth-first crawl starting at the given URL, following `<a href>` links.
- Stays on the start URL's site: same host, with `www.` ignored
  (`example.com` and `www.example.com` are treated as the same site).
- The search word is matched **case-insensitively against the raw HTML** of
  each page, so occurrences in markup, attributes and scripts count too.
- Only `text/html` / `application/xhtml+xml` responses are scanned; other
  content types (images, PDFs, plain text, …) are skipped.
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
| 0    | Scan completed (with or without matches), including Ctrl+C stops |
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

`tests/run-e2e.sh` builds the app and runs it against two local fixture
sites (a small static site and an endless generated one), covering matching
rules, exporting, interactive prompts, exit codes, Ctrl+C handling and
`--max-pages`:

```bash
tests/run-e2e.sh
```

## License

MIT — see [LICENSE](LICENSE).
