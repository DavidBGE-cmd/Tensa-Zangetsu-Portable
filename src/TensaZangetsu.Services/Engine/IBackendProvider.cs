namespace TensaZangetsu.Services.Engine;

/// <summary>
/// Support and readiness level of a specific device operation.
/// Distinguishes between documented models, indexed assets, unverified code, and tested hardware.
/// </summary>
public enum OperationSupportLevel
{
    /// <summary>No backend implementation exists or required hardware/tools are missing.</summary>
    Unavailable,

    /// <summary>Operation exists in reference tool/documentation, but is not implemented.</summary>
    ReferenceOnly,

    /// <summary>Loader or asset is indexed in the catalog, but the operation logic is unverified.</summary>
    IndexedUnverified,

    /// <summary>Protocol code is implemented, but has not yet been validated on real hardware.</summary>
    ImplementedUntested,

    /// <summary>Operation has been fully verified and tested on physical hardware.</summary>
    Verified
}

/// <summary>
/// Progress reporter interface passed to backend execution routines.
/// </summary>
public interface IOperationProgressReporter
{
    /// <summary>
    /// Reports percentage-based progress and current step status text.
    /// </summary>
    void ReportProgress(double percentage, string statusMessage, long bytesTransferred = 0, long totalBytes = 0);

    /// <summary>
    /// Reports a structured log entry with level and optional hex color for UI console.
    /// </summary>
    void ReportLog(string message, string level = "INFO", string? colorHex = null);
}

/// <summary>
/// Request parameter bundle for an engine operation.
/// </summary>
public sealed record OperationRequest(
    string OperationName,
    string Platform,
    string? PortName = null,
    string? ModelName = null,
    string? Brand = null,
    string? Chipset = null,
    bool IsSimulation = false,
    IReadOnlyDictionary<string, string>? Parameters = null);

/// <summary>
/// Comprehensive outcome of an operation execution.
/// </summary>
public sealed record OperationResult(
    bool Success,
    string Message,
    OperationState FinalState,
    OperationSupportLevel SupportLevel = OperationSupportLevel.Verified,
    IReadOnlyDictionary<string, object?>? Data = null,
    Exception? Error = null)
{
    public static OperationResult MakeSuccess(string message, IReadOnlyDictionary<string, object?>? data = null)
        => new(true, message, OperationState.Completed, OperationSupportLevel.Verified, data);

    public static OperationResult MakeFailed(string message, Exception? error = null, OperationState finalState = OperationState.Failed)
        => new(false, message, finalState, OperationSupportLevel.Verified, null, error);

    public static OperationResult MakeUnavailable(string operationName, string reason)
        => new(false, $"[UNAVAILABLE] {operationName}: {reason}", OperationState.Unsupported, OperationSupportLevel.Unavailable);

    public static OperationResult MakeCancelled(string reason = "Operation cancelled by user.")
        => new(false, reason, OperationState.Cancelled, OperationSupportLevel.Verified);
}

/// <summary>
/// Contract implemented by hardware-specific protocol backends (MTK, Qualcomm, SPRD, ADB)
/// and the offline simulation backend.
/// </summary>
public interface IBackendProvider
{
    /// <summary>Unique platform identifier (e.g., "MTK", "QCOM", "SPRD", "ADB", "SIMULATION").</summary>
    string PlatformId { get; }

    /// <summary>User-friendly display name for the backend.</summary>
    string DisplayName { get; }

    /// <summary>True if this backend is an offline simulation, false if operating on real physical hardware.</summary>
    bool IsSimulation { get; }

    /// <summary>Checks whether this backend supports the given operation name.</summary>
    bool SupportsOperation(string operationName);

    /// <summary>Gets the explicit support and verification level for the given operation and model.</summary>
    OperationSupportLevel GetSupportLevel(string operationName, string? modelId = null);

    /// <summary>
    /// Executes the servicing operation asynchronously.
    /// </summary>
    Task<OperationResult> ExecuteOperationAsync(
        OperationRequest request,
        IOperationProgressReporter reporter,
        CancellationToken ct);
}
