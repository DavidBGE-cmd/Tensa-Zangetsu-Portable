namespace TensaZangetsu.Services.AdbFastboot;

/// <summary>
/// Encapsulates the output and completion state of an external CLI process.
/// </summary>
public sealed record ProcessExecutionResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    bool TimedOut = false,
    bool Cancelled = false)
{
    public bool Success => ExitCode == 0 && !TimedOut && !Cancelled;

    public static ProcessExecutionResult MakeCancelled() =>
        new(-1, string.Empty, "Process execution was cancelled.", TimedOut: false, Cancelled: true);

    public static ProcessExecutionResult MakeTimedOut(TimeSpan timeout) =>
        new(-1, string.Empty, $"Process execution timed out after {timeout.TotalSeconds:F1}s.", TimedOut: true, Cancelled: false);

    public static ProcessExecutionResult MakeFailed(int exitCode, string stderr, string stdout = "") =>
        new(exitCode, stdout, stderr, TimedOut: false, Cancelled: false);
}

/// <summary>
/// Abstraction for executing platform CLI commands (e.g., adb, fastboot) with argument lists,
/// concurrent stream consumption, and process tree lifetime safety.
/// </summary>
public interface IProcessRunner
{
    /// <summary>
    /// Executes a process with an explicit list of arguments, avoiding shell injection.
    /// Standard output and standard error are consumed concurrently to prevent pipe buffer deadlocks.
    /// </summary>
    Task<ProcessExecutionResult> RunAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        TimeSpan? timeout = null,
        CancellationToken ct = default);
}
