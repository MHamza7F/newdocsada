using scada_demo_test.Maui.ViewModels;

namespace scada_demo_test.Maui.Views;

public partial class DashboardPage : ContentPage
{
    public DashboardPage(DashboardViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
