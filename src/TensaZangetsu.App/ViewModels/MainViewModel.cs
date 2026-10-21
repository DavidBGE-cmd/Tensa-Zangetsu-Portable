using System.Collections.ObjectModel;
using System.IO.Ports;
using System.Text.Json;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Diagnostics;
using Avalonia.Media.Imaging;
using TensaZangetsu.App.Services;
using TensaZangetsu.Protocol.Abstractions;
using TensaZangetsu.Protocol.Mtk.Abstractions;
using TensaZangetsu.Services.AdbFastboot;
using TensaZangetsu.Services.Catalog;
using TensaZangetsu.Services.Engine;
using TensaZangetsu.Services.Firmware;
using TensaZangetsu.Services.Loaders;
using TensaZangetsu.Services.Operations;
using CatalogDeviceModel = TensaZangetsu.Services.Catalog.DeviceModel;

namespace TensaZangetsu.App.ViewModels;

public sealed class LogDisplayItem
{
    public string Time { get; }
    public string Message { get; }
    public string ColorHex { get; }

    public LogDisplayItem(string time, string message, string colorHex = "#ECEFF1")
    {
        Time = time;
        Message = message;
        ColorHex = colorHex;
    }
}

public sealed partial class FlashPartitionRow : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected = true;

    public int Index { get; set; }
    public string Name { get; set; } = string.Empty;
    public string LengthFormatted { get; set; } = "0 B";
    public long LengthBytes { get; set; }
    public string Type { get; set; } = "EMMC";
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
}

public partial class MainViewModel : ViewModelBase
{
    private readonly ApplicationEngine _engine;
    private readonly DevicePortTrapService _portTrapService = new();
    private readonly StorageDialogService _storageDialog = new();
    private readonly List<FlashPartitionRow> _allPartitions = [];
    private readonly List<CatalogDeviceModel> _allKnownModels = [];
    private IReadOnlyList<TestPointEntry> _indexedTestPoints = [];
    private readonly SemaphoreSlim _initializationGate = new(1, 1);
    private readonly SemaphoreSlim _portRefreshGate = new(1, 1);
    private bool _isInitialized;
    private CancellationTokenSource? _currentOperationCts;
    private CancellationTokenSource? _scatterLoadCts;
    private Task _scatterLoadTask = Task.CompletedTask;
    private CancellationTokenSource? _testPointPreviewCts;
    private Task _testPointPreviewTask = Task.CompletedTask;

    public MainViewModel() : this(new ApplicationEngine())
    {
    }

