using System.Collections.Concurrent;
using TensaZangetsu.Services.Operations;

namespace TensaZangetsu.Services.Engine;

/// <summary>
/// Shared application engine for orchestrating device discovery, state transitions,
/// backend dispatch, safe cancellation, and servicing operations.
/// Consumed identically by both GUI and CLI hosts.
/// </summary>
public sealed class ApplicationEngine
{
    private readonly ConcurrentDictionary<string, IBackendProvider> _backends = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _executionGate = new(1, 1);
    private CancellationTokenSource? _currentOperationCts;
    private readonly DevicePortTrapService _portTrapService;

    public OperationStateMachine StateMachine { get; } = new();

    public event Action<OperationState, OperationState, string?>? StateChanged;
    public event Action<string, string, string?>? LogEmitted;
    public event Action<double, string>? ProgressUpdated;
    public event Action<DetectedDeviceInfo?>? DeviceChanged;

    public DetectedDeviceInfo? CurrentDevice { get; private set; }
    public TrappedDevicePort? CurrentTrappedPort { get; private set; }

    public OperationState CurrentState => StateMachine.CurrentState;
    public bool IsBusy => StateMachine.CurrentState.HasActiveSession() && 
                          StateMachine.CurrentState is OperationState.Executing 
                                                   or OperationState.WaitingForDevice 
                                                   or OperationState.AcquiringTransport 
                                                   or OperationState.Handshaking 
                                                   or OperationState.LoadingAgent 
                                                   or OperationState.Authorizing 
                                                   or OperationState.Verifying;

    public IReadOnlyCollection<IBackendProvider> RegisteredBackends => _backends.Values.ToList().AsReadOnly();

    public ApplicationEngine(DevicePortTrapService? portTrapService = null)
    {
        _portTrapService = portTrapService ?? new DevicePortTrapService();

        StateMachine.StateChanged += args =>
        {
            StateChanged?.Invoke(args.PreviousState, args.CurrentState, args.Reason);
        };

        // Register simulation and read-only platform tools backends
        RegisterBackend(new SimulationBackend());
        RegisterBackend(new AdbFastboot.AdbBackend());
        RegisterBackend(new AdbFastboot.FastbootBackend());
    }

    /// <summary>
    /// Registers a backend provider for a platform (e.g., MTK, QCOM, SPRD, ADB, SIMULATION).
    /// </summary>
    public void RegisterBackend(IBackendProvider backend)
    {
        ArgumentNullException.ThrowIfNull(backend);
        _backends[backend.PlatformId] = backend;
    }

    /// <summary>
    /// Finds a registered backend for the platform.
    /// </summary>
    public IBackendProvider? FindBackend(string platform, bool isSimulation = false)
    {
        if (isSimulation)
        {
            return _backends.TryGetValue("SIMULATION", out var sim) ? sim : null;
        }

        return _backends.TryGetValue(platform, out var backend) && !backend.IsSimulation 
            ? backend 
            : null;
    }

    /// <summary>
    /// Actively waits for a servicing device to connect on USB/COM bus,
    /// tracking states from WaitingForDevice to CandidateDetected and Ready.
    /// </summary>
    public async Task<TrappedDevicePort?> WaitForDeviceAsync(
        string preferredPlatform,
        CancellationToken ct = default)
    {
        if (!StateMachine.TryTransitionTo(OperationState.WaitingForDevice, $"Waiting for {preferredPlatform} device"))
        {
            LogEmitted?.Invoke($"Cannot wait for device while in state {CurrentState}.", "WARNING", "#FFD700");
            return null;
        }

        LogEmitted?.Invoke($"▶ [BUS MONITOR] Waiting for {preferredPlatform} device in BROM / Preloader / EDL 9008 / Diag...", "INFO", "#E0FFE8");
        ProgressUpdated?.Invoke(5, "Waiting for device connection...");

        try
        {
            var trapped = await _portTrapService.WaitForDevicePortAsync(
                preferredPlatform: preferredPlatform,
                ct: ct,
                logCallback: (msg, color) => LogEmitted?.Invoke(msg, "INFO", color));

            CurrentTrappedPort = trapped;
            CurrentDevice = new DetectedDeviceInfo(
                PortOrUsbPath: trapped.PortName,
                VendorId: trapped.VendorId.HasValue ? $"0x{trapped.VendorId:X4}" : "Unknown",
                ProductId: trapped.ProductId.HasValue ? $"0x{trapped.ProductId:X4}" : "Unknown",
                ChipName: trapped.Description,
                Description: trapped.Description,
                IsConnected: true);

            DeviceChanged?.Invoke(CurrentDevice);

            StateMachine.TryTransitionTo(OperationState.CandidateDetected, $"Device detected: {trapped.Description} ({trapped.PortName})");
            StateMachine.TryTransitionTo(OperationState.Ready, $"Device ready on {trapped.PortName}");

            ProgressUpdated?.Invoke(20, $"Device online: {trapped.PortName}");
            return trapped;
        }
        catch (OperationCanceledException)
        {
            StateMachine.TryTransitionTo(OperationState.Cancelled, "Device wait cancelled by user.");
            ProgressUpdated?.Invoke(0, "Cancelled");
            return null;
        }
        catch (Exception ex)
        {
            StateMachine.TryTransitionTo(OperationState.Failed, ex.Message);
            LogEmitted?.Invoke($"❌ Error detecting device: {ex.Message}", "ERROR", "#FF1744");
            return null;
        }
    }

