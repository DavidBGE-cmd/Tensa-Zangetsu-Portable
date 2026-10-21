using TensaZangetsu.Protocol.Qcom;
using TensaZangetsu.Protocol.Qcom.Abstractions;
using TensaZangetsu.Services.Engine;
using TensaZangetsu.Transport.Abstractions;
using TensaZangetsu.Transport.LibUsb;
using TensaZangetsu.Transport.SerialPort;

namespace TensaZangetsu.Services.Qcom;

/// <summary>
/// Delegate factory for creating an <see cref="ITransport"/> for Qualcomm EDL hardware.
/// Allows injecting mock transports during testing without accessing physical hardware.
/// </summary>
public delegate Task<ITransport> QcomTransportFactory(OperationRequest request, CancellationToken ct);

/// <summary>
/// Hardware backend provider for Qualcomm chipset devices in EDL mode (Emergency Download / 9008).
/// Orchestrates synchronous wire communication through <see cref="QcomProtocol"/> with Sahara handshake
/// and fail-closed security.
/// </summary>
public sealed class QcomBackend : IBackendProvider
{
    private readonly QcomTransportFactory _transportFactory;
    private readonly QcomProtocolOptions _options;

    public string PlatformId => "QCOM";
    public string DisplayName => "Qualcomm EDL (Sahara/Firehose)";
    public bool IsSimulation => false;

    public QcomBackend(QcomTransportFactory? transportFactory = null, QcomProtocolOptions? options = null)
    {
        _transportFactory = transportFactory ?? DefaultTransportFactoryAsync;
        _options = options ?? new QcomProtocolOptions
        {
            ReadTimeoutMilliseconds = 5000,
            WriteTimeoutMilliseconds = 5000,
            ConnectTimeoutMilliseconds = 15000,
            ResourceRequestTimeoutMilliseconds = 30000,
            ProbeFirehoseOnSaharaTimeout = false
        };
    }

