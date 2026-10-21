namespace TensaZangetsu.Services.Engine;

/// <summary>
/// States for the shared operation state machine.
/// Both GUI and CLI consume these states through <see cref="ApplicationEngine"/>.
/// </summary>
public enum OperationState
{
    /// <summary>No operation in progress; device may or may not be connected.</summary>
    Idle,

    /// <summary>Passively waiting for a device to appear on the bus.</summary>
    WaitingForDevice,

    /// <summary>A candidate device was detected but not yet acquired.</summary>
    CandidateDetected,

    /// <summary>Acquiring exclusive transport (COM port, USB bulk, etc.).</summary>
    AcquiringTransport,

    /// <summary>Performing protocol handshake (BROM sync, Sahara hello, BSL sync).</summary>
    Handshaking,

    /// <summary>Reading device identity (HW code, MSM ID, chip ID).</summary>
    Identifying,

    /// <summary>Resolving the appropriate loader/agent for the identified device.</summary>
    ResolvingLoader,

    /// <summary>Performing authentication (SLA, DAA, Sahara image transfer, FDL signature).</summary>
    Authorizing,

    /// <summary>Loading the download agent or firehose programmer.</summary>
    LoadingAgent,

    /// <summary>Device is ready for operations; session is active.</summary>
    Ready,

    /// <summary>Building an operation plan (partition selection, validation, dry-run).</summary>
    Planning,

    /// <summary>Waiting for explicit user confirmation before a destructive operation.</summary>
    AwaitingConfirmation,

    /// <summary>Executing the confirmed operation (read, write, erase, etc.).</summary>
    Executing,

    /// <summary>Verifying the operation result (readback, hash comparison).</summary>
    Verifying,

    /// <summary>Operation completed successfully; session may still be active.</summary>
    Completed,

    // ──── Terminal / error states ────

    /// <summary>Operation was cancelled by the user at a safe point.</summary>
    Cancelled,

    /// <summary>A timeout expired during acquisition, handshake, or transfer.</summary>
    TimedOut,

    /// <summary>The device disconnected during an operation.</summary>
    Disconnected,

    /// <summary>The device or operation is not supported by any registered backend.</summary>
    Unsupported,

    /// <summary>The operation requires OEM authentication that is not configured.</summary>
    AuthRequired,

    /// <summary>The device is in an uncertain state and may need manual recovery.</summary>
    RecoveryRequired,

    /// <summary>The operation failed with a definitive error.</summary>
    Failed
}

/// <summary>
/// Categorizes <see cref="OperationState"/> values for guard checks.
/// </summary>
public static class OperationStateExtensions
{
    /// <summary>Returns true if the state is a terminal/error state that requires reset or reconnection.</summary>
    public static bool IsTerminal(this OperationState state) => state switch
    {
        OperationState.Cancelled => true,
        OperationState.TimedOut => true,
        OperationState.Disconnected => true,
        OperationState.Unsupported => true,
        OperationState.AuthRequired => true,
        OperationState.RecoveryRequired => true,
        OperationState.Failed => true,
        _ => false
    };

    /// <summary>Returns true if the state indicates an active session with the device.</summary>
    public static bool HasActiveSession(this OperationState state) => state switch
    {
        OperationState.Handshaking => true,
        OperationState.Identifying => true,
        OperationState.ResolvingLoader => true,
        OperationState.Authorizing => true,
        OperationState.LoadingAgent => true,
        OperationState.Ready => true,
        OperationState.Planning => true,
        OperationState.AwaitingConfirmation => true,
        OperationState.Executing => true,
        OperationState.Verifying => true,
        OperationState.Completed => true,
        _ => false
    };

    /// <summary>Returns true if cancellation can be honored immediately without risk.</summary>
    public static bool IsSafelyCancellable(this OperationState state) => state switch
    {
        OperationState.Idle => false, // nothing to cancel
        OperationState.WaitingForDevice => true,
        OperationState.CandidateDetected => true,
        OperationState.AcquiringTransport => true,
        OperationState.Handshaking => true,
        OperationState.Identifying => true,
        OperationState.ResolvingLoader => true,
        OperationState.Authorizing => true,
        OperationState.LoadingAgent => true,
        OperationState.Ready => true,
        OperationState.Planning => true,
        OperationState.AwaitingConfirmation => true,
        OperationState.Executing => false, // CancelRequested instead
        OperationState.Verifying => false,
        _ => false
    };
}
