using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace AntivirusApp.Helpers;

public static class Anim
{
    public static void FadeIn(FrameworkElement element, int delayMs = 0, double from = 0.0)
    {
        try
        {
            element.Opacity = from;

            var storyboard = new Storyboard();
            var fade = new DoubleAnimation
            {
                From = from,
                To = 1,
                Duration = new Duration(TimeSpan.FromMilliseconds(280)),
                BeginTime = TimeSpan.FromMilliseconds(delayMs),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            Storyboard.SetTarget(fade, element);
            Storyboard.SetTargetProperty(fade, "Opacity");
            storyboard.Children.Add(fade);
            storyboard.Begin();
        }
        catch (Exception)
        {
            element.Opacity = 1;
        }
    }

    public static void Pop(FrameworkElement element)
    {
        try
        {
            var scale = new ScaleTransform { ScaleX = 0.86, ScaleY = 0.86 };
            element.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
            element.RenderTransform = scale;

            var storyboard = new Storyboard();

            foreach (var property in new[] { "ScaleX", "ScaleY" })
            {
                var animation = new DoubleAnimation
                {
                    From = 0.86,
                    To = 1,
                    Duration = new Duration(TimeSpan.FromMilliseconds(380)),
                    EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.55 }
                };

                Storyboard.SetTarget(animation, scale);
                Storyboard.SetTargetProperty(animation, property);
                storyboard.Children.Add(animation);
            }

            storyboard.Begin();
        }
        catch (Exception)
        {
            element.RenderTransform = null;
        }
    }

    public static void HoverScale(FrameworkElement element, double grow = 1.08)
    {
        try
        {
            var scale = new ScaleTransform { ScaleX = 1, ScaleY = 1 };
            element.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
            element.RenderTransform = scale;

            void To(double target)
            {
                var storyboard = new Storyboard();

                foreach (var property in new[] { "ScaleX", "ScaleY" })
                {
                    var animation = new DoubleAnimation
                    {
                        To = target,
                        Duration = new Duration(TimeSpan.FromMilliseconds(160)),
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    };

                    Storyboard.SetTarget(animation, scale);
                    Storyboard.SetTargetProperty(animation, property);
                    storyboard.Children.Add(animation);
                }

                storyboard.Begin();
            }

            element.PointerEntered += (_, _) => To(grow);
            element.PointerExited += (_, _) => To(1);
        }
        catch (Exception)
        {
            element.RenderTransform = null;
        }
    }

    public static void GrowBar(ScaleTransform scale, int delayMs = 0, int durationMs = 420)
    {
        try
        {
            var storyboard = new Storyboard();
            var grow = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = new Duration(TimeSpan.FromMilliseconds(durationMs)),
                BeginTime = TimeSpan.FromMilliseconds(delayMs),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            Storyboard.SetTarget(grow, scale);
            Storyboard.SetTargetProperty(grow, "ScaleX");
            storyboard.Children.Add(grow);
            storyboard.Begin();
        }
        catch (Exception)
        {
            scale.ScaleX = 1;
        }
    }

