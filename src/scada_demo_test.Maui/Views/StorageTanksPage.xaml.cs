using scada_demo_test.Maui.ViewModels;

namespace scada_demo_test.Maui.Views;

public partial class StorageTanksPage : ContentPage
{
    private readonly StorageTanksViewModel _viewModel;

    public StorageTanksPage(StorageTanksViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadTanksCommand.ExecuteAsync(null);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.Unsubscribe();
    }
}
