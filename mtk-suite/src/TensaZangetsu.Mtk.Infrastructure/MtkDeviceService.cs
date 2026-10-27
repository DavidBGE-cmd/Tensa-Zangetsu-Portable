using TensaZangetsu.Mtk.Core;
using TensaZangetsu.Services.Engine;
using TensaZangetsu.Services.Mtk;

namespace TensaZangetsu.Mtk.Infrastructure;

public sealed class MtkDeviceService : IMtkDeviceService
{
    private readonly MtkBackend _backend;

    public MtkDeviceService(MtkBackend? backend = null) =>
        _backend = backend ?? new MtkBackend();

    public async Task<MtkOperationResult> IdentifyAsync(
        IProgress<MtkLogEntry>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var logs = new List<MtkLogEntry>();
        var reporter = new Reporter(logs, progress);
        OperationResult result = await _backend.ExecuteOperationAsync(
            new OperationRequest("ReadInfo", "MTK"),
            reporter,
            cancellationToken).ConfigureAwait(false);

        MtkDeviceSnapshot? snapshot = result.Data is null ? null : CreateSnapshot(result.Data);
        return new(result.Success, result.Message, snapshot, logs);
    }

    private static MtkDeviceSnapshot CreateSnapshot(IReadOnlyDictionary<string, object?> data) =>
        new(
            Value(data, "Platform"),
            Value(data, "ChipName"),
            Value(data, "ChipDescription"),
            Value(data, "HardwareCode"),
            Value(data, "HardwareSubCode"),
            Value(data, "HardwareVersion"),
            Value(data, "SoftwareVersion"),
            Value(data, "BromVersion"),
            Value(data, "PreloaderVersion"),
            Value(data, "BootStage"),
            Boolean(data, "SecureBoot"),
            Boolean(data, "Sla"),
            Boolean(data, "Daa"),
            Boolean(data, "CertificateRequired"),
            Value(data, "SecurityRaw"));

    private static string Value(IReadOnlyDictionary<string, object?> data, string key) =>
        data.TryGetValue(key, out object? value) && value is not null ? Convert.ToString(value) ?? "Unknown" : "Unknown";

    private static bool Boolean(IReadOnlyDictionary<string, object?> data, string key) =>
        data.TryGetValue(key, out object? value) && value is bool boolean && boolean;

    private sealed class Reporter(List<MtkLogEntry> logs, IProgress<MtkLogEntry>? progress) : IOperationProgressReporter
    {
        public void ReportProgress(double percentage, string statusMessage, long bytesTransferred = 0, long totalBytes = 0) =>
            Report("INFO", $"{percentage:0}% {statusMessage}");

        public void ReportLog(string message, string level = "INFO", string? colorHex = null) =>
            Report(level, message);

        private void Report(string level, string message)
        {
            var entry = new MtkLogEntry(DateTimeOffset.UtcNow, level, message);
            logs.Add(entry);
            progress?.Report(entry);
        }
    }
}
