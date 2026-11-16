using System;
using AntivirusApp.Views;
using Microsoft.UI.Xaml;

namespace AntivirusApp;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        Title = $"Aegis · Endpoint Suite · v22.0 · motor {AntivirusApp.Engine.HeuristicScanner.Version}";

        try
        {
            AppWindow.Resize(new Windows.Graphics.SizeInt32(1360, 880));
        }
        catch (Exception ex)
        {
            App.Log("Resize falló: " + ex.Message);
        }

        RootFrame.Loaded += RootFrame_Loaded;
    }

    private void RootFrame_Loaded(object sender, RoutedEventArgs e)
    {
        RootFrame.Loaded -= RootFrame_Loaded;

        try
        {
            App.Log("MainWindow: navegando a LoginPage");
            RootFrame.Navigate(typeof(LoginPage));
            App.Log("MainWindow: LoginPage cargada");
        }
        catch (Exception ex)
        {
            App.Log("MainWindow: fallo al navegar a LoginPage", ex);
            throw;
        }
    }
}