    public bool SupportsOperation(string operationName)
    {
        return operationName.Equals("ReadInfo", StringComparison.OrdinalIgnoreCase) ||
               operationName.Equals("DeviceInfo", StringComparison.OrdinalIgnoreCase) ||
               operationName.Equals("Connect", StringComparison.OrdinalIgnoreCase) ||
               operationName.Equals("BootDevice", StringComparison.OrdinalIgnoreCase) ||
               operationName.Equals("ProbeSahara", StringComparison.OrdinalIgnoreCase);
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
            string unavailReason = $"Operation '{request.OperationName}' is disabled fail-closed on Qualcomm. Destructive partition writes, rawprogram flashing, and account bypasses require physical validation.";
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
        reporter.ReportProgress(10, "Detecting Qualcomm EDL device (COM / USB 0x05C6:0x9008)...");
        reporter.ReportLog("[QCOM] Searching for Qualcomm device in EDL 9008 mode...", "INFO");

        ITransport? transport = null;
        try
        {
            transport = await _transportFactory(request, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();

            reporter.ReportProgress(30, "Connecting and starting Sahara handshake...");
            reporter.ReportLog("[QCOM] Transport acquired. Negotiating Sahara Hello and reading target ID...", "INFO");

            var target = await Task.Run(() =>
            {
                var protocol = new QcomProtocol(transport, _options, leaveTransportOpen: false);
                protocol.ProbeSahara();
                return protocol.TargetInfo;
            }, ct).ConfigureAwait(false);

            ct.ThrowIfCancellationRequested();

            var sahara = target?.Sahara;
            string serial = sahara?.Serial.HasValue == true ? $"0x{sahara.Serial.Value:X16}" : "Unknown";
            var msmInfo = sahara?.MsmHwInfo;
            string msmId = msmInfo?.MsmId.HasValue == true ? $"0x{msmInfo.MsmId.Value:X8}" : "Unknown";
            string socHwVer = msmInfo?.SocHwVersion.HasValue == true ? $"0x{msmInfo.SocHwVersion.Value:X8}" : "Unknown";
            string oemId = msmInfo?.OemId.HasValue == true ? $"0x{msmInfo.OemId.Value:X4}" : "Unknown";
            string modelId = msmInfo?.ModelId.HasValue == true ? $"0x{msmInfo.ModelId.Value:X4}" : "Unknown";
            string sblVer = sahara?.SblVersion.HasValue == true ? $"0x{sahara.SblVersion.Value:X8}" : "Unknown";
            string hash = sahara?.CaHash.HasValue == true ? Convert.ToHexString(sahara.CaHash.Value.Span) : "None";

            reporter.ReportLog($"[QCOM] Connected in Sahara EDL mode. Protocol Version: {sahara?.Version}", "INFO", "#00E5FF");
            reporter.ReportLog($"[QCOM] Chip Serial Number: {serial}", "INFO");
            reporter.ReportLog($"[QCOM] MSM HW Info: MSM ID={msmId}, SoC Version={socHwVer}, OEM ID={oemId}, Model ID={modelId}", "INFO");
            reporter.ReportLog($"[QCOM] SBL Version: {sblVer}", "INFO");
            reporter.ReportLog($"[QCOM] Root CA Hash: {(hash.Length > 16 ? hash[..16] + "..." : hash)}", "INFO");

            var data = new Dictionary<string, object?>
            {
                ["Platform"] = "QCOM",
                ["Mode"] = "EDL",
                ["SaharaVersion"] = sahara?.Version,
                ["Serial"] = serial,
                ["MsmId"] = msmId,
                ["SocHwVersion"] = socHwVer,
                ["OemId"] = oemId,
                ["ModelId"] = modelId,
                ["SblVersion"] = sblVer,
                ["CaHashFingerprint"] = hash.Length > 16 ? hash[..16] : hash
            };

            reporter.ReportProgress(100, $"Identified Qualcomm device: Serial={serial}");
            return OperationResult.MakeSuccess(
                $"Qualcomm EDL device identified: Serial={serial}, MSM={msmId}",
                data);
        }
        catch (OperationCanceledException)
        {
            reporter.ReportLog("[QCOM] Operation cancelled by user.", "WARNING", "#FFD700");
            reporter.ReportProgress(0, "Cancelled");
            return OperationResult.MakeCancelled();
        }
        catch (TimeoutException tex)
        {
            string timeoutMsg = $"Qualcomm Sahara handshake timed out: {tex.Message}";
            reporter.ReportLog($"[QCOM] {timeoutMsg}", "ERROR", "#FF1744");
            reporter.ReportProgress(0, "Timed out");
            return OperationResult.MakeFailed(timeoutMsg, tex, OperationState.TimedOut);
        }
        catch (Exception ex)
        {
            string errorMsg = $"Qualcomm ReadInfo failed: {ex.Message}";
            reporter.ReportLog($"[QCOM] {errorMsg}", "ERROR", "#FF1744");
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

    private static Task<ITransport> DefaultTransportFactoryAsync(OperationRequest request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        // 1. Explicit COM port specified in request
        if (!string.IsNullOrWhiteSpace(request.PortName) &&
            request.PortName.StartsWith("COM", StringComparison.OrdinalIgnoreCase))
        {
            var comTransport = SerialPortTransportFactory.Create(
                request.PortName,
                readTimeout: 5000,
                writeTimeout: 5000);
            return Task.FromResult<ITransport>(comTransport);
        }

        // 2. Scan LibUsb for Qualcomm EDL VID 0x05C6, PID 0x9008
        try
        {
            var identities = LibUsbTransportFactory.Enumerate(vendorId: 0x05C6, productId: 0x9008);
            if (identities.Count == 1)
            {
                var options = new LibUsbConnectionOptions
                {
                    Identity = identities[0],
                    ReadTimeoutMilliseconds = 5000,
                    WriteTimeoutMilliseconds = 5000,
                    RecoverInitialReadStall = true
                };
                return Task.FromResult<ITransport>(LibUsbTransportFactory.Create(options));
            }

            if (identities.Count > 1)
            {
                throw new InvalidOperationException(
                    $"Multiple Qualcomm EDL USB devices detected ({identities.Count}). Connect only one device to prevent ambiguity.");
            }
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            // LibUsb runtime not available or enumeration failed; fallback to COM check below
        }

        // 3. Fallback: check available COM ports
        var portNames = System.IO.Ports.SerialPort.GetPortNames();
        if (portNames.Length == 1)
        {
            var comTransport = SerialPortTransportFactory.Create(
                portNames[0],
                readTimeout: 5000,
                writeTimeout: 5000);
            return Task.FromResult<ITransport>(comTransport);
        }

        throw new InvalidOperationException(
            "No Qualcomm EDL device detected (QDLoader 9008 / VID 0x05C6:0x9008). Connect device in EDL mode (via TestPoint, EDL cable, or 'fastboot oem edl').");
    }
}
