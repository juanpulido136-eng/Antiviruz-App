using System;
using System.Collections.Generic;
using System.Linq;
using AntivirusApp.Helpers;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;

namespace AntivirusApp.Controls;

public sealed class ChartSlice
{
    public string Label = string.Empty;
    public long Value;
    public Color Color = Color.FromArgb(255, 255, 255, 255);
    public string ValueText = string.Empty;
}

public static class Charts
{

    public static readonly Color[] Palette =
    {
        Color.FromArgb(255, 79, 140, 255),
        Color.FromArgb(255, 47, 208, 140),
        Color.FromArgb(255, 242, 179, 61),
        Color.FromArgb(255, 255, 77, 98),
        Color.FromArgb(255, 53, 195, 232),
        Color.FromArgb(255, 167, 139, 250),
        Color.FromArgb(255, 244, 114, 182),
        Color.FromArgb(255, 45, 212, 191),
        Color.FromArgb(255, 148, 163, 184)
    };

    public static Color ColorAt(int index) => Palette[Math.Abs(index) % Palette.Length];

    public static Brush BrushOf(Color color) => new SolidColorBrush(color);

    private static Brush R(string key) => (Brush)Application.Current.Resources[key];

    public static void Bars(Panel host, IReadOnlyList<ChartSlice> slices, bool compact = false, bool animate = false)
    {
        host.Children.Clear();

        if (slices.Count == 0)
        {
            host.Children.Add(Empty());
            return;
        }

        long top = slices.Max(s => s.Value);
        if (top <= 0) top = 1;

        double labelWidth = compact ? 118 : 158;
        int index = 0;

        foreach (var slice in slices)
        {
            double fraction = slice.Value / (double)top;
            if (fraction < 0.02) fraction = 0.02;
            if (fraction > 1) fraction = 1;

            var row = new Grid
            {
                ColumnSpacing = 12,
                Margin = new Thickness(0, 0, 0, compact ? 8 : 10)
            };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(labelWidth) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(compact ? 66 : 84) });

