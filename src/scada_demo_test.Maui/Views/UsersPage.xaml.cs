using scada_demo_test.Maui.ViewModels;

namespace scada_demo_test.Maui.Views;

public partial class UsersPage : ContentPage
{
    private readonly UsersViewModel _viewModel;

    public UsersPage(UsersViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadUsersCommand.ExecuteAsync(null);
    }
}
