namespace SiteSearcher;

internal static class ResultExporter
{
    /// <summary>Writes the URLs to a text file, one per line (UTF-8).</summary>
    public static void WriteTo(string path, IEnumerable<string> urls)
        => File.WriteAllLines(path, urls);
}
