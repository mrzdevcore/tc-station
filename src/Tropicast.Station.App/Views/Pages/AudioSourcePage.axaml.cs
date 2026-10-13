using Avalonia.Controls;
using Tropicast.Station.App.ViewModels;

namespace Tropicast.Station.App.Views.Pages;

public partial class AudioSourcePage : UserControl
{
    public AudioSourcePage() => InitializeComponent();

    // Set here, not with a DataContext binding in XAML: a hidden page is not templated yet, and such a
    // binding inside it stays unresolved until the page first shows, leaving its rows unbound (search, tests).
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        AudioDevices.DataContext = DataContext is MainViewModel model ? model.Audio : null;
    }
}
