using TensaZangetsu.Protocol.Mtk;
using TensaZangetsu.Protocol.Mtk.Abstractions;
using TensaZangetsu.Services.Engine;
using TensaZangetsu.Transport.Abstractions;
using TensaZangetsu.Transport.LibUsb;

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
    private readonly MtkTransportFactory _transportFactory;
    private readonly MtkProtocolOptions _options;

    public string PlatformId => "MTK";
    public string DisplayName => "MediaTek (MTK BROM/Preloader)";
    public bool IsSimulation => false;

    public MtkBackend(MtkTransportFactory? transportFactory = null, MtkProtocolOptions? options = null)
    {
        _transportFactory = transportFactory ?? DefaultTransportFactoryAsync;
        _options = options ?? new MtkProtocolOptions
        {
            ReadTimeoutMilliseconds = 5000,
            ConnectTimeoutMilliseconds = 15000,
            OperationTimeoutMilliseconds = 30000,
            InitializeWatchdogOnProbe = true
        };
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
        reporter.ReportProgress(10, "Detecting MediaTek USB device (VID 0x0E8D)...");
        reporter.ReportLog("[MTK] Searching for MediaTek USB device in BROM / Preloader mode...", "INFO");

        IUsbTransport? transport = null;
        try
        {
            transport = await _transportFactory(request, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();

            reporter.ReportProgress(30, "Connecting and starting BROM handshake...");
            reporter.ReportLog("[MTK] USB transport acquired. Starting BROM handshake sequence (A0 0A 50 05)...", "INFO");

            // Execute synchronous wire handshake and identification on background thread
            var target = await Task.Run(() =>
            {
                var protocol = new MtkProtocol(transport, _options, leaveTransportOpen: false);
                return protocol.Probe(ct);
            }, ct).ConfigureAwait(false);

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
            return OperationResult.MakeSuccess(
                $"MediaTek device identified: {chipName} (0x{hwCode:X4}) in {stage} mode.",
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
        finally
        {
            if (transport is not null)
            {
                try
                {
                    transport.Dispose();
                }
                catch { }
            }
        }
    }

    private static Task<IUsbTransport> DefaultTransportFactoryAsync(OperationRequest request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        try
        {
            var identities = LibUsbTransportFactory.Enumerate(vendorId: 0x0E8D);
            if (identities.Count == 0)
            {
                throw new InvalidOperationException(
                    "No MediaTek USB device detected (VID 0x0E8D). Connect device in BROM mode (hold Vol+ and Vol- while plugging in USB cable).");
            }

            if (identities.Count > 1)
            {
                throw new InvalidOperationException(
                    $"Multiple MediaTek USB devices detected ({identities.Count}). Connect only one device to prevent ambiguous operations.");
            }

            var identity = identities[0];
            var options = new LibUsbConnectionOptions
            {
                Identity = identity,
                ReadTimeoutMilliseconds = 5000,
                WriteTimeoutMilliseconds = 5000,
                RecoverInitialReadStall = true
            };

            return Task.FromResult(LibUsbTransportFactory.Create(options));
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException(
                $"Failed to access USB subsystem via LibUsb: {ex.Message}. Verify that WinUSB / libusbK drivers are installed.", ex);
        }
    }
}
