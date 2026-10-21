namespace TensaZangetsu.Services.Engine;

/// <summary>
/// Event arguments for state machine transitions.
/// </summary>
public sealed record StateChangedEventArgs(
    OperationState PreviousState,
    OperationState CurrentState,
    string? Reason = null,
    DateTime Timestamp = default)
{
    public DateTime Timestamp { get; init; } = Timestamp == default ? DateTime.UtcNow : Timestamp;
}

/// <summary>
/// Thread-safe finite state machine governing device connection, identification,
/// loader injection, authorization, and servicing operations.
/// Prevents invalid transitions and guarantees safe cancellation and terminal state recovery.
/// </summary>
public sealed class OperationStateMachine
{
    private readonly object _syncRoot = new();
    private OperationState _currentState = OperationState.Idle;
    private OperationState _previousState = OperationState.Idle;

    /// <summary>
    /// Triggered whenever a valid state transition takes place.
    /// </summary>
    public event Action<StateChangedEventArgs>? StateChanged;

    /// <summary>
    /// Gets the current state of the machine.
    /// </summary>
    public OperationState CurrentState
    {
        get
        {
            lock (_syncRoot) return _currentState;
        }
    }

    /// <summary>
    /// Gets the previous state before the most recent transition.
    /// </summary>
    public OperationState PreviousState
    {
        get
        {
            lock (_syncRoot) return _previousState;
        }
    }

    /// <summary>Returns true if the machine is in Idle state.</summary>
    public bool IsIdle => CurrentState == OperationState.Idle;

    /// <summary>Returns true if the machine is in a terminal error or completion state.</summary>
    public bool IsTerminal => CurrentState.IsTerminal();

    /// <summary>Returns true if there is an active session established with the device.</summary>
    public bool HasActiveSession => CurrentState.HasActiveSession();

    /// <summary>Returns true if cancellation can be honored immediately at this state.</summary>
    public bool IsSafelyCancellable => CurrentState.IsSafelyCancellable();

    /// <summary>
    /// Checks whether transitioning from the current state to <paramref name="nextState"/> is valid.
    /// </summary>
    public bool CanTransitionTo(OperationState nextState)
    {
        lock (_syncRoot)
        {
            return IsValidTransition(_currentState, nextState);
        }
    }

    /// <summary>
    /// Attempts to transition the state machine to <paramref name="nextState"/>.
    /// Returns true if successful, or false if the transition is prohibited by guards.
    /// </summary>
    public bool TryTransitionTo(OperationState nextState, string? reason = null)
    {
        StateChangedEventArgs? eventArgs = null;
        lock (_syncRoot)
        {
            if (!IsValidTransition(_currentState, nextState))
                return false;

            _previousState = _currentState;
            _currentState = nextState;
            eventArgs = new StateChangedEventArgs(_previousState, _currentState, reason);
        }

        if (eventArgs is not null)
        {
            StateChanged?.Invoke(eventArgs);
        }

        return true;
    }

    /// <summary>
    /// Transitions to <paramref name="nextState"/> or throws <see cref="InvalidOperationException"/> if invalid.
    /// </summary>
    public void TransitionTo(OperationState nextState, string? reason = null)
    {
        if (!TryTransitionTo(nextState, reason))
        {
            lock (_syncRoot)
            {
                throw new InvalidOperationException(
                    $"Invalid state transition from {_currentState} to {nextState}. Reason: {reason ?? "None specified."}");
            }
        }
    }

    /// <summary>
    /// Resets the machine from any state back to <see cref="OperationState.Idle"/>.
    /// </summary>
    public void Reset(string? reason = "Session reset")
    {
        StateChangedEventArgs? eventArgs = null;
        lock (_syncRoot)
        {
            if (_currentState == OperationState.Idle) return;

            _previousState = _currentState;
            _currentState = OperationState.Idle;
            eventArgs = new StateChangedEventArgs(_previousState, OperationState.Idle, reason);
        }

        if (eventArgs is not null)
        {
            StateChanged?.Invoke(eventArgs);
        }
    }

    /// <summary>
    /// Pure function determining whether a transition from <paramref name="from"/> to <paramref name="to"/> is allowed.
    /// </summary>
    public static bool IsValidTransition(OperationState from, OperationState to)
    {
        if (from == to) return true; // Re-entrant no-op allowed

        // Any non-terminal active state can transition to Disconnected, TimedOut, or Failed on unexpected bus/fatal errors
        if (to is OperationState.Disconnected or OperationState.Failed or OperationState.TimedOut)
            return true;

        // If safely cancellable, can transition to Cancelled
        if (to == OperationState.Cancelled && from.IsSafelyCancellable())
            return true;

        // Terminal states can only transition back to Idle (or WaitingForDevice for immediate retry)
        if (from.IsTerminal())
            return to is OperationState.Idle or OperationState.WaitingForDevice;

        return from switch
        {
            OperationState.Idle => to is OperationState.WaitingForDevice
                                     or OperationState.CandidateDetected
                                     or OperationState.AcquiringTransport
                                     or OperationState.Planning,

            OperationState.WaitingForDevice => to is OperationState.CandidateDetected
                                                  or OperationState.AcquiringTransport
                                                  or OperationState.Idle,

            OperationState.CandidateDetected => to is OperationState.AcquiringTransport
                                                   or OperationState.WaitingForDevice
                                                   or OperationState.Unsupported
                                                   or OperationState.Idle,

            OperationState.AcquiringTransport => to is OperationState.Handshaking
                                                    or OperationState.Identifying
                                                    or OperationState.Ready
                                                    or OperationState.Idle,

            OperationState.Handshaking => to is OperationState.Identifying
                                             or OperationState.Ready
                                             or OperationState.Unsupported
                                             or OperationState.Idle,

            OperationState.Identifying => to is OperationState.ResolvingLoader
                                             or OperationState.Ready
                                             or OperationState.Unsupported
                                             or OperationState.AuthRequired,

            OperationState.ResolvingLoader => to is OperationState.Authorizing
                                                or OperationState.LoadingAgent
                                                or OperationState.Ready
                                                or OperationState.Unsupported
                                                or OperationState.AuthRequired,

            OperationState.Authorizing => to is OperationState.LoadingAgent
                                             or OperationState.Ready
                                             or OperationState.AuthRequired,

            OperationState.LoadingAgent => to is OperationState.Ready
                                              or OperationState.Unsupported
                                              or OperationState.AuthRequired,

            OperationState.Ready => to is OperationState.Planning
                                      or OperationState.AwaitingConfirmation
                                      or OperationState.Executing
                                      or OperationState.Idle,

            OperationState.Planning => to is OperationState.AwaitingConfirmation
                                         or OperationState.Executing
                                         or OperationState.Ready
                                         or OperationState.Idle,

            OperationState.AwaitingConfirmation => to is OperationState.Executing
                                                       or OperationState.Ready
                                                       or OperationState.Cancelled
                                                       or OperationState.Idle,

            OperationState.Executing => to is OperationState.Verifying
                                          or OperationState.Completed
                                          or OperationState.RecoveryRequired
                                          or OperationState.Cancelled,

            OperationState.Verifying => to is OperationState.Completed
                                          or OperationState.RecoveryRequired
                                          or OperationState.Cancelled,

            OperationState.Completed => to is OperationState.Ready
                                          or OperationState.Planning
                                          or OperationState.Idle,

            _ => false
        };
    }
}
