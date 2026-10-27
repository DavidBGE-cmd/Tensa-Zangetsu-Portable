namespace TensaZangetsu.Mtk.Core;

public sealed record MtkLogEntry(DateTimeOffset Timestamp, string Level, string Message);

public sealed record MtkDeviceSnapshot(
    string Platform,
    string ChipName,
    string ChipDescription,
    string HardwareCode,
    string HardwareSubCode,
    string HardwareVersion,
    string SoftwareVersion,
    string BromVersion,
    string PreloaderVersion,
    string BootStage,
    bool SecureBoot,
    bool Sla,
    bool Daa,
    bool CertificateRequired,
    string SecurityRaw);

public sealed record MtkOperationResult(
    bool Success,
    string Message,
    MtkDeviceSnapshot? Device,
    IReadOnlyList<MtkLogEntry> Logs);

public interface IMtkDeviceService
{
    Task<MtkOperationResult> IdentifyAsync(
        IProgress<MtkLogEntry>? progress = null,
        CancellationToken cancellationToken = default);
}
