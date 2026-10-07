using scada_demo_test.Maui.ViewModels;

namespace scada_demo_test.Maui.Views;

public partial class AnalyticsPage : ContentPage
{
    public AnalyticsPage(AnalyticsViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }
}
