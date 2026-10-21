using System.Text.RegularExpressions;
using TensaZangetsu.Services.Engine;

namespace TensaZangetsu.Services.AdbFastboot;

/// <summary>
/// Read-only hardware backend for Android devices connected via ADB (Android Debug Bridge).
/// Enforces read-only safety: destructive, partition writing, or mode-switching operations
/// (such as reboot to EDL) are strictly marked as <see cref="OperationSupportLevel.Unavailable"/>.
/// </summary>
public sealed class AdbBackend : IBackendProvider
{
    private readonly IProcessRunner _runner;
    private readonly AdbFastbootOptions _options;

    public string PlatformId => "ADB";
    public string DisplayName => "Android Debug Bridge (ADB)";
    public bool IsSimulation => false;

    public AdbBackend(IProcessRunner? runner = null, AdbFastbootOptions? options = null)
    {
        _options = options ?? new AdbFastbootOptions();
        _runner = runner ?? new SystemProcessRunner(TimeSpan.FromSeconds(_options.DefaultTimeoutSeconds));
    }

    public bool SupportsOperation(string operationName)
    {
        return operationName.Equals("ReadInfo", StringComparison.OrdinalIgnoreCase) ||
               operationName.Equals("DeviceInfo", StringComparison.OrdinalIgnoreCase) ||
               operationName.Equals("PartitionList", StringComparison.OrdinalIgnoreCase) ||
               operationName.Equals("ReadPartitions", StringComparison.OrdinalIgnoreCase);
    }

    public OperationSupportLevel GetSupportLevel(string operationName, string? modelId = null)
    {
        if (SupportsOperation(operationName))
        {
            return OperationSupportLevel.ImplementedUntested;
        }

        return OperationSupportLevel.Unavailable;
    }

    public async Task<OperationResult> ExecuteOperationAsync(
        OperationRequest request,
        IOperationProgressReporter reporter,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(reporter);

        string adbExe = _options.ResolveAdbExecutable();
        if (!File.Exists(adbExe))
        {
            string missingMsg = $"ADB tool executable was not found ('{adbExe}'). Please configure its path in appsettings.json or place it in platform-tools.";
            reporter.ReportLog($"[FAIL-CLOSED] {missingMsg}", "ERROR", "#FF1744");
            return OperationResult.MakeUnavailable(request.OperationName, missingMsg);
        }

        if (request.OperationName.Equals("ReadInfo", StringComparison.OrdinalIgnoreCase) ||
            request.OperationName.Equals("DeviceInfo", StringComparison.OrdinalIgnoreCase))
        {
            return await ExecuteReadInfoAsync(adbExe, request, reporter, ct).ConfigureAwait(false);
        }

        if (request.OperationName.Equals("PartitionList", StringComparison.OrdinalIgnoreCase) ||
            request.OperationName.Equals("ReadPartitions", StringComparison.OrdinalIgnoreCase))
        {
            return await ExecutePartitionListAsync(adbExe, request, reporter, ct).ConfigureAwait(false);
        }

        string unavailReason = $"Operation '{request.OperationName}' is not supported by the read-only ADB backend. Destructive and EDL mode-switch commands are disabled.";
        reporter.ReportLog($"[ADB] {unavailReason}", "WARNING", "#FFD700");
        return OperationResult.MakeUnavailable(request.OperationName, unavailReason);
    }

    private async Task<OperationResult> ExecuteReadInfoAsync(
        string adbExe,
        OperationRequest request,
        IOperationProgressReporter reporter,
        CancellationToken ct)
    {
        reporter.ReportLog($"[ADB] Querying connected devices via '{adbExe}'...", "INFO");
        reporter.ReportProgress(10, "Running 'adb devices -l'...");

        var result = await _runner.RunAsync(adbExe, ["devices", "-l"], ct: ct).ConfigureAwait(false);

        if (result.Cancelled) return OperationResult.MakeCancelled();
        if (result.TimedOut) return OperationResult.MakeFailed("ADB devices command timed out.", finalState: OperationState.TimedOut);
        if (result.ExitCode != 0)
        {
            return OperationResult.MakeFailed($"'adb devices -l' returned error exit code {result.ExitCode}: {result.StandardError}");
        }

        var lines = result.StandardOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        string? serial = null;
        string? state = null;
        string model = "";
        string product = "";
        string device = "";

        foreach (var line in lines)
        {
            if (line.StartsWith("List of devices", StringComparison.OrdinalIgnoreCase)) continue;
            if (line.StartsWith("* daemon", StringComparison.OrdinalIgnoreCase)) continue;

            var parts = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                serial = parts[0];
                state = parts[1].ToLowerInvariant();

                model = ExtractProp(line, "model:") ?? "";
                product = ExtractProp(line, "product:") ?? "";
                device = ExtractProp(line, "device:") ?? "";
                break;
            }
        }

        if (string.IsNullOrEmpty(serial))
        {
            reporter.ReportLog("[ADB] No active devices found in 'adb devices -l'.", "WARNING", "#FFD700");
            return OperationResult.MakeFailed("No device detected over ADB. Verify USB connection and debugging mode.", finalState: OperationState.Disconnected);
        }