    public MainViewModel(ApplicationEngine engine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));

        _engine.StateChanged += (prev, curr, reason) =>
        {
            PostToUi(() =>
            {
                OnEngineStateChanged(prev, curr, reason);
            });
        };

        _engine.LogEmitted += (msg, lvl, col) =>
        {
            AddLog(msg, col ?? "#ECEFF1");
        };

        _engine.ProgressUpdated += (pct, status) =>
        {
            PostToUi(() =>
            {
                ProgressValue = (int)pct;
                ProgressStatusText = status;
            });
        };

        _engine.DeviceChanged += dev =>
        {
            PostToUi(() =>
            {
                IsDeviceOnline = dev != null && dev.IsConnected;
                if (dev != null)
                {
                    ConnectionStatusBadge = $"ONLINE • {dev.Description} ({dev.PortOrUsbPath})";
                    ConnectionBadgeColor = "#00FF66";
                }
                else
                {
                    ConnectionStatusBadge = "STANDBY • No device connected";
                    ConnectionBadgeColor = "#264D31";
                }
            });
        };
    }

    public static Action<Action>? CustomUiDispatcher { get; set; }

    private static void PostToUi(Action action)
    {
        if (CustomUiDispatcher is not null)
        {
            CustomUiDispatcher(action);
            return;
        }

        if (Dispatcher.UIThread is { } dispatcher)
        {
            if (dispatcher.CheckAccess())
            {
                action();
            }
            else
            {
                dispatcher.Post(action);
            }
        }
        else
        {
            action();
        }
    }

    private void OnEngineStateChanged(OperationState prev, OperationState curr, string? reason)
    {
        switch (curr)
        {
            case OperationState.Idle:
                ConnectionStatusBadge = SelectedLoader == "Demo / Emulación Offline"
                    ? "STANDBY • [MODO SIMULACIÓN] Emulación Offline"
                    : "STANDBY • Hardware backend unavailable";
                ConnectionBadgeColor = SelectedLoader == "Demo / Emulación Offline" ? "#FFD700" : "#264D31";
                break;
            case OperationState.WaitingForDevice:
                ConnectionStatusBadge = "LISTENING • Waiting for device...";
                ConnectionBadgeColor = "#FFD600";
                break;
            case OperationState.CandidateDetected:
                ConnectionStatusBadge = "DETECTED • Candidate device on bus";
                ConnectionBadgeColor = "#00E5FF";
                break;
            case OperationState.AcquiringTransport:
            case OperationState.Handshaking:
            case OperationState.Identifying:
            case OperationState.ResolvingLoader:
            case OperationState.Authorizing:
            case OperationState.LoadingAgent:
                ConnectionStatusBadge = $"SYNC • {curr}";
                ConnectionBadgeColor = "#00E5FF";
                break;
            case OperationState.Ready:
                ConnectionStatusBadge = "ONLINE • Session active and ready";
                ConnectionBadgeColor = "#00FF66";
                break;
            case OperationState.Executing:
                ConnectionStatusBadge = $"BUSY • Executing ({reason ?? "Operation"})";
                ConnectionBadgeColor = "#00E5FF";
                break;
            case OperationState.Completed:
                ConnectionStatusBadge = "SUCCESS • Operation completed";
                ConnectionBadgeColor = "#00FF66";
                break;
            case OperationState.Cancelled:
                ConnectionStatusBadge = "CANCELLED • Operation cancelled";
                ConnectionBadgeColor = "#FFD600";
                break;
            case OperationState.Failed:
                ConnectionStatusBadge = $"ERROR • {reason ?? "Failed"}";
                ConnectionBadgeColor = "#FF3344";
                break;
            case OperationState.Unsupported:
                ConnectionStatusBadge = "UNSUPPORTED • Operation unavailable";
                ConnectionBadgeColor = "#FFD600";
                break;
            case OperationState.TimedOut:
                ConnectionStatusBadge = "TIMEOUT • Device unresponsive";
                ConnectionBadgeColor = "#FF3344";
                break;
            case OperationState.Disconnected:
                ConnectionStatusBadge = "DISCONNECTED • Device lost";
                ConnectionBadgeColor = "#7A2424";
                break;
            case OperationState.RecoveryRequired:
                ConnectionStatusBadge = "RECOVERY REQUIRED • Manual action needed";
                ConnectionBadgeColor = "#FF1744";
                break;
        }
    }

    // ================== NAVEGACIÓN MODULAR DE ESTUDIO (TABS LATERALES) ==================
    [ObservableProperty]
    private int _selectedNavigationModule = 0; // 0 = Conexión & Boot, 1 = Servicios & Seguridad, 2 = Gestión de Particiones, 3 = Ayuda & TestPoints, 4 = Ajustes & Engine

    public bool IsModule0Active => SelectedNavigationModule == 0;
    public bool IsModule1Active => SelectedNavigationModule == 1;
    public bool IsModule2Active => SelectedNavigationModule == 2;
    public bool IsModule3Active => SelectedNavigationModule == 3;
    public bool IsModule4Active => SelectedNavigationModule == 4;

    partial void OnSelectedNavigationModuleChanged(int value)
    {
        OnPropertyChanged(nameof(IsModule0Active));
        OnPropertyChanged(nameof(IsModule1Active));
        OnPropertyChanged(nameof(IsModule2Active));
        OnPropertyChanged(nameof(IsModule3Active));
        OnPropertyChanged(nameof(IsModule4Active));
    }

    [RelayCommand]
    public void SelectModule(object? moduleParam)
    {
        if (moduleParam is int i)
            SelectedNavigationModule = i;
        else if (moduleParam is string s && int.TryParse(s, out int parsed))
            SelectedNavigationModule = parsed;
    }

    // ================== ESTADO DE CONEXIÓN EN TIEMPO REAL ==================
    [ObservableProperty]
    private string _connectionStatusBadge = "STANDBY • Hardware backend unavailable";

    [ObservableProperty]
    private string _connectionBadgeColor = "#264D31";

    [ObservableProperty]
    private bool _isDeviceOnline;

    // ================== CONEXIÓN Y PUERTO ==================
    [ObservableProperty]
    private string? _selectedPort;

    [ObservableProperty]
    private ObservableCollection<string> _availablePorts = [];

    [ObservableProperty]
    private string _selectedBase = "Mediatek";

    [ObservableProperty]
    private ObservableCollection<string> _availableBases = ["Mediatek", "Qualcomm", "Unisoc", "Samsung", "ADB", "Fastboot"];

    [ObservableProperty]
    private string _selectedLoader = "No verified backend";

    [ObservableProperty]
    private ObservableCollection<string> _availableLoaders = [
        "No verified backend",
        "Demo / Emulación Offline"
    ];

    partial void OnSelectedLoaderChanged(string value)
    {
        if (value == "Demo / Emulación Offline")
        {
            ConnectionStatusBadge = "STANDBY • [MODO SIMULACIÓN] Emulación Offline";
            ConnectionBadgeColor = "#FFD700";
            AddLog("💡 [MODO SIMULACIÓN] Backend de simulación offline seleccionado. Las operaciones se ejecutarán de forma emulada sin abrir puertos físicos.", "#80FF9E");
        }
        else
        {
            ConnectionStatusBadge = "STANDBY • Hardware backend unavailable";
            ConnectionBadgeColor = "#264D31";
        }
    }

    // ================== FILTRO POR MARCA Y MODELO ==================
    [ObservableProperty]
    private string _selectedBrand = "Todas";

    [ObservableProperty]
    private ObservableCollection<string> _availableBrands = [
        "Todas",
        "Samsung",
        "Xiaomi / Poco",
        "Oppo / Realme",
        "Huawei / Honor",
        "Motorola",
        "Vivo",
        "Transsion (Infinix/Tecno)",
        "TCL / Alcatel",
        "Generic"
    ];

    partial void OnSelectedBrandChanged(string value)
    {
        FilterModelsByBrand();
    }

    private void FilterModelsByBrand()
    {
        AvailableDeviceModels.Clear();
        IEnumerable<CatalogDeviceModel> devices = _allKnownModels;
        if (!SelectedBrand.Equals("Todas", StringComparison.OrdinalIgnoreCase))
        {
            string[] brandFilters = SelectedBrand
                .Split('(')[0]
                .Split('/')
                .Select(brand => brand.Trim())
                .Where(brand => brand.Length > 0)
                .ToArray();
            devices = devices.Where(device => brandFilters.Any(brand =>
                device.Brand.Contains(brand, StringComparison.OrdinalIgnoreCase)));
        }

        foreach (CatalogDeviceModel device in devices)
            AvailableDeviceModels.Add(device.Name);

        if (AvailableDeviceModels.Count > 0)
            SelectedDeviceModel = AvailableDeviceModels[0];
        else
            SelectedDeviceModel = null;
    }

    // ================== MULTI-DA & MODELO (CATÁLOGO DINÁMICO) ==================
    [ObservableProperty]
    private string? _selectedDeviceModel;

    [ObservableProperty]
    private ObservableCollection<string> _availableDeviceModels = [];

    [ObservableProperty]
    private MtkDaVariant? _selectedDaVariant;

    [ObservableProperty]
    private ObservableCollection<MtkDaVariant> _availableDaVariants = [];

    [ObservableProperty]
    private bool _hasMultipleDaVariants;

    [ObservableProperty]
    private bool _isAutoDaFallbackEnabled;

    [ObservableProperty]
    private string _daVariantSummary = "No hay variantes de loader verificadas.";

    [RelayCommand]
    public void FallbackToNextDaVariant()
    {
        AddLog("Unavailable: no verified loader variants are configured for this profile.", "#FFD600");
    }

    partial void OnSelectedDeviceModelChanged(string? value)
    {
        AvailableDaVariants.Clear();
        _engine.ResetSession("Model changed by user");
        _allPartitions.Clear();
        Partitions.Clear();

        if (string.IsNullOrWhiteSpace(value))
        {
            HasMultipleDaVariants = false;
            SelectedDaVariant = null;
            DaVariantSummary = "Sin modelo seleccionado.";
            return;
        }

        SelectedDaVariant = null;
        HasMultipleDaVariants = false;
        CustomDaPath = null;
        CustomAuthPath = null;
        DaVariantSummary = "Sin variantes verificadas para el modelo seleccionado.";
        SelectedTestPoint = null;
    }

    partial void OnSelectedDaVariantChanged(MtkDaVariant? value)
    {
        if (value == null) return;
        CustomDaPath = value.FilePath;
        if (!string.IsNullOrEmpty(value.AssociatedAuthPath))
            CustomAuthPath = value.AssociatedAuthPath;
        DaVariantSummary = $"Patch/OS: {value.SecurityPatchOrOs} | BIT: {value.BinaryBit ?? "N/A"} | Rol: {value.OperationRole} | Stage: {value.ConnectionStage}";
        AddLog($"[AutoLoader] Variante manual seleccionada: {value.DisplayLabel}", "#00E676");
    }

    // ================== VISOR INTERACTIVO DE TESTPOINTS ==================
    [ObservableProperty]
    private ObservableCollection<TestPointEntry> _testPointsList = [];

    [ObservableProperty]
    private TestPointEntry? _selectedTestPoint;

    [ObservableProperty]
    private string _testPointSearchQuery = string.Empty;

    [ObservableProperty]
    private Bitmap? _selectedTestPointBitmap;

    [ObservableProperty]
    private string _testPointDetails = "Las imágenes y su asociación a modelos/revisiones no están verificadas.";

    [ObservableProperty]
    private bool _hasSelectedTestPoint;

    partial void OnSelectedTestPointChanged(TestPointEntry? value)
    {
        _testPointPreviewCts?.Cancel();
        _testPointPreviewCts = null;
        SelectedTestPointBitmap?.Dispose();
        SelectedTestPointBitmap = null;

        if (value is null)
        {
            HasSelectedTestPoint = false;
            TestPointDetails = "Las imágenes y su asociación a modelos/revisiones no están verificadas.";
            return;
        }

        HasSelectedTestPoint = false;
        TestPointDetails = $"Cargando diagrama: {value.ModelName}...";
        var previewCts = new CancellationTokenSource();
        _testPointPreviewCts = previewCts;
        _testPointPreviewTask = LoadTestPointPreviewAsync(value, previewCts);
    }

    private async Task LoadTestPointPreviewAsync(TestPointEntry entry, CancellationTokenSource previewCts)
    {
        Bitmap? bitmap = null;
        try
        {
            bitmap = await Task.Run(() =>
            {
                previewCts.Token.ThrowIfCancellationRequested();
                using var stream = File.OpenRead(entry.FilePath);
                return new Bitmap(stream);
            }, previewCts.Token);
            previewCts.Token.ThrowIfCancellationRequested();

            SelectedTestPointBitmap = bitmap;
            bitmap = null;
            HasSelectedTestPoint = true;
            TestPointDetails = $"Imagen no verificada: {entry.ModelName} | Marca inferida: {entry.Brand} | {entry.ImageFormat.ToUpperInvariant()} ({FormatBytes(entry.FileSizeBytes)})";
            AddLog($"[TestPoint] Diagrama cargado: {entry.ModelName} ({entry.Brand})", "#00E5FF");
        }
        catch (OperationCanceledException) when (previewCts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            HasSelectedTestPoint = false;
            TestPointDetails = $"Error al cargar imagen: {ex.Message}";
            AddLog($"[TestPoint] Error al abrir imagen: {ex.Message}", "#FF1744");
        }
        finally
        {
            bitmap?.Dispose();
            if (ReferenceEquals(_testPointPreviewCts, previewCts))
                _testPointPreviewCts = null;
            previewCts.Dispose();
        }
    }

    partial void OnTestPointSearchQueryChanged(string value)
    {
        FilterTestPoints();
    }

    private void FilterTestPoints()
    {
        IEnumerable<TestPointEntry> filtered = _indexedTestPoints;
        if (!string.IsNullOrWhiteSpace(TestPointSearchQuery))
        {
            string query = TestPointSearchQuery.Trim();
            filtered = filtered.Where(item =>
                item.ModelName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                item.Brand.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        TestPointsList.Clear();
        foreach (var item in filtered)
            TestPointsList.Add(item);

        if (SelectedTestPoint is not null && !TestPointsList.Contains(SelectedTestPoint))
            SelectedTestPoint = null;
    }

    [RelayCommand]
    public Task OpenTestPointExternalAsync()
    {
        string? filePath = SelectedTestPoint?.FilePath;
        if (filePath is null)
            return Task.CompletedTask;

        return Task.Run(() =>
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    AddLog("El archivo de imagen seleccionado ya no existe.", "#FFAB00");
                    return;
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = filePath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                AddLog($"Error al abrir visor externo: {ex.Message}", "#FFAB00");
            }
        });
    }

    [RelayCommand]
    public void SearchTestPointForCurrentModel()
    {
        if (!string.IsNullOrEmpty(SelectedDeviceModel))
        {
            SelectedNavigationModule = 3; // Cambiar a pestaña TestPoints
            TestPointSearchQuery = SelectedDeviceModel;
        }
    }

    // ================== COMANDOS QUALCOMM FASTBOOT / EDL ==================
    [RelayCommand]
    public async Task FastbootToEdlAsync()
    {
        await ExecuteServicingOperationAsync("FastbootToEdl");
    }

    [RelayCommand]
    public async Task AdbToEdlAsync()
    {
        await ExecuteServicingOperationAsync("AdbToEdl");
    }

    // ================== COMANDO REPARACIÓN GPT (AMONET) ==================
    [RelayCommand]
    public async Task AmonetGptFixAsync()
    {
        await ExecuteServicingOperationAsync("AmonetGptFix");
    }

    // ================== CUSTOM LOADERS / EXPANDER ==================
    [ObservableProperty]
    private bool _isCustomExpanded;

    [ObservableProperty]
    private string? _customDaPath;

    [ObservableProperty]
    private string? _customPreloaderPath;

    [ObservableProperty]
    private string? _customAuthPath;

    // ================== CONSOLA DE LOGS ==================
    [ObservableProperty]
    private ObservableCollection<LogDisplayItem> _logMessages = [];

    // ================== BARRA DE ESTADO / PROGRESO ==================
    [ObservableProperty]
    private string _progressStatusText = "Idle";

    [ObservableProperty]
    private int _progressValue = 0;

    [ObservableProperty]
    private bool _isBusy;

    // ================== SERVICIO (TAB 1) ==================
    [ObservableProperty]
    private string _selectedFactoryResetTask = "Format MISC";

    [ObservableProperty]
    private ObservableCollection<string> _factoryResetTasks = ["Format MISC", "Wipe Userdata", "Safe Format (Preserve Photos)"];

    [ObservableProperty]
    private string _selectedSecurityTask = "Backup";

    [ObservableProperty]
    private ObservableCollection<string> _securityTasks = ["Backup", "Restore", "Erase"];

    [ObservableProperty]
    private string _selectedMetaTask = "Remove FRP Tecno/Infinix/Itel";

    [ObservableProperty]
    private ObservableCollection<string> _metaTasks = [
        "Remove FRP Tecno/Infinix/Itel",
        "Factory Reset Meta",
        "Read Meta Info",
        "Dump NVRAM Meta"
    ];

    [ObservableProperty]
    private string _selectedMt62xxTask = "FactoryReset/Reset Data MT62xx";

    [ObservableProperty]
    private ObservableCollection<string> _mt62xxTasks = ["FactoryReset/Reset Data MT62xx", "Read Flash MT62xx", "Format FAT MT62xx"];

    // ================== FLASH (TAB 2) ==================
    [ObservableProperty]
    private string _selectedRebootType = "Mediatek TXT,XML Scatter";

    [ObservableProperty]
    private ObservableCollection<string> _rebootTypes = [
        "Mediatek TXT,XML Scatter",
        "Qualcomm rawprogram.xml",
        "UNISOC .PAC Firmware"
    ];

    [ObservableProperty]
    private string? _firmwareScatterPath;

    [ObservableProperty]
    private string _scatterFilterQuery = string.Empty;

    [ObservableProperty]
    private ObservableCollection<FlashPartitionRow> _partitions = [];

    [ObservableProperty]
    private FlashPartitionRow? _selectedPartition;

    // ================== TOOLS (TAB 3) ==================
    [ObservableProperty]
    private string _selectedRpmbTask = "Read";

    [ObservableProperty]
    private ObservableCollection<string> _rpmbTasks = ["Read", "Write", "Erase"];

    [ObservableProperty]
    private string _selectedOemInfoTask = "Read";

    [ObservableProperty]
    private ObservableCollection<string> _oemInfoTasks = ["Read", "Write", "Erase"];

    [ObservableProperty]
    private string _selectedLkTask = "Patch Boot Verification (Orange State)";

    [ObservableProperty]
    private ObservableCollection<string> _lkTasks = [
        "Patch Boot Verification (Orange State)",
        "Bypass Certificate Check",
        "Inject bl2_ext Payload"
    ];

    // ================== SETTINGS (TAB 4) ==================
    [ObservableProperty]
    private int _baudRate = 921600;

    [ObservableProperty]
    private bool _autoReboot = true;

    [ObservableProperty]
    private bool _verifyWrite = true;

    [ObservableProperty]
    private bool _useHighSpeedUsb = true;

    [ObservableProperty]
    private string _detectedChipsetBanner = "Chipset ID : Not detected";

    private void ShowBannerWelcome()
    {
        AddLog("Tensa-Zangetsu Portable", "#00FF66");
        AddLog("No device-operation backend is currently verified; hardware actions remain unavailable.", "#FFD600");
        AddLog("Catalog entries, asset names, and diagrams do not establish device or operation compatibility.", "#E0FFE8");
        AddLog("----------------------------------------------------------------", "#1B4D28");
    }

    public async Task InitializeAsync()
    {
        await _initializationGate.WaitAsync();
        try
        {
            if (_isInitialized)
                return;

            var modelsTask = Task.Run(() => DeviceCatalogRepository.GetAll().ToArray());
            var testPointsTask = Task.Run(() => new MtkTestPointCatalogService().GetAll().ToArray());
            await Task.WhenAll(modelsTask, testPointsTask);

            _allKnownModels.Clear();
            _allKnownModels.AddRange(await modelsTask);
            _indexedTestPoints = await testPointsTask;
            FilterModelsByBrand();
            FilterTestPoints();
            ShowBannerWelcome();
            await RefreshPortsAsync();
            _isInitialized = true;
        }
        finally
        {
            _initializationGate.Release();
        }
    }

    public void AddLog(string message, string colorHex = "#ECEFF1")
    {
        PostToUi(() =>
        {
            string time = DateTime.Now.ToString("HH:mm:ss");
            LogMessages.Add(new LogDisplayItem(time, message, colorHex));
            if (LogMessages.Count > 1000) LogMessages.RemoveAt(0);
        });
    }

    [RelayCommand]
    public async Task RefreshPortsAsync()
    {
        await _portRefreshGate.WaitAsync();
        try
        {
            string[] ports = await Task.Run(SerialPort.GetPortNames);
            AvailablePorts.Clear();
            foreach (var p in ports)
                AvailablePorts.Add(p);

            if (SelectedPort is null || !AvailablePorts.Contains(SelectedPort))
                SelectedPort = AvailablePorts.Count > 0 ? AvailablePorts[0] : null;

            ConnectionStatusBadge = "STANDBY • No validated device backend";
            ConnectionBadgeColor = "#264D31";
            IsDeviceOnline = false;
        }
        catch (Exception ex)
        {
            ConnectionStatusBadge = "PORT SCAN FAILED";
            ConnectionBadgeColor = "#7A2424";
            IsDeviceOnline = false;
            AddLog($"Error al enumerar puertos: {ex.Message}", "#FF3344");
        }
        finally
        {
            _portRefreshGate.Release();
        }
    }

    [RelayCommand]
    public void ClearLogs()
    {
        LogMessages.Clear();
    }

    [RelayCommand]
    public async Task CancelOperationAsync()
    {
        _engine.CancelCurrentOperation();

        if (_currentOperationCts != null && !_currentOperationCts.IsCancellationRequested)
        {
            AddLog("🛑 Solicitando cancelación de operación...", "#FF1744");
            _currentOperationCts.Cancel();
        }

        if (_scatterLoadCts is not null)
        {
            AddLog("Cancelando la lectura local de metadatos...", "#FFD600");
            _scatterLoadCts.Cancel();
            await _scatterLoadTask;
        }

        if (_testPointPreviewCts is not null)
        {
            _testPointPreviewCts.Cancel();
            await _testPointPreviewTask;
        }
    }

    // =========================================================================
    // =========================================================================
    // COMANDOS DE EXPLORACIÓN DE ARCHIVOS (STORAGE PICKER)
    // =========================================================================
    [RelayCommand]
    public async Task BrowseCustomDaAsync()
    {
        var path = await _storageDialog.OpenFileDialogAsync("Seleccionar Download Agent (DA)", ("Archivos DA (*.bin)", ["*.bin"]));
        if (!string.IsNullOrEmpty(path))
        {
            CustomDaPath = path;
            AddLog($"DA personalizado seleccionado: {Path.GetFileName(path)}", "#00E5FF");
        }
    }

    [RelayCommand]
    public async Task BrowseCustomPreloaderAsync()
    {
        var path = await _storageDialog.OpenFileDialogAsync("Seleccionar Preloader EMI", ("Preloader (*.bin)", ["*.bin"]));
        if (!string.IsNullOrEmpty(path))
        {
            CustomPreloaderPath = path;
            AddLog($"Preloader personalizado seleccionado: {Path.GetFileName(path)}", "#00E5FF");
        }
    }

    [RelayCommand]
    public async Task BrowseCustomAuthAsync()
    {
        var path = await _storageDialog.OpenFileDialogAsync("Seleccionar Archivo de Autenticación", ("Archivos Auth (*.auth, *.bin)", ["*.auth", "*.bin"]));
        if (!string.IsNullOrEmpty(path))
        {
            CustomAuthPath = path;
            AddLog($"Auth personalizado seleccionado: {Path.GetFileName(path)}", "#00E5FF");
        }
    }

    [RelayCommand]
    public async Task BrowseFirmwareAsync()
    {
        var path = await _storageDialog.OpenFileDialogAsync(
            "Seleccionar Firmware / Scatter",
            ("Archivos de Firmware / Scatter", ["*.txt", "*.xml", "*.ofp", "*.ops", "*.pac", "*.bin"]),
            ("MTK Scatter (*.txt, *.xml)", ["*.txt", "*.xml"]),
            ("Paquetes ROM (*.ofp, *.ops, *.pac, *.ozip, payload.bin)", ["*.ofp", "*.ops", "*.pac", "*.ozip", "payload.bin"]),
            ("Todos los archivos", ["*.*"]));

        if (!string.IsNullOrEmpty(path))
        {
            FirmwareScatterPath = path;
        }
    }

    partial void OnFirmwareScatterPathChanged(string? value)
    {
        _scatterLoadCts?.Cancel();
        _scatterLoadCts = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            _allPartitions.Clear();
            ApplyPartitionFilter();
            return;
        }

        var loadCts = new CancellationTokenSource();
        _scatterLoadCts = loadCts;
        _scatterLoadTask = LoadScatterOrRomAsync(value, loadCts);
    }

    partial void OnScatterFilterQueryChanged(string value)
    {
        ApplyPartitionFilter();
    }

    private void ApplyPartitionFilter()
    {
        if (string.IsNullOrWhiteSpace(ScatterFilterQuery))
        {
            Partitions = new ObservableCollection<FlashPartitionRow>(_allPartitions);
        }
        else
        {
            var terms = ScatterFilterQuery.Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var filtered = _allPartitions.Where(p =>
                terms.Any(t => p.Name.Contains(t, StringComparison.OrdinalIgnoreCase) ||
                               p.FileName.Contains(t, StringComparison.OrdinalIgnoreCase) ||
                               p.Type.Contains(t, StringComparison.OrdinalIgnoreCase))).ToList();
            Partitions = new ObservableCollection<FlashPartitionRow>(filtered);
        }
    }

    private async Task LoadScatterOrRomAsync(string path, CancellationTokenSource loadCts)
    {
        try
        {
            ProgressStatusText = "Parsing firmware metadata...";
            List<FlashPartitionRow> parsedPartitions = await Task.Run(() =>
            {
                loadCts.Token.ThrowIfCancellationRequested();
                string ext = Path.GetExtension(path).ToLowerInvariant();
                string fileName = Path.GetFileName(path);

                if (ext is ".txt" or ".xml")
                {
                    string content = File.ReadAllText(path);
                    loadCts.Token.ThrowIfCancellationRequested();
                    var manifest = MtkScatterManifestParser.Parse(content);
                    string dir = Path.GetDirectoryName(path) ?? "";
                    return manifest.Partitions.Select((partition, index) =>
                    {
                        string fileNameForPartition = partition.FileName ?? $"{partition.Name}.img";
                        long lengthBytes = checked((long)partition.Length);
                        return new FlashPartitionRow
                        {
                            Index = index,
                            Name = partition.Name,
                            LengthBytes = lengthBytes,
                            LengthFormatted = FormatBytes(lengthBytes),
                            Type = partition.Storage.ToString().ToUpperInvariant(),
                            FileName = fileNameForPartition,
                            FilePath = Path.Combine(dir, fileNameForPartition),
                            IsSelected = partition.Download
                        };
                    }).ToList();
                }

                if (ext is ".ofp" or ".ops" or ".pac" or ".ozip" ||
                    fileName.Equals("payload.bin", StringComparison.OrdinalIgnoreCase))
                {
                    var entries = FlashEngineService.InspectRomPackageOffline(path);
                    return entries.Select((entry, index) =>
                    {
                        return new FlashPartitionRow
                        {
                            Index = index,
                            Name = entry.Name,
                            LengthBytes = entry.SizeBytes,
                            LengthFormatted = entry.SizeFormatted,
                            Type = entry.Category,
                            FileName = entry.FileName,
                            FilePath = path,
                            IsSelected = !entry.IsLoader
                        };
                    }).ToList();
                }

                throw new NotSupportedException($"Unsupported firmware metadata format: {ext}");
            }, loadCts.Token);

            loadCts.Token.ThrowIfCancellationRequested();
            _allPartitions.Clear();
            _allPartitions.AddRange(parsedPartitions);
            ApplyPartitionFilter();
            AddLog($"Firmware metadata parsed offline: {Path.GetFileName(path)} ({_allPartitions.Count} entries). No device operation was performed.", "#00E5FF");
            ProgressStatusText = "Metadata loaded";
        }
        catch (OperationCanceledException) when (loadCts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            AddLog($"Error al parsear firmware/scatter: {ex.Message}", "#FF3344");
            ProgressStatusText = "Metadata parsing failed";
        }
        finally
        {
            if (ReferenceEquals(_scatterLoadCts, loadCts))
                _scatterLoadCts = null;
            loadCts.Dispose();
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
        int counter = 0;
        decimal number = bytes;
        while (Math.Round(number / 1024) >= 1)
        {
            number /= 1024;
            counter++;
        }
        return $"{number:n1} {suffixes[counter]}";
    }

    private async Task ExecuteServicingOperationAsync(string operationName)
    {
        if (IsBusy)
        {
            AddLog("⚠️ Hay otra operación en curso. Espera a que finalice o presiona 'STOP / CANCEL'.", "#FFD600");
            return;
        }

        bool isSimulation = SelectedLoader == "Demo / Emulación Offline";
        string platform = SelectedBase switch
        {
            "Mediatek" => "MTK",
            "Qualcomm" => "QCOM",
            "Unisoc" => "SPRD",
            "ADB" => "ADB",
            "Fastboot" => "FASTBOOT",
            _ => SelectedBase.ToUpperInvariant()
        };

        var request = new OperationRequest(
            OperationName: operationName,
            Platform: platform,
            PortName: SelectedPort,
            ModelName: SelectedDeviceModel,
            Brand: SelectedBrand != "Todas" ? SelectedBrand : null,
            Chipset: null,
            IsSimulation: isSimulation);

        IsBusy = true;
        _currentOperationCts = new CancellationTokenSource();

        try
        {
            var result = await Task.Run(() => _engine.ExecuteOperationAsync(request, _currentOperationCts.Token));
            if (!result.Success && result.SupportLevel == OperationSupportLevel.Unavailable)
            {
                ProgressStatusText = "Operation unavailable";
            }
        }
        catch (OperationCanceledException)
        {
            AddLog("🛑 Operación cancelada por el usuario.", "#FFD600");
            ProgressStatusText = "Cancelado";
            ProgressValue = 0;
        }
        catch (Exception ex)
        {
            AddLog($"❌ Error durante {operationName}: {ex.Message}", "#FF3344");
            ProgressStatusText = "Error en la operación";
            ProgressValue = 0;
        }
        finally
        {
            IsBusy = false;
            _currentOperationCts?.Dispose();
            _currentOperationCts = null;
        }
    }

    // =========================================================================
    // SERVICIO: READ INFO / CONNECT / BOOT
    // =========================================================================
    [RelayCommand]
    public async Task ReadInfoAsync()
    {
        await ExecuteServicingOperationAsync("ReadInfo");
    }

    [RelayCommand]
    public async Task ConnectAndReadInfoAsync()
    {
        await ExecuteServicingOperationAsync("ReadInfo");
    }

    [RelayCommand]
    public async Task BootDeviceAsync()
    {
        await ExecuteServicingOperationAsync("ReadInfo");
    }

    [RelayCommand]
    public async Task RebootDeviceAsync()
    {
        await ExecuteServicingOperationAsync("RebootDevice");
    }

    // =========================================================================
    // SERVICIO: REMOVE FRP
    // =========================================================================
    [RelayCommand]
    public async Task RemoveFrpAsync()
    {
        await ExecuteServicingOperationAsync("ResetFrp");
    }

    // =========================================================================
    // SERVICIO: FACTORY RESET
    // =========================================================================
    [RelayCommand]
    public async Task StartFactoryResetTaskAsync()
    {
        await ExecuteServicingOperationAsync($"FactoryReset:{SelectedFactoryResetTask}");
    }

    // =========================================================================
    // SERVICIO: SECURITY / EFS
    // =========================================================================
    [RelayCommand]
    public async Task StartSecurityTaskAsync()
    {
        await ExecuteServicingOperationAsync($"Security:{SelectedSecurityTask}");
    }

    // =========================================================================
    // SERVICIO: META TASKS
    // =========================================================================
    [RelayCommand]
    public async Task StartMetaTaskAsync()
    {
        await ExecuteServicingOperationAsync($"Meta:{SelectedMetaTask}");
    }

    // =========================================================================
    // SERVICIO: MT62XX TASKS
    // =========================================================================
    [RelayCommand]
    public async Task StartMt62xxTaskAsync()
    {
        await ExecuteServicingOperationAsync($"Mt62xx:{SelectedMt62xxTask}");
    }

    // =========================================================================
    // SERVICIO: UNLOCK BOOTLOADER
    // =========================================================================
    [RelayCommand]
    public async Task UnlockBootloaderAsync()
    {
        await ExecuteServicingOperationAsync("UnlockBootloader");
    }

    // =========================================================================
    // SERVICIO: READ HARDWARE KEYS
    // =========================================================================
    [RelayCommand]
    public async Task ReadHardwareKeysAsync()
    {
        await ExecuteServicingOperationAsync("ReadHardwareKeys");
    }

    // =========================================================================
    // SERVICIO: OEM SERVICING (MI ACCOUNT, OTA, HUAWEI ID)
    // =========================================================================
    [RelayCommand]
    public async Task RemoveMiAccountAsync()
    {
        await ExecuteServicingOperationAsync("RemoveMiAccount");
    }

    [RelayCommand]
    public async Task DisableOtaAsync()
    {
        await ExecuteServicingOperationAsync("DisableOta");
    }

    [RelayCommand]
    public async Task RemoveHuaweiIdAsync()
    {
        await ExecuteServicingOperationAsync("RemoveHuaweiId");
    }

    [RelayCommand]
    public async Task RunPythonBypassAsync()
    {
        await ExecuteServicingOperationAsync("PythonBypass");
    }

    // =========================================================================
    // SERVICIO: RPMB & OEMINFO & LK
    // =========================================================================
    [RelayCommand]
    public async Task StartRpmbTaskAsync()
    {
        await ExecuteServicingOperationAsync($"Rpmb:{SelectedRpmbTask}");
    }

    [RelayCommand]
    public async Task StartOemInfoTaskAsync()
    {
        await ExecuteServicingOperationAsync($"OemInfo:{SelectedOemInfoTask}");
    }

    [RelayCommand]
    public async Task StartLkTaskAsync()
    {
        await ExecuteServicingOperationAsync($"LkPatch:{SelectedLkTask}");
    }

    // =========================================================================
    // SERVICIO: FLASH / PARTITION OPERATIONS (READ / WRITE / ERASE)
    // =========================================================================
    [RelayCommand]
    public async Task ReadFlashAsync()
    {
        var selected = Partitions.Where(p => p.IsSelected).ToList();
        if (selected.Count == 0)
        {
            AddLog("⚠️ No hay particiones marcadas para volcar.", "#FFD600");
            return;
        }

        await ExecuteServicingOperationAsync($"ReadPartition ({selected.Count} particiones)");
    }

    [RelayCommand]
    public async Task WriteFlashAsync()
    {
        var selected = Partitions.Where(p => p.IsSelected).ToList();
        if (selected.Count == 0)
        {
            AddLog("⚠️ No hay particiones marcadas para escribir.", "#FFD600");
            return;
        }

        await ExecuteServicingOperationAsync($"WritePartition ({selected.Count} particiones)");
    }

    [RelayCommand]
    public async Task EraseFlashAsync()
    {
        if (SelectedPartition == null)
        {
            AddLog("⚠️ Selecciona una fila en la tabla para borrar.", "#FFD600");
            return;
        }

        await ExecuteServicingOperationAsync($"ErasePartition:{SelectedPartition.Name}");
    }
}
