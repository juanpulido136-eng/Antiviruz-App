using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace AntivirusApp.Controls;

public sealed class TreemapItem
{
    public string Label = string.Empty;
    public string Sub = string.Empty;

    public string Tag = string.Empty;

    public long Value;
    public Color Color;
}

public static class Treemap
{
    public static void Draw(Canvas host, IReadOnlyList<TreemapItem> items, double width, double height)
    {
        host.Children.Clear();

        if (width < 60 || height < 60) return;

        host.Width = width;
        host.Height = height;

        var list = items.Where(i => i.Value > 0).OrderByDescending(i => i.Value).ToList();
        if (list.Count == 0) return;

        long total = list.Sum(i => i.Value);
        if (total <= 0) return;

        int rows = Math.Clamp((int)Math.Round(Math.Sqrt(list.Count)), 2, 5);

        double y = 0;
        int index = 0;
        long remaining = total;

        for (int r = 0; r < rows && index < list.Count; r++)
        {
            int rowsLeft = rows - r;
            long rowTarget = (long)Math.Ceiling(remaining / (double)rowsLeft);

            var rowItems = new List<TreemapItem>();
            long rowValue = 0;

            while (index < list.Count && (rowValue < rowTarget || rowItems.Count == 0))
            {
                rowItems.Add(list[index]);
                rowValue += list[index].Value;
                index++;
            }

            if (rowValue <= 0) break;

            bool last = r == rows - 1 || index >= list.Count;
            double rowHeight = last ? height - y : height * rowValue / total;
            if (rowHeight < 6) rowHeight = 6;

            double x = 0;
            for (int i = 0; i < rowItems.Count; i++)
            {
                double w = i == rowItems.Count - 1
                    ? width - x
                    : width * rowItems[i].Value / (double)rowValue;

                AddTile(host, rowItems[i], x + 2, y + 2, Math.Max(3, w - 4), Math.Max(3, rowHeight - 4));
                x += w;
            }

            y += rowHeight;
            remaining -= rowValue;
            if (remaining <= 0) break;
        }
    }

    private static void AddTile(Canvas host, TreemapItem item, double x, double y, double width, double height)
    {
        var tile = new Border
        {
            Width = width,
            Height = height,
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(item.Color),
            BorderBrush = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)),
            BorderThickness = new Thickness(1)
        };

        if (width > 70 && height > 30)
        {
            var stack = new StackPanel { Spacing = 1, Margin = new Thickness(8, 5, 8, 5) };

            if (!string.IsNullOrEmpty(item.Tag) && width > 96)
            {
                stack.Children.Add(new TextBlock
                {
                    Text = item.Tag,
                    FontSize = 9,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromArgb(205, 255, 255, 255)),
                    CharacterSpacing = 60
                });
            }

            stack.Children.Add(new TextBlock
            {
                Text = item.Label,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromArgb(245, 255, 255, 255)),
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextWrapping = TextWrapping.NoWrap
            });

            if (height > 44)
            {
                stack.Children.Add(new TextBlock
                {
                    Text = item.Sub,
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromArgb(210, 255, 255, 255)),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    TextWrapping = TextWrapping.NoWrap
                });
            }

            tile.Child = stack;
        }

        Canvas.SetLeft(tile, x);
        Canvas.SetTop(tile, y);
        host.Children.Add(tile);
    }
}