        if (state == "unauthorized")
        {
            reporter.ReportLog($"[ADB] Device '{serial}' is unauthorized.", "WARNING", "#FFD700");
            reporter.ReportLog("⚠️ Unlock device screen and accept RSA USB Debugging prompt.", "WARNING", "#FFD700");
            return OperationResult.MakeFailed($"Device '{serial}' is unauthorized. Allow USB debugging on the device screen.", finalState: OperationState.AuthRequired);
        }

        reporter.ReportProgress(50, $"Device found: {serial} ({state}). Querying system properties...");

        // Query properties if device is online
        string androidVersion = "";
        string manufacturer = "";
        string buildId = "";

        if (state == "device")
        {
            var propRelease = await _runner.RunAsync(adbExe, ["-s", serial, "shell", "getprop", "ro.build.version.release"], ct: ct).ConfigureAwait(false);
            if (propRelease.Success && !string.IsNullOrWhiteSpace(propRelease.StandardOutput))
                androidVersion = propRelease.StandardOutput.Trim();

            var propMan = await _runner.RunAsync(adbExe, ["-s", serial, "shell", "getprop", "ro.product.manufacturer"], ct: ct).ConfigureAwait(false);
            if (propMan.Success && !string.IsNullOrWhiteSpace(propMan.StandardOutput))
                manufacturer = propMan.StandardOutput.Trim();

            var propBuild = await _runner.RunAsync(adbExe, ["-s", serial, "shell", "getprop", "ro.build.display.id"], ct: ct).ConfigureAwait(false);
            if (propBuild.Success && !string.IsNullOrWhiteSpace(propBuild.StandardOutput))
                buildId = propBuild.StandardOutput.Trim();
        }

        var data = new Dictionary<string, object?>
        {
            ["Serial"] = serial,
            ["State"] = state,
            ["Model"] = model,
            ["Product"] = product,
            ["Device"] = device,
            ["AndroidVersion"] = androidVersion,
            ["Manufacturer"] = manufacturer,
            ["BuildId"] = buildId
        };

        reporter.ReportLog($"[ADB] Device Serial: {serial}", "INFO", "#00E676");
        reporter.ReportLog($"[ADB] Manufacturer: {manufacturer} | Model: {model}", "INFO", "#00E676");
        if (!string.IsNullOrEmpty(androidVersion)) reporter.ReportLog($"[ADB] Android: {androidVersion} (Build: {buildId})", "INFO", "#00E676");

        reporter.ReportProgress(100, "Device info retrieved.");
        return OperationResult.MakeSuccess($"ADB device info retrieved for {serial} ({model}).", data);
    }

    private async Task<OperationResult> ExecutePartitionListAsync(
        string adbExe,
        OperationRequest request,
        IOperationProgressReporter reporter,
        CancellationToken ct)
    {
        reporter.ReportLog("[ADB] Reading partitions via 'adb shell cat /proc/partitions'...", "INFO");
        reporter.ReportProgress(20, "Executing 'cat /proc/partitions'...");

        var args = !string.IsNullOrWhiteSpace(request.PortName)
            ? new[] { "-s", request.PortName, "shell", "cat", "/proc/partitions" }
            : ["shell", "cat", "/proc/partitions"];

        var result = await _runner.RunAsync(adbExe, args, ct: ct).ConfigureAwait(false);

        if (result.Cancelled) return OperationResult.MakeCancelled();
        if (result.TimedOut) return OperationResult.MakeFailed("ADB partition read timed out.", finalState: OperationState.TimedOut);
        if (result.ExitCode != 0)
        {
            return OperationResult.MakeFailed($"'cat /proc/partitions' returned error code {result.ExitCode}: {result.StandardError}");
        }

        var partitions = new List<AdbPartitionEntry>();
        var lines = result.StandardOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            var match = Regex.Match(line.Trim(), @"^(\d+)\s+(\d+)\s+(\d+)\s+([a-zA-Z0-9_\-\./]+)$");
            if (match.Success)
            {
                if (int.TryParse(match.Groups[1].Value, out int major) &&
                    int.TryParse(match.Groups[2].Value, out int minor) &&
                    long.TryParse(match.Groups[3].Value, out long blocks))
                {
                    string name = match.Groups[4].Value;
                    partitions.Add(new AdbPartitionEntry(major, minor, blocks, name));
                }
            }
        }

        if (partitions.Count == 0)
        {
            return OperationResult.MakeFailed("No partitions found in /proc/partitions or output was unexpected.");
        }

        reporter.ReportLog($"[ADB] Successfully parsed {partitions.Count} partition entries.", "INFO", "#00E676");
        reporter.ReportProgress(100, $"Found {partitions.Count} partitions.");

        var data = new Dictionary<string, object?>
        {
            ["Count"] = partitions.Count,
            ["Partitions"] = partitions
        };

        return OperationResult.MakeSuccess($"Retrieved {partitions.Count} partitions via ADB.", data);
    }

    private static string? ExtractProp(string line, string prefix)
    {
        int idx = line.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;
        int start = idx + prefix.Length;
        int end = line.IndexOf(' ', start);
        return end > start ? line[start..end] : line[start..];
    }
}

public sealed record AdbPartitionEntry(int Major, int Minor, long Blocks, string Name);
