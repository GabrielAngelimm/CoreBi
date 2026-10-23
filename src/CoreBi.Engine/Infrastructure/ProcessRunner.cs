using System.Diagnostics;
using System.Text;

namespace CoreBi.Engine.Infrastructure;

internal sealed record ProcessResult(int ExitCode, string Output, string Error);

internal static class ProcessRunner
{
    internal static ProcessStartInfo Create(string executable, IEnumerable<string> arguments)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        return start;
    }

    public static async Task<ProcessResult> RunAsync(string executable, IEnumerable<string> arguments, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        using var process = Process.Start(Create(executable, arguments))
            ?? throw new InvalidOperationException("Não foi possível iniciar o processo.");
        // Drain both pipes from the start; waiting for exit before reading can deadlock.
        Task<string> output = ReadBoundedAsync(process.StandardOutput);
        Task<string> error = ReadBoundedAsync(process.StandardError);
        try { await process.WaitForExitAsync(token).ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (Exception ex) { DiagnosticLog.Write("process.cancel", ex); }
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                await process.WaitForExitAsync(cleanup.Token).ConfigureAwait(false);
                await Task.WhenAll(output, error).WaitAsync(cleanup.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw new InvalidOperationException("Não foi possível confirmar a parada do processo em cinco segundos."); }
            throw;
        }
        return new(process.ExitCode, await output.ConfigureAwait(false), await error.ConfigureAwait(false));
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader)
    {
        const int limit = 64 * 1024;
        var result = new StringBuilder();
        var buffer = new char[4096];
        int count;
        while ((count = await reader.ReadAsync(buffer).ConfigureAwait(false)) != 0)
        {
            result.Append(buffer, 0, count);
            if (result.Length > limit) result.Remove(0, result.Length - limit);
        }
        return result.ToString();
    }
}
