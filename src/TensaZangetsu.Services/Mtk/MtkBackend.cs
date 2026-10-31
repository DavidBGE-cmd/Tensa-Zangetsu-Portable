using TensaZangetsu.Protocol.Mtk;
using TensaZangetsu.Protocol.Mtk.Abstractions;
using TensaZangetsu.Services.Engine;
using TensaZangetsu.Transport.Abstractions;
using System.Text.RegularExpressions;

namespace TensaZangetsu.Services.Mtk;

/// <summary>
/// Delegate factory for creating an <see cref="IUsbTransport"/> for MediaTek hardware.
/// Allows injecting mock transports during testing without accessing physical USB devices.
/// </summary>
public delegate Task<IUsbTransport> MtkTransportFactory(OperationRequest request, CancellationToken ct);

/// <summary>
/// Hardware backend provider for MediaTek (MTK) chipset devices in BROM or Preloader mode.
/// Orchestrates synchronous wire communication through <see cref="MtkProtocol"/> with asynchronous
/// host resource management and serialized session gates.
/// </summary>
public sealed class MtkBackend : IBackendProvider
{
    private static readonly Regex ComPortPattern = new(
        @"(?<![A-Z])COM(?<number>\d+)(?!\d)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private readonly MtkSessionHost _sessionHost;

    public string PlatformId => "MTK";
    public string DisplayName => "MediaTek (MTK BROM/Preloader)";
    public bool IsSimulation => false;

    public MtkBackend(MtkTransportFactory? transportFactory = null, MtkProtocolOptions? options = null)
    {
        _sessionHost = new MtkSessionHost(transportFactory, options);
    }

    public bool SupportsOperation(string operationName)
    {
        return operationName.Equals("ReadInfo", StringComparison.OrdinalIgnoreCase) ||
               operationName.Equals("DeviceInfo", StringComparison.OrdinalIgnoreCase) ||
               operationName.Equals("Connect", StringComparison.OrdinalIgnoreCase) ||
               operationName.Equals("BootDevice", StringComparison.OrdinalIgnoreCase);
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

        if (!SupportsOperation(request.OperationName))
        {
            string unavailReason = $"Operation '{request.OperationName}' is disabled fail-closed on MediaTek. Destructive partition operations and security bypasses require physical validation.";
            reporter.ReportLog($"[FAIL-CLOSED] {unavailReason}", "WARNING", "#FFD700");
            return OperationResult.MakeUnavailable(request.OperationName, unavailReason);
        }

        return await ExecuteReadInfoAsync(request, reporter, ct).ConfigureAwait(false);
    }

    private async Task<OperationResult> ExecuteReadInfoAsync(
        OperationRequest request,
        IOperationProgressReporter reporter,
        CancellationToken ct)
    {
        reporter.ReportProgress(10, "Detecting MediaTek BROM / Preloader candidate...");
        reporter.ReportLog("[MTK] Searching for MediaTek USB or VCOM device in BROM / Preloader mode...", "INFO");

        try
        {
            reporter.ReportProgress(30, "Connecting and starting BROM handshake...");
            Match comMatch = ComPortPattern.Match(request.PortName ?? string.Empty);
            if (comMatch.Success)
                return await ExecuteSerialProbeAsync(
                    reporter,
                    ct,
                    $"COM{comMatch.Groups["number"].Value}").ConfigureAwait(false);

            reporter.ReportLog("[MTK] Starting USB BROM handshake sequence (A0 0A 50 05)...", "INFO");
            MtkTargetInfo target = await _sessionHost.ProbeAsync(request, ct).ConfigureAwait(false);

            ct.ThrowIfCancellationRequested();

            ushort hwCode = target.HardwareCode;
            var chip = MtkChipCatalog.Find(hwCode);
            string chipName = chip?.Name ?? target.ChipName ?? $"MT{hwCode:X4}";
            string chipDesc = chip?.Description ?? target.ChipDescription ?? "";
            string stage = target.Stage.ToString();
            var sec = target.Security;

            reporter.ReportLog($"[MTK] Connected to target chip: {chipName} {(string.IsNullOrEmpty(chipDesc) ? "" : $"({chipDesc})")}", "INFO", "#00E5FF");
            reporter.ReportLog($"[MTK] Hardware Code: 0x{hwCode:X4}, SubCode: 0x{target.HardwareSubCode:X4}", "INFO");
            reporter.ReportLog($"[MTK] Hardware Ver: 0x{target.HardwareVersion:X4}, Software Ver: 0x{target.SoftwareVersion:X4}", "INFO");
            reporter.ReportLog($"[MTK] Boot Stage: {stage}, BROM Ver: 0x{target.BromVersion:X2}, BL Ver: 0x{target.PreloaderVersion:X2}", "INFO");
            reporter.ReportLog($"[MTK] Security: Raw=0x{sec.Raw:X8} [SecureBoot={sec.SecureBoot}, SLA={sec.Sla}, DAA={sec.Daa}, CertRequired={sec.CertificateRequired}]", "INFO");

            var data = new Dictionary<string, object?>
            {
                ["Platform"] = "MTK",
                ["ChipName"] = chipName,
                ["ChipDescription"] = chipDesc,
                ["HardwareCode"] = $"0x{hwCode:X4}",
                ["HardwareSubCode"] = $"0x{target.HardwareSubCode:X4}",
                ["HardwareVersion"] = $"0x{target.HardwareVersion:X4}",
                ["SoftwareVersion"] = $"0x{target.SoftwareVersion:X4}",
                ["BromVersion"] = $"0x{target.BromVersion:X2}",
                ["PreloaderVersion"] = $"0x{target.PreloaderVersion:X2}",
                ["BootStage"] = stage,
                ["SecureBoot"] = sec.SecureBoot,
                ["Sla"] = sec.Sla,
                ["Daa"] = sec.Daa,
                ["CertificateRequired"] = sec.CertificateRequired,
                ["SecurityRaw"] = $"0x{sec.Raw:X8}"
            };

            reporter.ReportProgress(100, $"Identified {chipName} in {stage}");
            return new OperationResult(
                true,
                $"MediaTek device identified: {chipName} (0x{hwCode:X4}) in {stage} mode.",
                OperationState.Completed,
                OperationSupportLevel.ImplementedUntested,
                data);
        }
        catch (OperationCanceledException)
        {
            reporter.ReportLog("[MTK] Operation cancelled by user.", "WARNING", "#FFD700");
            reporter.ReportProgress(0, "Cancelled");
            return OperationResult.MakeCancelled();
        }
        catch (TimeoutException tex)
        {
            string timeoutMsg = $"MediaTek BROM handshake timed out: {tex.Message}";
            reporter.ReportLog($"[MTK] {timeoutMsg}", "ERROR", "#FF1744");
            reporter.ReportProgress(0, "Timed out");
            return OperationResult.MakeFailed(timeoutMsg, tex, OperationState.TimedOut);
        }
        catch (Exception ex)
        {
            string errorMsg = $"MediaTek ReadInfo failed: {ex.Message}";
            reporter.ReportLog($"[MTK] {errorMsg}", "ERROR", "#FF1744");
            reporter.ReportProgress(0, "Failed");
            return OperationResult.MakeFailed(errorMsg, ex, OperationState.Failed);
        }
    }

    private static async Task<OperationResult> ExecuteSerialProbeAsync(
        IOperationProgressReporter reporter,
        CancellationToken ct,
        string portName)
    {
        reporter.ReportLog($"[MTK] Using MediaTek VCOM transport {portName}.", "INFO", "#00E5FF");
        ushort hardwareCode = await Task.Run(
            () => MtkVcomProbe.Connect(portName, ct),
            ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();

        string chipName = MtkChipCatalog.Find(hardwareCode)?.Name ?? $"MT{hardwareCode:X4}";
        reporter.ReportLog($"[MTK] VCOM BROM identified hardware code 0x{hardwareCode:X4}.", "INFO");
        reporter.ReportProgress(100, $"Identified {chipName} through {portName}");

        return new OperationResult(
            true,
            $"MediaTek device identified: {chipName} (0x{hardwareCode:X4}) through {portName}.",
            OperationState.Completed,
            OperationSupportLevel.ImplementedUntested,
            new Dictionary<string, object?>
            {
                ["Platform"] = "MTK",
                ["Transport"] = "SerialPort",
                ["PortName"] = portName,
                ["HardwareCode"] = $"0x{hardwareCode:X4}",
                ["BootStage"] = "BROM/VCOM",
                ["SecurityStatus"] = "Not queried by legacy VCOM probe"
            });
    }
}
