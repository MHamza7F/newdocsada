using scada_demo_test.Maui.ViewModels;

namespace scada_demo_test.Maui.Views;

public partial class LogsPage : ContentPage
{
    public LogsPage(LogsViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
