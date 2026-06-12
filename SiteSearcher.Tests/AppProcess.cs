using System.Diagnostics;

namespace SiteSearcher.Tests;

internal sealed record AppRun(int ExitCode, string Stdout, string Stderr);

/// <summary>
/// Runs the real sitesearcher executable as a child process. The app's build
/// output (dll + runtimeconfig + deps) is copied into the test output directory
/// by the project reference, so it is launched through the dotnet muxer.
/// </summary>
internal static class AppProcess
{
    private static readonly string AppDll = Path.Combine(AppContext.BaseDirectory, "sitesearcher.dll");

    // Under VSTest on Windows the test host is testhost.exe, not dotnet; fall
    // back to resolving "dotnet" from PATH in that case.
    private static string DotnetHost =>
        Environment.ProcessPath is { } p
            && Path.GetFileNameWithoutExtension(p).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
        ? p : "dotnet";

    internal static async Task<AppRun> RunAsync(string[] args, string stdin = "", int timeoutSeconds = 60)
    {
        var psi = new ProcessStartInfo(DotnetHost)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetTempPath(),
        };
        psi.ArgumentList.Add(AppDll);
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi)!;

        // Start draining both pipes before waiting so a chatty child can never
        // fill a pipe buffer and deadlock against WaitForExit.
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.StandardInput.WriteAsync(stdin);
        process.StandardInput.Close(); // EOF for Console.ReadLine

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new AssertFailedException(
                $"sitesearcher did not exit within {timeoutSeconds}s. Partial stdout: {await stdoutTask}");
        }

        return new AppRun(process.ExitCode, await stdoutTask, await stderrTask);
    }
}