    /// <summary>
    /// Executes an operation fail-closed. If no verified backend exists or the operation
    /// is unsupported, returns Unavailable immediately without sending commands or opening transport.
    /// </summary>
    public async Task<OperationResult> ExecuteOperationAsync(
        OperationRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Enforce serialized session gate
        if (!await _executionGate.WaitAsync(0, ct).ConfigureAwait(false))
        {
            string busyMsg = "Another operation is already in progress. Wait for it to finish or cancel it.";
            LogEmitted?.Invoke($"⚠️ {busyMsg}", "WARNING", "#FFD700");
            return OperationResult.MakeFailed(busyMsg);
        }

        _currentOperationCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var activeToken = _currentOperationCts.Token;

        try
        {
            // 1. Resolve backend
            var backend = FindBackend(request.Platform, request.IsSimulation);

            if (backend is null)
            {
                string unavailableMsg = request.IsSimulation
                    ? "Offline simulation backend is not registered."
                    : $"No hardware backend is registered for platform '{request.Platform}'. No transport was opened and no device command was sent.";

                LogEmitted?.Invoke($"Unavailable: {request.OperationName}. {unavailableMsg}", "WARNING", "#FFD700");
                ProgressUpdated?.Invoke(0, "Operation unavailable");
                return OperationResult.MakeUnavailable(request.OperationName, unavailableMsg);
            }

            var supportLevel = backend.GetSupportLevel(request.OperationName, request.ModelName);
            if (supportLevel == OperationSupportLevel.Unavailable)
            {
                string unsuppMsg = $"Operation '{request.OperationName}' is unavailable on {backend.DisplayName} for model '{request.ModelName ?? "Generic"}'.";
                LogEmitted?.Invoke($"Unavailable: {unsuppMsg}", "WARNING", "#FFD700");
                ProgressUpdated?.Invoke(0, "Operation unavailable");
                return OperationResult.MakeUnavailable(request.OperationName, unsuppMsg);
            }

            // 2. Transition state machine to Executing
            if (!StateMachine.TryTransitionTo(OperationState.Executing, $"Executing {request.OperationName} via {backend.DisplayName}"))
            {
                // If we were Idle, transition through Planning/Ready to Executing
                if (StateMachine.IsIdle)
                {
                    StateMachine.TryTransitionTo(OperationState.Planning, "Planning operation");
                    StateMachine.TryTransitionTo(OperationState.Executing, $"Executing {request.OperationName}");
                }
            }

            var reporter = new EngineProgressReporter(this);

            // 3. Dispatch to backend
            var result = await backend.ExecuteOperationAsync(request, reporter, activeToken).ConfigureAwait(false);

            if (result.Success)
            {
                StateMachine.TryTransitionTo(OperationState.Completed, $"{request.OperationName} completed");
            }
            else
            {
                StateMachine.TryTransitionTo(result.FinalState, result.Message);
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            StateMachine.TryTransitionTo(OperationState.Cancelled, "Operation cancelled by user.");
            LogEmitted?.Invoke("🛑 Operation cancelled by user.", "WARNING", "#FFD700");
            ProgressUpdated?.Invoke(0, "Cancelled");
            return OperationResult.MakeCancelled();
        }
        catch (Exception ex)
        {
            StateMachine.TryTransitionTo(OperationState.Failed, ex.Message);
            LogEmitted?.Invoke($"❌ Operation failed: {ex.Message}", "ERROR", "#FF1744");
            ProgressUpdated?.Invoke(0, "Failed");
            return OperationResult.MakeFailed(ex.Message, ex);
        }
        finally
        {
            _currentOperationCts?.Dispose();
            _currentOperationCts = null;
            _executionGate.Release();
        }
    }

    /// <summary>
    /// Requests cancellation of the currently running operation at the earliest safe boundary.
    /// </summary>
    public void CancelCurrentOperation()
    {
        try
        {
            _currentOperationCts?.Cancel();
        }
        catch (ObjectDisposedException) { }
    }

    /// <summary>
    /// Resets the engine session and returns the state machine to Idle.
    /// </summary>
    public void ResetSession(string? reason = "Session reset")
    {
        CancelCurrentOperation();
        CurrentDevice = null;
        CurrentTrappedPort = null;
        DeviceChanged?.Invoke(null);
        StateMachine.Reset(reason);
        ProgressUpdated?.Invoke(0, "Idle");
        LogEmitted?.Invoke($"[ENGINE] {reason}.", "INFO", "#80FF9E");
    }

    private sealed class EngineProgressReporter(ApplicationEngine engine) : IOperationProgressReporter
    {
        public void ReportProgress(double percentage, string statusMessage, long bytesTransferred = 0, long totalBytes = 0)
        {
            engine.ProgressUpdated?.Invoke(percentage, statusMessage);
        }

        public void ReportLog(string message, string level = "INFO", string? colorHex = null)
        {
            engine.LogEmitted?.Invoke(message, level, colorHex);
        }
    }
}
