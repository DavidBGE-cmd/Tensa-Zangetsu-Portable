using System.ComponentModel;
using System.Diagnostics;

namespace TensaZangetsu.Services.AdbFastboot;

/// <summary>
/// Default system implementation of <see cref="IProcessRunner"/> using <see cref="Process"/>
/// with explicit <see cref="ProcessStartInfo.ArgumentList"/>, concurrent stream draining,
/// and safe tree killing on cancellation/timeout.
/// </summary>
public sealed class SystemProcessRunner : IProcessRunner
{
    private readonly TimeSpan _defaultTimeout;

    public SystemProcessRunner(TimeSpan? defaultTimeout = null)
    {
        _defaultTimeout = defaultTimeout ?? TimeSpan.FromSeconds(15);
    }

    public async Task<ProcessExecutionResult> RunAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentNullException.ThrowIfNull(arguments);

        var effectiveTimeout = timeout ?? _defaultTimeout;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(effectiveTimeout);

        try
        {
            using var proc = new Process();
            proc.StartInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            foreach (var arg in arguments)
            {
                proc.StartInfo.ArgumentList.Add(arg);
            }

            try
            {
                proc.Start();
            }
            catch (Win32Exception ex)
            {
                return ProcessExecutionResult.MakeFailed(-1, $"Failed to start process '{executablePath}': {ex.Message}");
            }
            catch (FileNotFoundException ex)
            {
                return ProcessExecutionResult.MakeFailed(-1, $"Executable not found: {ex.Message}");
            }

            // Drain stdout and stderr concurrently to prevent buffer deadlock
            var stdoutTask = proc.StandardOutput.ReadToEndAsync(cts.Token);
            var stderrTask = proc.StandardError.ReadToEndAsync(cts.Token);

            try
            {
                await proc.WaitForExitAsync(cts.Token).ConfigureAwait(false);
                string stdout = await stdoutTask.ConfigureAwait(false);
                string stderr = await stderrTask.ConfigureAwait(false);

                return new ProcessExecutionResult(proc.ExitCode, stdout, stderr);
            }
            catch (OperationCanceledException)
            {
                // Kill process tree
                try
                {
                    if (!proc.HasExited)
                    {
                        proc.Kill(entireProcessTree: true);
                    }
                }
                catch
                {
                    // Ignore errors during kill if process terminated concurrently
                }

                if (ct.IsCancellationRequested)
                {
                    return ProcessExecutionResult.MakeCancelled();
                }

                return ProcessExecutionResult.MakeTimedOut(effectiveTimeout);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ProcessExecutionResult.MakeFailed(-1, ex.Message);
        }
    }
}
