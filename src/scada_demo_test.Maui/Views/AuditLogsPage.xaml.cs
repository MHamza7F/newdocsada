using scada_demo_test.Maui.ViewModels;

namespace scada_demo_test.Maui.Views;

public partial class AuditLogsPage : ContentPage
{
    private readonly AuditLogsViewModel _viewModel;

    public AuditLogsPage(AuditLogsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadLogsCommand.ExecuteAsync(null);
    }
}
