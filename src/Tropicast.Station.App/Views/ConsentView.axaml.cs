using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Tropicast.Station.App.ViewModels;

namespace Tropicast.Station.App.Views;

public partial class ConsentView : UserControl
{
    public ConsentView()
    {
        InitializeComponent();
        // Switching between the summary and the licenses hides the focused link: focus goes to what replaces it.
        ConsentLicenses.PropertyChanged += (_, e) =>
        {
            if (e.Property == IsVisibleProperty && IsVisible)
            {
                Control target = ConsentLicenses.IsVisible ? ConsentLicensesBack : ConsentLicensesLink;
                Dispatcher.UIThread.Post(() => target.Focus(NavigationMethod.Tab), DispatcherPriority.Loaded);
            }
        };
    }

    // Set here, as on the pages: a binding inside a control hidden at start stays unresolved until it first shows.
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        Root.DataContext = DataContext is MainViewModel model ? model.Consent : null;
    }

    /// <summary>Screen readers start at the heading; Tab then goes through the screen only (the window behind is disabled).</summary>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty && change.GetNewValue<bool>())
        {
            Dispatcher.UIThread.Post(() => ConsentHeading.Focus(), DispatcherPriority.Loaded);
        }
    }
}
