using Avalonia.Controls;
using Avalonia.Interactivity;
using TensaZangetsu.Mtk.Core;
using TensaZangetsu.Mtk.Infrastructure;

namespace TensaZangetsu.Mtk.App;

public partial class MainWindow : Window
{
    private readonly IMtkDeviceService _service;
    private CancellationTokenSource? _cancellation;

    public MainWindow() : this(new MtkDeviceService())
    {
    }

    public MainWindow(IMtkDeviceService service)
    {
        _service = service;
        InitializeComponent();
    }

    private async void Identify(object? sender, RoutedEventArgs e)
    {
        _cancellation?.Dispose();
        _cancellation = new CancellationTokenSource();
        IdentifyButton.IsEnabled = false;
        CancelButton.IsEnabled = true;
        InfoText.Text = string.Empty;
        LogText.Text = string.Empty;
        StatusText.Text = "Waiting for MTK device...";

        try
        {
            var progress = new Progress<MtkLogEntry>(entry =>
                LogText.Text += $"[{entry.Timestamp:HH:mm:ss}] {entry.Level}: {entry.Message}\n");
            MtkOperationResult result = await _service.IdentifyAsync(progress, _cancellation.Token);
            StatusText.Text = result.Success ? "Identified" : "Unavailable / failed";
            InfoText.Text = result.Device is null
                ? result.Message
                : string.Join('\n',
                    $"Platform: {result.Device.Platform}",
                    $"Chip: {result.Device.ChipName} ({result.Device.ChipDescription})",
                    $"Hardware code: {result.Device.HardwareCode}",
                    $"Subcode: {result.Device.HardwareSubCode}",
                    $"Hardware version: {result.Device.HardwareVersion}",
                    $"Software version: {result.Device.SoftwareVersion}",
                    $"BROM: {result.Device.BromVersion}",
                    $"Preloader: {result.Device.PreloaderVersion}",
                    $"Stage: {result.Device.BootStage}",
                    $"SecureBoot={result.Device.SecureBoot}, SLA={result.Device.Sla}, DAA={result.Device.Daa}",
                    $"CertificateRequired={result.Device.CertificateRequired}",
                    $"Security raw: {result.Device.SecurityRaw}");
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Cancelled";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Failed";
            InfoText.Text = ex.Message;
        }
        finally
        {
            IdentifyButton.IsEnabled = true;
            CancelButton.IsEnabled = false;
            _cancellation?.Dispose();
            _cancellation = null;
        }
    }

    private void Cancel(object? sender, RoutedEventArgs e) => _cancellation?.Cancel();
}
