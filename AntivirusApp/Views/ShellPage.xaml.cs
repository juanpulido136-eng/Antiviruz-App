using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using AntivirusApp.Models;
using AntivirusApp.Services;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;

namespace AntivirusApp.Views;

public sealed partial class ShellPage : Page
{
    private ScanItem? _detail;

    public static ShellPage? Instance { get; private set; }

    public ShellPage()
    {
        InitializeComponent();
        Instance = this;

        ProfileStore.Changed += () => DispatcherQueue.TryEnqueue(RefreshAvatar);

        NavScan.Click += (_, _) => NavScan_Click();
        NavCleaner.Click += (_, _) => NavCleaner_Click();
        NavSpace.Click += (_, _) => NavSpace_Click();
        NavLive.Click += (_, _) => NavLive_Click();
        NavAccount.Click += (_, _) => NavAccount_Click();
        NavHelp.Click += (_, _) => NavHelp_Click();
        HelpButton.Click += (_, _) => NavHelp_Click();

        HeaderAvatar.Tapped += (_, _) => NavAccount_Click();

        ContentFrame.NavigationFailed += ContentFrame_NavigationFailed;
        Loaded += ShellPage_Loaded;
    }

    private void ShellPage_Loaded(object sender, RoutedEventArgs e)
    {
        RefreshAvatar();
        Helpers.Anim.Pop(AvatarRing);
        Helpers.Anim.HoverScale(AvatarRing, 1.10);
        Helpers.Anim.Pop(LogoBox);

        if (ContentFrame.Content is null)
            Navigate(typeof(ScanPage), "Análisis manual", "Analiza archivos, carpetas o ZIP completos", NavScan);
    }

    private void RefreshAvatar()
    {
        try
        {
            var profile = ProfileStore.Load();

            string name = string.IsNullOrWhiteSpace(profile.DisplayName) ? AuthService.CurrentUser : profile.DisplayName;
            HeaderUserName.Text = name;

            if (!string.IsNullOrWhiteSpace(profile.AvatarPath) && File.Exists(profile.AvatarPath))
            {
                HeaderAvatar.ProfilePicture = new BitmapImage(new Uri(profile.AvatarPath));
                HeaderAvatar.Initials = string.Empty;
            }
            else
            {
                HeaderAvatar.ProfilePicture = null;
                HeaderAvatar.Initials = ProfileStore.Initials(name);
            }
        }
        catch (Exception ex)
        {
            App.Log("ShellPage.RefreshAvatar FALLÓ", ex);
        }
    }

    private void NavScan_Click() =>
        Navigate(typeof(ScanPage), "Análisis manual", "Analiza archivos, carpetas o ZIP completos", NavScan);

    private void NavCleaner_Click() =>
        Navigate(typeof(CleanerPage), "Aegiss_Clenaer", "Escanea el equipo completo o una carpeta y elimina lo que ya no uses", NavCleaner);

    private void NavSpace_Click() =>
        Navigate(typeof(SpacePage), "Aegiss_TheClurer", "Aplicaciones del sistema en mapa de cuadrados y compresión ZIP", NavSpace);

    private void NavLive_Click() =>
        Navigate(typeof(LivePage), "Vigilancia en vivo", "Revisa automáticamente cada archivo nuevo de Descargas", NavLive);

    private void NavHelp_Click() =>
        Navigate(typeof(HelpPage), "Ayuda", "Qué hace cada módulo y cómo usarlo", NavHelp);

    private void NavAccount_Click() =>
        Navigate(typeof(AccountPage), "Cuenta", "Foto, nombre y descripción de esta cuenta", NavAccount);

    private void Navigate(Type page, string title, string subtitle, Button active)
    {
        if (ContentFrame.CurrentSourcePageType != page) ContentFrame.Navigate(page);

        TitleText.Text = title;
        SubText.Text = subtitle;

        Mark(NavScan, active == NavScan);
        Mark(NavCleaner, active == NavCleaner);
        Mark(NavSpace, active == NavSpace);
        Mark(NavLive, active == NavLive);
        Mark(NavAccount, active == NavAccount);
        Mark(NavHelp, active == NavHelp);
    }

    private static void Mark(Button button, bool on)
    {
        button.Background = on ? Palette.Of("RaisedBrush") : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        button.Foreground = on ? Palette.Of("AccentBrush") : Palette.Of("MutedBrush");
    }

