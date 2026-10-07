using scada_demo_test.Maui.ViewModels;

namespace scada_demo_test.Maui.Views;

public partial class AlertsPage : ContentPage
{
    private readonly AlertsViewModel _viewModel;

    public AlertsPage(AlertsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadAlertsCommand.ExecuteAsync(null);
    }
}
