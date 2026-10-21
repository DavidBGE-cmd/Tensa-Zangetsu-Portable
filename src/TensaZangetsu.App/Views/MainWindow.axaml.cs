using Avalonia.Controls;
using TensaZangetsu.App.ViewModels;

namespace TensaZangetsu.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Opened += OnOpened;
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
            return;

        try
        {
            await viewModel.InitializeAsync();
        }
        catch (Exception ex)
        {
            viewModel.AddLog($"Initialization failed: {ex.Message}", "#FF3344");
        }
    }
}