    public void OpenDetail(ScanItem item)
    {
        _detail = item;
        DrawerGlyph.Glyph = item.VerdictGlyph;
        DrawerGlyph.Foreground = item.Accent;
        DrawerState.Text = item.Result.IsError
            ? item.VerdictLabel + " · no se pudo leer completo"
            : $"{item.VerdictLabel}, riesgo {item.Result.RiskScore} de 100";
        DrawerState.Foreground = item.Accent;
        DrawerName.Text = item.Name;
        DrOrigin.Text = item.Origin;
        DrType.Text = item.TypeText;
        DrSize.Text = item.SizeText;
        DrTime.Text = item.When.ToString("dd/MM/yyyy HH:mm:ss");
        DrSha.Text = string.IsNullOrWhiteSpace(item.Result.Sha256)
            ? "No disponible"
            : item.Result.Sha256;
        ReasonsList.ItemsSource = item.Reasons;
        DeleteButton.IsEnabled = item.CanDelete;
        ShowButton.IsEnabled = true;
        Scrim.Visibility = Visibility.Visible;
        Drawer.Visibility = Visibility.Visible;
    }

    private void CloseDetail()
    {
        _detail = null;
        Scrim.Visibility = Visibility.Collapsed;
        Drawer.Visibility = Visibility.Collapsed;
    }

    private void CloseDetail_Click(object sender, RoutedEventArgs e) => CloseDetail();

    private void Scrim_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e) => CloseDetail();

    private void ShowFolder_Click(object sender, RoutedEventArgs e) => _detail?.ShowInFolder();

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_detail is null) return;
        var item = _detail;

        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(new TextBlock
        {
            Text = "Esta acción borra el archivo del disco y no se puede deshacer desde la aplicación.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Palette.Of("MutedBrush")
        });
        content.Children.Add(new Border
        {
            Background = Palette.Of("RaisedBrush"),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12),
            Child = new TextBlock
            {
                Text = item.FilePath,
                TextWrapping = TextWrapping.Wrap,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12
            }
        });

        var dialog = new ContentDialog
        {
            Title = "Eliminar archivo",
            Content = content,
            PrimaryButtonText = "Eliminar",
            CloseButtonText = "Cancelar",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        try
        {
            item.Remove();
            CloseDetail();
            NotificationService.Summary("\uE73E", "Eliminado", "Archivo eliminado", item.Name, "OkBrush");
        }
        catch (Exception ex)
        {
            NotificationService.Summary("\uE783", "Error", "No se pudo eliminar el archivo", ex.Message, "DangerBrush");
        }
    }

    private async void ContentFrame_NavigationFailed(object sender, NavigationFailedEventArgs e)
    {
        e.Handled = true;

        string page = e.SourcePageType?.Name ?? "la página";
        App.Log("NavigationFailed -> " + page, e.Exception);

        string detail = Describe(e.Exception);

        try
        {
            var dialog = new ContentDialog
            {
                Title = "No se pudo abrir " + page,
                Content = new ScrollViewer
                {
                    MaxHeight = 420,
                    Content = new TextBlock
                    {
                        Text = detail,
                        TextWrapping = TextWrapping.Wrap,
                        FontFamily = new FontFamily("Consolas"),
                        FontSize = 12,
                        IsTextSelectionEnabled = true
                    }
                },
                CloseButtonText = "Cerrar",
                XamlRoot = XamlRoot
            };

            await dialog.ShowAsync();
        }
        catch (Exception)
        {
            NotificationService.Summary("\uE783", "Error", "No se pudo abrir " + page, detail, "DangerBrush");
        }
    }

    private static string Describe(Exception? ex)
    {
        var sb = new StringBuilder();
        int depth = 0;

        while (ex is not null && depth < 6)
        {
            if (depth > 0) sb.AppendLine("Excepción interna:");
            sb.AppendLine(ex.GetType().FullName + ": " + ex.Message);

            string? frame = (ex.StackTrace ?? string.Empty)
                .Split('\n')
                .Select(l => l.Trim())
                .FirstOrDefault(l => l.Contains("AntivirusApp"));

            if (!string.IsNullOrEmpty(frame)) sb.AppendLine("   " + frame);

            ex = ex.InnerException;
            depth++;
        }

        sb.AppendLine();
        sb.AppendLine(@"Log completo: %LocalAppData%\Aegis\app.log");
        return sb.ToString();
    }
}
