using System;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace AntivirusApp.Engine;

public static class Ring
{
    public static void Set(Ellipse ring, double percent)
    {
        if (ring is null)
        {
            return;
        }

        percent = Math.Clamp(percent, 0, 100);

        double width = ring.Width;
        double height = ring.Height;

        if (double.IsNaN(width) || double.IsNaN(height) || width <= 0 || height <= 0)
        {
            return;
        }

        double thickness = ring.StrokeThickness > 0 ? ring.StrokeThickness : 1;

        double a = width / 2;
        double b = height / 2;

        double perimeter = Math.PI * (3 * (a + b) - Math.Sqrt((3 * a + b) * (a + 3 * b)));
        double units = perimeter / thickness;

        double visible = units * (percent / 100.0);

        ring.StrokeDashCap = PenLineCap.Flat;
        ring.StrokeDashArray = new DoubleCollection { visible, units - visible };
        ring.StrokeDashOffset = 0;
    }

    public static void Reset(Ellipse ring) => Set(ring, 0);
}
