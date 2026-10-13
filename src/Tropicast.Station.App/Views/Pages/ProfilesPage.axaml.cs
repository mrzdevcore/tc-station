using Avalonia.Controls;
using Tropicast.Station.App.ViewModels;

namespace Tropicast.Station.App.Views.Pages;

public partial class ProfilesPage : UserControl
{
    public ProfilesPage() => InitializeComponent();

    // Set here, not with a DataContext binding in XAML: a hidden page is not templated yet, and such a
    // binding inside it stays unresolved until the page first shows, leaving its rows unbound (search, tests).
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        Root.DataContext = DataContext is MainViewModel model ? model.Profiles : null;
    }
}
