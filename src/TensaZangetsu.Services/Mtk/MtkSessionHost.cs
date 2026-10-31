using System.Globalization;
using System.Text.RegularExpressions;
using TensaZangetsu.Protocol.Mtk;
using TensaZangetsu.Protocol.Mtk.Abstractions;
using TensaZangetsu.Services.Engine;
using TensaZangetsu.Transport.Abstractions;
using TensaZangetsu.Transport.LibUsb;

namespace TensaZangetsu.Services.Mtk;

/// <summary>
/// Owns one short-lived native USB MTK probe and rejects ambiguous candidates.
/// Resource upload and authentication remain separate phases.
/// </summary>
public sealed class MtkSessionHost
{
    private static readonly Regex UsbCandidatePattern = new(
        @"VID_(?<vid>[0-9A-F]{4})&PID_(?<pid>[0-9A-F]{4}).*?\bBus\s+(?<bus>\d+|\?)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly MtkTransportFactory _transportFactory;
    private readonly MtkProtocolOptions _options;

    public MtkSessionHost(
        MtkTransportFactory? transportFactory = null,
        MtkProtocolOptions? options = null)
    {
        _transportFactory = transportFactory ?? CreateTransportAsync;
        _options = options ?? new MtkProtocolOptions
        {
            ReadTimeoutMilliseconds = 5000,
            ConnectTimeoutMilliseconds = 15000,
            OperationTimeoutMilliseconds = 30000,
            InitializeWatchdogOnProbe = true
        };
    }

    public async Task<MtkTargetInfo> ProbeAsync(OperationRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        IUsbTransport transport = await _transportFactory(request, ct).ConfigureAwait(false);
        try
        {
            using var protocol = new MtkProtocol(transport, _options, leaveTransportOpen: false);
            return await Task.Run(() => protocol.Probe(ct), ct).ConfigureAwait(false);
        }
        finally
        {
            transport.Dispose();
        }
    }

    private static Task<IUsbTransport> CreateTransportAsync(OperationRequest request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        try
        {
            IReadOnlyList<UsbTransportIdentity> identities =
                LibUsbTransportFactory.Enumerate(vendorId: 0x0E8D);
            UsbTransportIdentity identity = ResolveIdentity(identities, request.PortName);
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
                $"Failed to access USB subsystem via LibUsb: {ex.Message}. Verify WinUSB/libusbK and the native runtime.",
                ex);
        }
    }

    private static UsbTransportIdentity ResolveIdentity(
        IReadOnlyList<UsbTransportIdentity> identities,
        string? selectedPort)
    {
        if (identities.Count == 0)
            throw new InvalidOperationException(
                "No MediaTek USB device detected (VID 0x0E8D). Connect the device in BROM mode.");

        if (string.IsNullOrWhiteSpace(selectedPort))
        {
            if (identities.Count != 1)
                throw new InvalidOperationException(
                    $"Multiple MediaTek USB devices detected ({identities.Count}). Select one device explicitly.");

            return identities[0];
        }

        Match match = UsbCandidatePattern.Match(selectedPort);
        if (!match.Success)
        {
            if (identities.Count != 1)
                throw new InvalidOperationException(
                    "The selected port is not a native MTK USB identity and multiple MTK devices are present.");

            return identities[0];
        }

        ushort vid = ushort.Parse(match.Groups["vid"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        ushort pid = ushort.Parse(match.Groups["pid"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        IEnumerable<UsbTransportIdentity> candidates = identities
            .Where(identity => identity.VendorId == vid && identity.ProductId == pid);

        string busText = match.Groups["bus"].Value;
        if (byte.TryParse(busText, NumberStyles.Integer, CultureInfo.InvariantCulture, out byte bus))
            candidates = candidates.Where(identity => identity.BusNumber == bus);

        UsbTransportIdentity[] matches = candidates.ToArray();
        if (matches.Length != 1)
            throw new InvalidOperationException(
                $"The selected MTK USB candidate is no longer unique ({matches.Length} matches). Refresh the device list.");

        return matches[0];
    }
}
