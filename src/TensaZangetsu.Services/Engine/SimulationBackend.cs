namespace TensaZangetsu.Services.Engine;

/// <summary>
/// Dedicated offline simulation backend.
/// Strictly separated from hardware execution. Every log, result, and progress message
/// emitted by this backend is permanently tagged with "[SIMULATION]".
/// </summary>
public sealed class SimulationBackend : IBackendProvider
{
    private static readonly HashSet<string> SupportedOps = new(StringComparer.OrdinalIgnoreCase)
    {
        "ReadInfo",
        "ReadGpt",
        "ResetFrp",
        "FactoryReset",
        "UnlockBootloader",
        "BackupSecurity",
        "FlashFirmware",
        "DumpStorage",
        "ErasePartition",
        "ReadPartition",
        "WritePartition",
        "BackupEfs",
        "RestoreEfs",
        "ReadHardwareKeys",
        "FixBootLoop",
        "BypassSlaDaa"
    };

    public string PlatformId => "SIMULATION";
    public string DisplayName => "Offline Simulation / Emulation Backend";
    public bool IsSimulation => true;

    public bool SupportsOperation(string operationName) => SupportedOps.Contains(operationName);

    public OperationSupportLevel GetSupportLevel(string operationName, string? modelId = null)
        => SupportedOps.Contains(operationName)
            ? OperationSupportLevel.ImplementedUntested
            : OperationSupportLevel.Unavailable;

    public async Task<OperationResult> ExecuteOperationAsync(
        OperationRequest request,
        IOperationProgressReporter reporter,
        CancellationToken ct)
    {
        reporter.ReportLog("----------------------------------------------------------------", "INFO", "#1B4D28");
        reporter.ReportLog($"⚠ [SIMULATION] Starting offline simulated operation: {request.OperationName}", "WARNING", "#FFD700");
        reporter.ReportLog("[SIMULATION] No physical transport or COM port will be opened.", "INFO", "#80FF9E");
        reporter.ReportProgress(5, "[SIMULATION] Initializing virtual device state...");

        await Task.Delay(300, ct);

        // Phase 1: Virtual device detection
        reporter.ReportProgress(25, "[SIMULATION] Emulating device bus presence...");
        reporter.ReportLog($"✔ [SIMULATION] Virtual device detected: {request.Brand ?? "Generic"} {request.ModelName ?? "TestDevice"} ({request.Platform})", "SUCCESS", "#00FF66");
        await Task.Delay(350, ct);

        // Phase 2: Emulated handshake & identification
        reporter.ReportProgress(45, "[SIMULATION] Emulating protocol handshake & security flags...");
        reporter.ReportLog("[SIMULATION] Handshake: 0xA0 BROM sync OK (Simulated)", "INFO", "#00E5FF");
        reporter.ReportLog($"[SIMULATION] Chipset ID: {request.Chipset ?? "Virtual Helio G85 / Snapdragon 665"} (Simulated)", "INFO", "#00FF66");
        reporter.ReportLog("[SIMULATION] Security: SBC=1, SLA=0, DAA=0 (Simulated)", "INFO", "#E0FFE8");
        await Task.Delay(350, ct);

        // Phase 3: Emulated loader dispatch
        reporter.ReportProgress(70, "[SIMULATION] Emulating loader dispatch...");
        reporter.ReportLog("[SIMULATION] Download Agent payload dispatched to virtual SRAM.", "INFO", "#00E5FF");
        await Task.Delay(400, ct);

        // Phase 4: Emulated payload execution
        reporter.ReportProgress(90, $"[SIMULATION] Executing {request.OperationName} logic...");
        reporter.ReportLog($"[SIMULATION] Executing operation command payload for '{request.OperationName}'...", "INFO", "#80FF9E");
        await Task.Delay(500, ct);

        // Phase 5: Completion
        reporter.ReportProgress(100, $"[SIMULATION] {request.OperationName} finished.");
        reporter.ReportLog($"✔ [SIMULATION] Operation '{request.OperationName}' concluded successfully in offline simulation mode.", "SUCCESS", "#00FF66");

        var data = new Dictionary<string, object?>
        {
            ["IsSimulation"] = true,
            ["Operation"] = request.OperationName,
            ["SimulatedDevice"] = $"{request.Brand} {request.ModelName}".Trim(),
            ["Timestamp"] = DateTime.UtcNow
        };

        return OperationResult.MakeSuccess($"[SIMULATION] {request.OperationName} completed successfully.", data);
    }
}