    public static void SlideIn(FrameworkElement element, int delayMs = 0, double offsetY = 14)
    {
        try
        {
            element.Opacity = 0;

            var translate = new TranslateTransform { Y = offsetY };
            element.RenderTransform = translate;

            var storyboard = new Storyboard();

            var fade = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = new Duration(TimeSpan.FromMilliseconds(260)),
                BeginTime = TimeSpan.FromMilliseconds(delayMs),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(fade, element);
            Storyboard.SetTargetProperty(fade, "Opacity");
            storyboard.Children.Add(fade);

            var slide = new DoubleAnimation
            {
                From = offsetY,
                To = 0,
                Duration = new Duration(TimeSpan.FromMilliseconds(320)),
                BeginTime = TimeSpan.FromMilliseconds(delayMs),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Storyboard.SetTarget(slide, translate);
            Storyboard.SetTargetProperty(slide, "Y");
            storyboard.Children.Add(slide);

            storyboard.Begin();
        }
        catch (Exception)
        {
            element.Opacity = 1;
        }
    }

    public static void Pulse(FrameworkElement element, double peak = 1.07, int durationMs = 220)
    {
        try
        {
            if (element.RenderTransform is not ScaleTransform scale)
            {
                scale = new ScaleTransform { ScaleX = 1, ScaleY = 1 };
                element.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
                element.RenderTransform = scale;
            }

            double start = double.IsNaN(scale.ScaleX) ? 1 : scale.ScaleX;

            var storyboard = new Storyboard();

            foreach (var property in new[] { "ScaleX", "ScaleY" })
            {
                var up = new DoubleAnimation
                {
                    From = start,
                    To = peak,
                    Duration = new Duration(TimeSpan.FromMilliseconds(durationMs / 2)),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                Storyboard.SetTarget(up, scale);
                Storyboard.SetTargetProperty(up, property);
                storyboard.Children.Add(up);

                var down = new DoubleAnimation
                {
                    From = peak,
                    To = 1,
                    BeginTime = TimeSpan.FromMilliseconds(durationMs / 2),
                    Duration = new Duration(TimeSpan.FromMilliseconds(durationMs / 2)),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
                };
                Storyboard.SetTarget(down, scale);
                Storyboard.SetTargetProperty(down, property);
                storyboard.Children.Add(down);
            }

            storyboard.Begin();
        }
        catch (Exception)
        {

        }
    }

    public static Storyboard Spin(RotateTransform transform, double seconds = 9, bool reverse = false)
    {
        var storyboard = new Storyboard();

        try
        {
            var spin = new DoubleAnimation
            {
                From = 0,
                To = reverse ? -360 : 360,
                Duration = new Duration(TimeSpan.FromSeconds(seconds)),
                RepeatBehavior = RepeatBehavior.Forever
            };

            Storyboard.SetTarget(spin, transform);
            Storyboard.SetTargetProperty(spin, "Angle");
            storyboard.Children.Add(spin);
            storyboard.Begin();
        }
        catch (Exception)
        {

        }

        return storyboard;
    }

    public static Storyboard Breathe(FrameworkElement element, double from = 0.35, double to = 0.9, double seconds = 2.4)
    {
        var storyboard = new Storyboard();

        try
        {
            var fade = new DoubleAnimation
            {
                From = from,
                To = to,
                Duration = new Duration(TimeSpan.FromSeconds(seconds)),
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };

            Storyboard.SetTarget(fade, element);
            Storyboard.SetTargetProperty(fade, "Opacity");
            storyboard.Children.Add(fade);
            storyboard.Begin();
        }
        catch (Exception)
        {

        }

        return storyboard;
    }

    public static Storyboard PulseLoop(FrameworkElement element, double peak = 1.05, double seconds = 1.9)
    {
        var storyboard = new Storyboard();

        try
        {
            var scale = new ScaleTransform { ScaleX = 1, ScaleY = 1 };
            element.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
            element.RenderTransform = scale;

            foreach (var property in new[] { "ScaleX", "ScaleY" })
            {
                var beat = new DoubleAnimation
                {
                    From = 1,
                    To = peak,
                    Duration = new Duration(TimeSpan.FromSeconds(seconds / 2)),
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                };

                Storyboard.SetTarget(beat, scale);
                Storyboard.SetTargetProperty(beat, property);
                storyboard.Children.Add(beat);
            }

            storyboard.Begin();
        }
        catch (Exception)
        {

        }

        return storyboard;
    }

    public static void PopIn(FrameworkElement element, int delayMs = 0)
    {
        try
        {
            element.Opacity = 0;

            var storyboard = new Storyboard();
            var fade = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = new Duration(TimeSpan.FromMilliseconds(200)),
                BeginTime = TimeSpan.FromMilliseconds(delayMs),
                EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 }
            };

            Storyboard.SetTarget(fade, element);
            Storyboard.SetTargetProperty(fade, "Opacity");
            storyboard.Children.Add(fade);
            storyboard.Begin();
        }
        catch (Exception)
        {
            element.Opacity = 1;
        }
    }
}
