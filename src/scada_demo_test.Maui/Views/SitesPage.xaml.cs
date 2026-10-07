using scada_demo_test.Maui.ViewModels;

namespace scada_demo_test.Maui.Views;

public partial class SitesPage : ContentPage
{
    private readonly SitesViewModel _viewModel;

    public SitesPage(SitesViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadSitesCommand.ExecuteAsync(null);
    }
}
