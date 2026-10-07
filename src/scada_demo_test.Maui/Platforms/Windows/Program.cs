using System;
using Microsoft.UI.Xaml;
using WinRT;
using Microsoft.Maui;
using Microsoft.Maui.Hosting;

namespace scada_demo_test.Maui.Platforms.Windows
{
    public class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            ComWrappersSupport.InitializeComWrappers();
            global::Microsoft.UI.Xaml.Application.Start(_ => new WinUIApp());
        }
    }

    class WinUIApp : MauiWinUIApplication
    {
        protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
    }
}