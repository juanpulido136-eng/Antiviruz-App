using System;
using AntivirusApp.Services;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.UI;

namespace AntivirusApp.Views;

public sealed class AlertWindow : Window
{
    private const int WidthPx = 396;
    private const int HeightPx = 142;

    private static AlertWindow? _current;

    private readonly DispatcherTimer _timer;
    private Border? _card;

    private AlertWindow()
    {
        Title = "Aegis";

        TrySetPresenter();
        Resize();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            Close();
        };

        Closed += (_, _) =>
        {
            _timer.Stop();
            if (ReferenceEquals(_current, this)) _current = null;
        };
    }

    public static void ShowOrUpdate(string glyph, string kind, string title, string body, Color accent)
    {
        _current ??= new AlertWindow();
        _current.Apply(glyph, kind, title, body, accent);
    }

    private void Apply(string glyph, string kind, string title, string body, Color accent)
    {
        _card = BuildCard(glyph, kind, title, body, accent);
        Content = _card;

        _card.PointerEntered += (_, _) => _timer.Stop();
        _card.PointerExited += (_, _) => _timer.Start();
        _card.Tapped += (_, _) => Close();

        FadeIn(_card);

        _timer.Stop();
        _timer.Start();

        Relayout();
        Activate();
    }

    private void TrySetPresenter()
    {
        try
        {

            var presenter = OverlappedPresenter.Create();
            presenter.IsAlwaysOnTop = true;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsResizable = false;
            presenter.SetBorderAndTitleBar(true, false);

            AppWindow.SetPresenter(presenter);
            AppWindow.IsShownInSwitchers = false;
        }
        catch (Exception ex)
        {
            App.Log("AlertWindow.TrySetPresenter FALLÓ", ex);
        }
    }

    private void Resize()
    {
        try
        {
            AppWindow.Resize(new SizeInt32(WidthPx, HeightPx));
        }
        catch (Exception)
        {

        }
    }

    private void Relayout()
    {
        try
        {
            var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;

            int x = area.X + area.Width - WidthPx - 18;
            int y = area.Y + area.Height - HeightPx - 18;

            AppWindow.Move(new PointInt32(x, y));
        }
        catch (Exception)
        {

        }
    }

    private static void FadeIn(UIElement element)
    {
        try
        {
            element.Opacity = 0;

            var storyboard = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
            var fade = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = new Duration(TimeSpan.FromMilliseconds(200)),
                EasingFunction = new Microsoft.UI.Xaml.Media.Animation.CubicEase
                {
                    EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut
                }
            };

            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(fade, element);
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(fade, "Opacity");
            storyboard.Children.Add(fade);
            storyboard.Begin();
        }
        catch (Exception)
        {
            element.Opacity = 1;
        }
    }

    private static Border BuildCard(string glyph, string kind, string title, string body, Color accent)
    {
        var accentBrush = new SolidColorBrush(accent);

        var bar = new Border
        {
            Width = 4,
            CornerRadius = new CornerRadius(2),
            Background = accentBrush,
            Margin = new Thickness(0, 8, 0, 8)
        };

        var icon = new Border
        {
            Width = 36,
            Height = 36,
            CornerRadius = new CornerRadius(18),
            Background = new SolidColorBrush(Color.FromArgb(32, accent.R, accent.G, accent.B)),
            VerticalAlignment = VerticalAlignment.Top,
            Child = new FontIcon { Glyph = glyph, FontSize = 15, Foreground = accentBrush }
        };

        var texts = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(new TextBlock
        {
            Text = kind.ToUpperInvariant(),
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = accentBrush
        });
        texts.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 20, 25, 26)),
            TextWrapping = TextWrapping.Wrap,
            MaxLines = 2,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        texts.Children.Add(new TextBlock
        {
            Text = body,
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromArgb(255, 58, 70, 68)),
            TextWrapping = TextWrapping.Wrap,
            MaxLines = 3,
            TextTrimming = TextTrimming.CharacterEllipsis
        });

        var middle = new Grid { ColumnSpacing = 12 };
        middle.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        middle.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        middle.Children.Add(icon);
        middle.Children.Add(texts);
        Grid.SetColumn(icon, 0);
        Grid.SetColumn(texts, 1);

        var root = new Grid { ColumnSpacing = 10 };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(bar);
        root.Children.Add(middle);
        Grid.SetColumn(middle, 1);

        return new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(255, 252, 253, 253)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 206, 216, 212)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 10, 14, 10),
            Child = root
        };
    }
}

public static class AlertPopup
{
    public static void Show(string glyph, string kind, string title, string body, string accentKey)
    {
        var dispatcher = App.CurrentWindow?.DispatcherQueue;
        if (dispatcher is null) return;

        var color = Resolve(accentKey);

        dispatcher.TryEnqueue(() =>
        {
            try
            {
                AlertWindow.ShowOrUpdate(glyph, kind, title, body, color);
            }
            catch (Exception ex)
            {
                App.Log("AlertPopup.Show FALLÓ", ex);
            }
        });
    }

    private static Color Resolve(string key)
    {
        try
        {
            if (Application.Current.Resources[key] is SolidColorBrush brush) return brush.Color;
        }
        catch (Exception)
        {

        }

        return Color.FromArgb(255, 11, 107, 104);
    }
}
