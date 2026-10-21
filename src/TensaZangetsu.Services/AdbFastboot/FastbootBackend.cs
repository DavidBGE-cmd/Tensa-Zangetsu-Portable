using System.Text.RegularExpressions;
using TensaZangetsu.Services.Engine;

namespace TensaZangetsu.Services.AdbFastboot;

/// <summary>
/// Read-only hardware backend for Android devices connected in Fastboot / Bootloader mode.
/// Restricts access to read-only diagnostic commands ('fastboot getvar all', 'fastboot getvar unlocked')
/// and strictly blocks flashing, mode changing ('oem edl'), and unauthorized unlocking commands.
/// </summary>
public sealed class FastbootBackend : IBackendProvider
{
    private readonly IProcessRunner _runner;
    private readonly AdbFastbootOptions _options;

    public string PlatformId => "FASTBOOT";
    public string DisplayName => "Android Fastboot Protocol";
    public bool IsSimulation => false;

    public FastbootBackend(IProcessRunner? runner = null, AdbFastbootOptions? options = null)
    {
        _options = options ?? new AdbFastbootOptions();
        _runner = runner ?? new SystemProcessRunner(TimeSpan.FromSeconds(_options.DefaultTimeoutSeconds));
    }

    public bool SupportsOperation(string operationName)
    {
        return operationName.Equals("ReadInfo", StringComparison.OrdinalIgnoreCase) ||
               operationName.Equals("DeviceInfo", StringComparison.OrdinalIgnoreCase) ||
               operationName.Equals("BootloaderStatus", StringComparison.OrdinalIgnoreCase);
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

        string fbExe = _options.ResolveFastbootExecutable();
        if (!File.Exists(fbExe))
        {
            string missingMsg = $"Fastboot tool executable was not found ('{fbExe}'). Please configure its path in appsettings.json or place it in platform-tools.";
            reporter.ReportLog($"[FAIL-CLOSED] {missingMsg}", "ERROR", "#FF1744");
            return OperationResult.MakeUnavailable(request.OperationName, missingMsg);
        }

        if (request.OperationName.Equals("ReadInfo", StringComparison.OrdinalIgnoreCase) ||
            request.OperationName.Equals("DeviceInfo", StringComparison.OrdinalIgnoreCase))
        {
            return await ExecuteReadInfoAsync(fbExe, request, reporter, ct).ConfigureAwait(false);
        }

        if (request.OperationName.Equals("BootloaderStatus", StringComparison.OrdinalIgnoreCase))
        {
            return await ExecuteBootloaderStatusAsync(fbExe, request, reporter, ct).ConfigureAwait(false);
        }

        string unavailReason = $"Operation '{request.OperationName}' is not supported by the read-only Fastboot backend. Flashing, erasing, unlocking, and EDL switches are strictly blocked.";
        reporter.ReportLog($"[FASTBOOT] {unavailReason}", "WARNING", "#FFD700");
        return OperationResult.MakeUnavailable(request.OperationName, unavailReason);
    }

    private async Task<OperationResult> ExecuteReadInfoAsync(
        string fbExe,
        OperationRequest request,
        IOperationProgressReporter reporter,
        CancellationToken ct)
    {
        reporter.ReportLog($"[FASTBOOT] Reading bootloader variables via '{fbExe}'...", "INFO");
        reporter.ReportProgress(15, "Executing 'fastboot getvar all'...");

        var args = !string.IsNullOrWhiteSpace(request.PortName)
            ? new[] { "-s", request.PortName, "getvar", "all" }
            : ["getvar", "all"];

        // Fastboot outputs getvar info primarily to StandardError
        var result = await _runner.RunAsync(fbExe, args, ct: ct).ConfigureAwait(false);

        if (result.Cancelled) return OperationResult.MakeCancelled();
        if (result.TimedOut) return OperationResult.MakeFailed("Fastboot getvar command timed out.", finalState: OperationState.TimedOut);
        if (result.ExitCode != 0)
        {
            return OperationResult.MakeFailed($"'fastboot getvar all' returned error exit code {result.ExitCode}: {result.StandardError}");
        }

        string combinedOutput = $"{result.StandardOutput}\n{result.StandardError}";
        var variables = ParseFastbootVariables(combinedOutput);

        if (variables.Count == 0)
        {
            // Try single getvar product if getvar all wasn't supported
            var singleResult = await _runner.RunAsync(fbExe, ["getvar", "product"], ct: ct).ConfigureAwait(false);
            if (singleResult.Success)
            {
                var singleVars = ParseFastbootVariables($"{singleResult.StandardOutput}\n{singleResult.StandardError}");
                foreach (var (k, v) in singleVars) variables[k] = v;
            }
        }

        if (variables.Count == 0)
        {
            reporter.ReportLog("[FASTBOOT] No bootloader variables could be read. Ensure device is in fastboot mode.", "WARNING", "#FFD700");
            return OperationResult.MakeFailed("No Fastboot variables retrieved. Device might not be in fastboot mode.", finalState: OperationState.Disconnected);
        }

        variables.TryGetValue("product", out var product);
        variables.TryGetValue("version-bootloader", out var bootloaderVer);
        variables.TryGetValue("version-baseband", out var basebandVer);
        variables.TryGetValue("serialno", out var serial);
        variables.TryGetValue("unlocked", out var unlocked);
        variables.TryGetValue("is-userspace", out var isUserspace);
        variables.TryGetValue("current-slot", out var currentSlot);

        reporter.ReportLog($"[FASTBOOT] Product: {product ?? "N/A"}", "INFO", "#00E676");
        reporter.ReportLog($"[FASTBOOT] Bootloader: {bootloaderVer ?? "N/A"} | Baseband: {basebandVer ?? "N/A"}", "INFO", "#00E676");
        reporter.ReportLog($"[FASTBOOT] Serial: {serial ?? "N/A"} | Slot: {currentSlot ?? "N/A"}", "INFO", "#00E676");
        reporter.ReportLog($"[FASTBOOT] Unlocked: {unlocked ?? "unknown"} | FastbootD Userspace: {isUserspace ?? "no"}", "INFO", "#00E676");

        reporter.ReportProgress(100, "Fastboot device info retrieved.");

        var data = new Dictionary<string, object?>
        {
            ["Product"] = product,
            ["BootloaderVersion"] = bootloaderVer,
            ["BasebandVersion"] = basebandVer,
            ["Serial"] = serial,
            ["Unlocked"] = unlocked,
            ["IsUserspace"] = isUserspace,
            ["CurrentSlot"] = currentSlot,
            ["AllVariables"] = variables
        };

        return OperationResult.MakeSuccess($"Fastboot variables retrieved successfully ({product ?? serial ?? "Device"}).", data);
    }