            row.Children.Add(new TextBlock
            {
                Text = slice.Label,
                FontSize = 12,
                Foreground = R("MutedBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            });

            var track = new Border
            {
                Height = compact ? 8 : 11,
                CornerRadius = new CornerRadius(compact ? 4 : 6),
                Background = R("RaisedBrush"),
                VerticalAlignment = VerticalAlignment.Center
            };

            var inner = new Grid();
            inner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(fraction, GridUnitType.Star) });
            inner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - fraction, GridUnitType.Star) });
            var bar = new Border
            {
                Background = BrushOf(slice.Color),
                CornerRadius = new CornerRadius(compact ? 4 : 6)
            };

            ScaleTransform? growth = null;

            if (animate)
            {
                growth = new ScaleTransform { ScaleX = 0, ScaleY = 1 };
                bar.RenderTransformOrigin = new Point(0, 0.5);
                bar.RenderTransform = growth;
            }

            inner.Children.Add(bar);
            track.Child = inner;

            var value = new TextBlock
            {
                Text = slice.ValueText,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right,
                TextTrimming = TextTrimming.CharacterEllipsis
            };

            Grid.SetColumn(track, 1);
            Grid.SetColumn(value, 2);
            row.Children.Add(track);
            row.Children.Add(value);

            int delay = 30 + index * 45;
            host.Children.Add(row);

            if (growth is not null)
            {
                var target = growth;

                if (host.IsLoaded)
                {
                    Anim.GrowBar(target, delay);
                }
                else
                {
                    host.Loaded += (_, _) => Anim.GrowBar(target, delay);
                }
            }

            index++;
        }
    }

    public static void Donut(Canvas host, IReadOnlyList<ChartSlice> slices, double size, string centerTop, string centerBottom)
    {
        host.Children.Clear();
        host.Width = size;
        host.Height = size;

        long total = slices.Sum(s => s.Value);
        double cx = size / 2.0;
        double cy = size / 2.0;
        double outer = size / 2.0 - 2;
        double innerRadius = outer * 0.63;

        if (total <= 0)
        {
            host.Children.Add(new Ellipse
            {
                Width = outer * 2,
                Height = outer * 2,
                Stroke = R("RaisedBrush"),
                StrokeThickness = outer - innerRadius
            });
            Canvas.SetLeft(host.Children[0], cx - outer);
            Canvas.SetTop(host.Children[0], cy - outer);
            AddCenter(host, size, centerTop, centerBottom);
            return;
        }

        const double start = -Math.PI / 2;
        const double full = Math.PI * 2;
        const double gap = 0.018;
        double acc = 0;

        foreach (var slice in slices)
        {
            if (slice.Value <= 0) continue;

            double sweep = slice.Value / (double)total * full;

            if (sweep >= full - 0.001)
            {
                var ring = new Ellipse
                {
                    Width = outer * 2,
                    Height = outer * 2,
                    Stroke = BrushOf(slice.Color),
                    StrokeThickness = outer - innerRadius
                };
                Canvas.SetLeft(ring, cx - outer);
                Canvas.SetTop(ring, cy - outer);
                host.Children.Add(ring);
                acc += sweep;
                continue;
            }

            double a0 = start + acc + gap;
            double a1 = start + acc + sweep - gap;
            if (a1 <= a0) a1 = a0 + 0.02;

            bool large = a1 - a0 > Math.PI;

            Point o0 = OnCircle(cx, cy, outer, a0);
            Point o1 = OnCircle(cx, cy, outer, a1);
            Point i1 = OnCircle(cx, cy, innerRadius, a1);
            Point i0 = OnCircle(cx, cy, innerRadius, a0);

            var figure = new PathFigure
            {
                StartPoint = o0,
                IsClosed = true,
                IsFilled = true
            };
            figure.Segments.Add(new ArcSegment
            {
                Point = o1,
                Size = new Size(outer, outer),
                IsLargeArc = large,
                SweepDirection = SweepDirection.Clockwise
            });
            figure.Segments.Add(new LineSegment { Point = i1 });
            figure.Segments.Add(new ArcSegment
            {
                Point = i0,
                Size = new Size(innerRadius, innerRadius),
                IsLargeArc = large,
                SweepDirection = SweepDirection.Counterclockwise
            });

            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);

            host.Children.Add(new Microsoft.UI.Xaml.Shapes.Path
            {
                Data = geometry,
                Fill = BrushOf(slice.Color)
            });

            acc += sweep;
        }

        AddCenter(host, size, centerTop, centerBottom);
    }

    private static void AddCenter(Canvas host, double size, string top, string bottom)
    {
        var stack = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 2
        };
        stack.Children.Add(new TextBlock
        {
            Text = top,
            FontSize = Math.Max(15, size * 0.115),
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center
        });
        stack.Children.Add(new TextBlock
        {
            Text = bottom,
            FontSize = 11,
            Foreground = R("MutedBrush"),
            HorizontalAlignment = HorizontalAlignment.Center
        });

        var overlay = new Grid { Width = size, Height = size };
        overlay.Children.Add(stack);
        Canvas.SetLeft(overlay, 0);
        Canvas.SetTop(overlay, 0);
        host.Children.Add(overlay);
    }

    private static Point OnCircle(double cx, double cy, double radius, double angle) =>
        new(cx + radius * Math.Cos(angle), cy + radius * Math.Sin(angle));

    public static void Legend(Panel host, IReadOnlyList<ChartSlice> slices, long total, bool compact = false)
    {
        host.Children.Clear();

        if (slices.Count == 0)
        {
            host.Children.Add(Empty());
            return;
        }

        foreach (var slice in slices)
        {
            var row = new Grid
            {
                ColumnSpacing = 8,
                Margin = new Thickness(0, 0, 0, compact ? 5 : 7)
            };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            row.Children.Add(new Border
            {
                Width = 9,
                Height = 9,
                CornerRadius = new CornerRadius(3),
                Background = BrushOf(slice.Color),
                VerticalAlignment = VerticalAlignment.Center
            });

            var label = new TextBlock
            {
                Text = slice.Label,
                FontSize = compact ? 11 : 12,
                Foreground = R("TextBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(label, 1);

            double percent = total > 0 ? slice.Value * 100.0 / total : 0;
            var value = new TextBlock
            {
                Text = $"{slice.ValueText}  ·  {percent:0.#}%",
                FontSize = compact ? 11 : 12,
                Foreground = R("MutedBrush"),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(value, 2);

            row.Children.Add(label);
            row.Children.Add(value);
            host.Children.Add(row);
        }
    }

    private static TextBlock Empty() => new()
    {
        Text = "Sin datos todavía. Analiza una unidad para ver la gráfica.",
        FontSize = 12,
        Foreground = R("MutedBrush"),
        TextWrapping = TextWrapping.Wrap
    };
}
