using System.Diagnostics;

namespace NTech.DataStudio.Trainer.Processes;

public sealed record ProcessResult(int ExitCode, string Output, string Error)
{
    public bool Succeeded => ExitCode == 0;
}

public interface IProcessRunner
{
    /// <summary>Arguments are passed as a list, never through a shell, so user input cannot become a command.</summary>
    Task<ProcessResult> RunAsync(string executable, IReadOnlyList<string> arguments, CancellationToken cancellationToken = default);
}

public sealed class ProcessRunner : IProcessRunner
{
    /// <summary>Exit code when the executable is not installed, so callers need no try/catch.</summary>
    public const int NotFound = -1;

    public async Task<ProcessResult> RunAsync(string executable, IReadOnlyList<string> arguments, CancellationToken cancellationToken = default)
    {
        var info = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);

        Process? process;
        try
        {
            process = Process.Start(info);
        }
        catch (System.ComponentModel.Win32Exception problem)
        {
            return new ProcessResult(NotFound, "", problem.Message);
        }

        if (process is null) return new ProcessResult(NotFound, "", $"Could not start {executable}");

        using (process)
        {
            var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var error = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            return new ProcessResult(process.ExitCode, await output, await error);
        }
    }
}
