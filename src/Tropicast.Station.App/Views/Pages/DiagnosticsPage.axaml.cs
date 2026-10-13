using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace Tropicast.Station.App.Views.Pages;

public partial class DiagnosticsPage : UserControl
{
    private Diagnostics? _diagnostics;

    public DiagnosticsPage() => InitializeComponent();

    /// <summary>Set by the main window once the host has created the diagnostics service.</summary>
    internal Diagnostics? Diagnostics
    {
        get => _diagnostics;
        set
        {
            _diagnostics = value;
            LogDirectory.Text = value?.LogDirectory ?? "Logs are not initialized.";
        }
    }

    private async void OnExportDiagnostics(object? sender, RoutedEventArgs e)
    {
        ExportDiagnosticsButton.IsEnabled = false;
        try
        {
            if (Diagnostics is null)
            {
                throw new InvalidOperationException("Diagnostic export is not initialized.");
            }
            var storage = TopLevel.GetTopLevel(this)?.StorageProvider
                ?? throw new InvalidOperationException("No window to choose a destination from.");
            var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export safe diagnostics", SuggestedFileName = $"tropicast-diagnostics-{DateTime.UtcNow:yyyyMMdd-HHmmss}.zip",
                DefaultExtension = "zip",
                FileTypeChoices = [new FilePickerFileType("ZIP diagnostics") { Patterns = ["*.zip"] }],
            });
            if (file is null)
            {
                DiagnosticStatus.Text = "Export cancelled.";
                return;
            }
            using (file)
            {
                // Assemble before opening the destination so corrupt logs do not produce a partial bundle.
                using var bundle = new MemoryStream();
                await Diagnostics.ExportAsync(bundle);
                bundle.Position = 0;
                await using var output = await file.OpenWriteAsync();
                output.SetLength(0);
                await bundle.CopyToAsync(output);
            }
            DiagnosticStatus.Text = Diagnostics.HasLogFailure
                ? "Diagnostics exported, but logging has failed. Check the log directory permissions and free space."
                : "Diagnostics exported. No credentials or profile/device names are included.";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException)
        {
            Diagnostics?.ReportExportFailure(error);
            DiagnosticStatus.Text = "Could not export diagnostics. Check the destination and log directory permissions and free space.";
        }
        finally
        {
            ExportDiagnosticsButton.IsEnabled = true;
        }
    }
}
