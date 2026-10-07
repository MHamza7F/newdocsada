using scada_demo_test.Maui.ViewModels;

namespace scada_demo_test.Maui.Views;

public partial class DevicesPage : ContentPage
{
    private readonly DevicesViewModel _viewModel;

    public DevicesPage(DevicesViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadDevicesCommand.ExecuteAsync(null);
    }
}
