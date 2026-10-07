using scada_demo_test.Maui.ViewModels;

namespace scada_demo_test.Maui.Views;

public partial class LoginPage : ContentPage
{
    public LoginPage(LoginViewModel vm)
    {
        MauiDiag.Info("LoginPage.ctor: enter");
        try
        {
            InitializeComponent();
            MauiDiag.Info("LoginPage.ctor: InitializeComponent done");
            BindingContext = vm;
            MauiDiag.Info("LoginPage.ctor: BindingContext set");
        }
        catch (System.Exception ex)
        {
            MauiDiag.Error("LoginPage.ctor FAILED: " + ex);
            throw;
        }
    }
}