    private async Task<OperationResult> ExecuteBootloaderStatusAsync(
        string fbExe,
        OperationRequest request,
        IOperationProgressReporter reporter,
        CancellationToken ct)
    {
        reporter.ReportLog($"[FASTBOOT] Checking bootloader lock state via 'fastboot getvar unlocked'...", "INFO");
        reporter.ReportProgress(25, "Running 'fastboot getvar unlocked'...");

        var args = !string.IsNullOrWhiteSpace(request.PortName)
            ? new[] { "-s", request.PortName, "getvar", "unlocked" }
            : ["getvar", "unlocked"];

        var result = await _runner.RunAsync(fbExe, args, ct: ct).ConfigureAwait(false);

        if (result.Cancelled) return OperationResult.MakeCancelled();
        if (result.TimedOut) return OperationResult.MakeFailed("Fastboot getvar unlocked command timed out.", finalState: OperationState.TimedOut);
        if (result.ExitCode != 0)
        {
            return OperationResult.MakeFailed($"'fastboot getvar unlocked' returned error code {result.ExitCode}: {result.StandardError}");
        }

        string combinedOutput = $"{result.StandardOutput}\n{result.StandardError}";
        var vars = ParseFastbootVariables(combinedOutput);

        bool isUnlocked = false;
        string rawValue = "unknown";

        if (vars.TryGetValue("unlocked", out var val))
        {
            rawValue = val;
            isUnlocked = val.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
                         val.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                         val.Equals("1", StringComparison.OrdinalIgnoreCase);
        }

        reporter.ReportLog($"[FASTBOOT] Bootloader Lock State: {(isUnlocked ? "UNLOCKED 🔓" : "LOCKED 🔒")} (Raw: {rawValue})", "INFO", isUnlocked ? "#FFD700" : "#00E676");
        reporter.ReportProgress(100, isUnlocked ? "Bootloader is UNLOCKED." : "Bootloader is LOCKED.");

        var data = new Dictionary<string, object?>
        {
            ["IsUnlocked"] = isUnlocked,
            ["RawValue"] = rawValue
        };

        return OperationResult.MakeSuccess($"Bootloader is {(isUnlocked ? "UNLOCKED" : "LOCKED")}.", data);
    }

    private static Dictionary<string, string> ParseFastbootVariables(string rawOutput)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var lines = rawOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            // Typical lines:
            // (bootloader) product: sweet
            // (bootloader) unlocked: yes
            // product: sweet
            var cleaned = line.Replace("(bootloader)", "", StringComparison.OrdinalIgnoreCase).Trim();
            int colonIdx = cleaned.IndexOf(':');
            if (colonIdx > 0 && colonIdx < cleaned.Length - 1)
            {
                string key = cleaned[..colonIdx].Trim();
                string val = cleaned[(colonIdx + 1)..].Trim();
                if (!string.IsNullOrEmpty(key) && !dict.ContainsKey(key))
                {
                    dict[key] = val;
                }
            }
        }

        return dict;
    }
}
