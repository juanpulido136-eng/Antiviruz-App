using System;
using System.IO;
using System.Text;
using AntivirusApp.Services;
using Microsoft.UI.Xaml;

namespace AntivirusApp;

public partial class App : Application
{
    public static Window CurrentWindow { get; private set; } = null!;

    public App()
    {
        InitializeComponent();

        UnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Log("AppDomain.UnhandledException", e.ExceptionObject as Exception);

        Log("App creada");
    }

    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        try
        {
            Log("OnLaunched: registrando notificaciones");
            NotificationService.Register();

            Log("OnLaunched: creando MainWindow");
            CurrentWindow = new MainWindow();

            Log("OnLaunched: activando ventana");
            CurrentWindow.Activate();

            Log("OnLaunched: terminado");
        }
        catch (Exception ex)
        {
            Log("OnLaunched FALLÓ", ex);
            Fail("No se pudo iniciar la aplicación", ex);
        }
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        Log("UnhandledException", e.Exception);
        e.Handled = true;
        Fail("Error no controlado", e.Exception);
    }

    internal static string LogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Aegis",
        "app.log");

    internal static void Log(string step, Exception? ex = null)
    {
        try
        {
            string folder = Path.GetDirectoryName(LogPath)!;
            Directory.CreateDirectory(folder);

            var sb = new StringBuilder();
            sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")).Append("  ").Append(step);

            if (ex is not null)
            {
                sb.AppendLine();
                sb.Append("   ").Append(ex.GetType().FullName).Append(": ").Append(ex.Message);
                sb.AppendLine();
                sb.Append(ex.StackTrace);
            }

            File.AppendAllText(LogPath, sb.ToString() + Environment.NewLine, Encoding.UTF8);
        }
        catch (Exception)
        {

        }
    }

    private static void Fail(string title, Exception? ex)
    {
        string text = ex is null
            ? "Error desconocido"
            : $"{ex.GetType().Name}: {ex.Message}\n\n{ex.StackTrace}";

        Log("Fail: " + title);
        MessageBox(IntPtr.Zero, $"{title}\n\n{text}\n\nLog: {LogPath}", "Aegis", 0x10);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);
}
