using System;
using System.IO;
using System.Security.Principal;
using System.Text;
using AntivirusApp.Models;
using AntivirusApp.Views;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace AntivirusApp.Services;

public sealed record AlertMessage(string Glyph, string Kind, string Title, string Body, string AccentKey, ScanItem? Item);

public static class NotificationService
{
    private static bool _initialized;

    public static bool ToastAvailable { get; private set; }

    public static string Status { get; private set; } = "sin registrar";

    public static bool Elevated { get; private set; }

    public static void Register()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;

        Elevated = IsElevated();

        try
        {
            AppNotificationManager.Default.NotificationInvoked += OnNotificationInvoked;
            AppNotificationManager.Default.Register();
            ToastAvailable = true;
            Status = "toasts registrados";
        }
        catch (Exception first)
        {
            try
            {
                AppNotificationManager.Default.Register("Aegis Endpoint Suite", ResolveIcon());
                ToastAvailable = true;
                Status = "toasts registrados (registro manual)";
            }
            catch (Exception second)
            {
                ToastAvailable = false;
                Status = "toasts no disponibles · " + first.Message + " · " + second.Message;
            }
        }

        if (Elevated)
        {
            Status += " · app elevada: Windows no muestra toasts a procesos con permisos de administrador";
        }

        Log("NotificationService.Register: " + Status);
    }

    public static void Unregister()
    {
        try
        {
            AppNotificationManager.Default.NotificationInvoked -= OnNotificationInvoked;
            AppNotificationManager.Default.Unregister();
        }
        catch (Exception ex)
        {
            Log("NotificationService.Unregister: " + ex.Message);
        }
    }

    public static void Alert(ScanItem item)
    {
        bool malicious = item.Verdict == Verdicts.Malicious;
        Notify(new AlertMessage(
            Palette.GlyphFor(item.Verdict),
            malicious ? "Amenaza probable" : "Archivo sospechoso",
            item.Name,
            $"Riesgo {item.Result.RiskScore} de 100. {item.Origin}",
            Palette.KeyFor(item.Verdict),
            item));
    }

    public static void Summary(string glyph, string kind, string title, string body, string accentKey) =>
        Notify(new AlertMessage(glyph, kind, title, body, accentKey, null));

    public static void Notify(AlertMessage message)
    {
        ShowToast(message);

        try
        {
            AlertPopup.Show(message.Glyph, message.Kind, message.Title, message.Body, message.AccentKey);
        }
        catch (Exception ex)
        {
            Log("Aviso interno no mostrado: " + ex.Message);
        }
    }

    private static void ShowToast(AlertMessage message)
    {
        if (!ToastAvailable)
        {
            if (!Elevated)
            {
                TryRegisterNow();
            }

            if (!ToastAvailable)
            {
                return;
            }
        }

        try
        {
            var toast = new AppNotificationBuilder()
                .AddArgument("action", "open")
                .AddText(message.Title)
                .AddText(message.Body)
                .BuildNotification();

            AppNotificationManager.Default.Show(toast);
        }
        catch (Exception ex)
        {
            ToastAvailable = false;
            Status = "fallo mostrando el toast · " + ex.Message;
            Log(Status);
        }
    }

    private static void TryRegisterNow()
    {
        try
        {
            AppNotificationManager.Default.Register();
            ToastAvailable = true;
            Status = "toasts registrados al enviar";
            Log(Status);
        }
        catch (Exception ex)
        {
            Status = "reintento de registro falló · " + ex.Message;
        }
    }

    private static void OnNotificationInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args)
    {
        try
        {
            App.CurrentWindow?.DispatcherQueue.TryEnqueue(() =>
            {
                if (App.CurrentWindow is null)
                {
                    return;
                }

                App.CurrentWindow.Activate();
                App.CurrentWindow.AppWindow?.Show();
            });
        }
        catch (Exception ex)
        {
            Log("NotificationInvoked: " + ex.Message);
        }
    }

    private static Uri ResolveIcon()
    {
        string[] candidates =
        {
            "Square44x44Logo.png",
            "Square44x44Logo.scale-200.png",
            "Square44x44Logo.targetsize-24_altform-unplated.png",
            "StoreLogo.png"
        };

        foreach (string candidate in candidates)
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Assets", candidate);

            if (File.Exists(path))
            {
                return new Uri(path);
            }
        }

        return new Uri(Path.Combine(AppContext.BaseDirectory, "AntivirusApp.exe"));
    }

    private static bool IsElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static void Log(string message)
    {
        try
        {
            App.Log("NotificationService: " + message);
        }
        catch (Exception)
        {

        }
    }
}
