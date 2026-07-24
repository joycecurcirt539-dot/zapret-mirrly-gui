using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace ZapretMirrlyGUI.Services;

/// <summary>
/// High-performance hardware composition animation engine for WinUI 3 page & panel transitions.
/// Designed for 120Hz+ displays with smooth Easing functions and zero-layout-thrashing transforms.
/// </summary>
public static class AnimationHelper
{
    /// <summary>
    /// Ensures element has a valid RenderTransform setup containing TranslateTransform & ScaleTransform.
    /// </summary>
    public static void EnsureRenderTransforms(
        UIElement element,
        out TranslateTransform translateTransform,
        out ScaleTransform scaleTransform)
    {
        if (element.RenderTransformOrigin != new Windows.Foundation.Point(0.5, 0.5))
        {
            element.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
        }

        if (element.RenderTransform is TransformGroup group)
        {
            TranslateTransform? foundTranslate = null;
            ScaleTransform? foundScale = null;

            foreach (var t in group.Children)
            {
                if (t is TranslateTransform tt) foundTranslate = tt;
                if (t is ScaleTransform st) foundScale = st;
            }

            if (foundTranslate == null)
            {
                foundTranslate = new TranslateTransform();
                group.Children.Add(foundTranslate);
            }

            if (foundScale == null)
            {
                foundScale = new ScaleTransform { ScaleX = 1.0, ScaleY = 1.0 };
                group.Children.Add(foundScale);
            }

            translateTransform = foundTranslate;
            scaleTransform = foundScale;
            return;
        }

        if (element.RenderTransform is TranslateTransform singleTranslate)
        {
            var newGroup = new TransformGroup();
            var newScale = new ScaleTransform { ScaleX = 1.0, ScaleY = 1.0 };
            newGroup.Children.Add(singleTranslate);
            newGroup.Children.Add(newScale);
            element.RenderTransform = newGroup;

            translateTransform = singleTranslate;
            scaleTransform = newScale;
            return;
        }

        var newTransformGroup = new TransformGroup();
        translateTransform = new TranslateTransform();
        scaleTransform = new ScaleTransform { ScaleX = 1.0, ScaleY = 1.0 };
        newTransformGroup.Children.Add(translateTransform);
        newTransformGroup.Children.Add(scaleTransform);

        element.RenderTransform = newTransformGroup;
    }

    /// <summary>
    /// Animates an element with entrance slide, scale, and fade.
    /// </summary>
    public static void AnimateElementEntrance(
        UIElement element,
        double offsetX, double offsetY,
        double scaleFrom = 0.98,
        int durationMs = 240,
        int delayMs = 0,
        EasingFunctionBase? easing = null)
    {
        EnsureRenderTransforms(element, out var translate, out var scale);

        translate.X = offsetX;
        translate.Y = offsetY;
        scale.ScaleX = scaleFrom;
        scale.ScaleY = scaleFrom;
        element.Opacity = 0.0;

        var sb = new Storyboard();
        if (delayMs > 0)
        {
            sb.BeginTime = TimeSpan.FromMilliseconds(delayMs);
        }

        var defaultEasing = easing ?? new QuarticEase { EasingMode = EasingMode.EaseOut };

        // X Translation
        if (Math.Abs(offsetX) > 0.01)
        {
            var animX = new DoubleAnimation
            {
                To = 0.0,
                Duration = TimeSpan.FromMilliseconds(durationMs),
                EasingFunction = defaultEasing
            };
            Storyboard.SetTarget(animX, translate);
            Storyboard.SetTargetProperty(animX, "X");
            sb.Children.Add(animX);
        }

        // Y Translation
        if (Math.Abs(offsetY) > 0.01)
        {
            var animY = new DoubleAnimation
            {
                To = 0.0,
                Duration = TimeSpan.FromMilliseconds(durationMs),
                EasingFunction = defaultEasing
            };
            Storyboard.SetTarget(animY, translate);
            Storyboard.SetTargetProperty(animY, "Y");
            sb.Children.Add(animY);
        }

        // Scale
        if (Math.Abs(scaleFrom - 1.0) > 0.001)
        {
            var animScaleX = new DoubleAnimation
            {
                To = 1.0,
                Duration = TimeSpan.FromMilliseconds(durationMs),
                EasingFunction = defaultEasing
            };
            Storyboard.SetTarget(animScaleX, scale);
            Storyboard.SetTargetProperty(animScaleX, "ScaleX");
            sb.Children.Add(animScaleX);

            var animScaleY = new DoubleAnimation
            {
                To = 1.0,
                Duration = TimeSpan.FromMilliseconds(durationMs),
                EasingFunction = defaultEasing
            };
            Storyboard.SetTarget(animScaleY, scale);
            Storyboard.SetTargetProperty(animScaleY, "ScaleY");
            sb.Children.Add(animScaleY);
        }

        // Fade In
        var animFade = new DoubleAnimation
        {
            To = 1.0,
            Duration = TimeSpan.FromMilliseconds(durationMs),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(animFade, element);
        Storyboard.SetTargetProperty(animFade, "Opacity");
        sb.Children.Add(animFade);

        sb.Begin();
    }

    /// <summary>
    /// Animates an element with exit slide, scale, and fade out.
    /// </summary>
    public static async Task AnimateElementExitAsync(
        UIElement element,
        double targetOffsetX, double targetOffsetY,
        double scaleTo = 0.97,
        int durationMs = 120)
    {
        EnsureRenderTransforms(element, out var translate, out var scale);

        var tcs = new TaskCompletionSource<bool>();
        var sb = new Storyboard();
        var easing = new CubicEase { EasingMode = EasingMode.EaseIn };

        if (Math.Abs(targetOffsetX) > 0.01)
        {
            var animX = new DoubleAnimation
            {
                To = targetOffsetX,
                Duration = TimeSpan.FromMilliseconds(durationMs),
                EasingFunction = easing
            };
            Storyboard.SetTarget(animX, translate);
            Storyboard.SetTargetProperty(animX, "X");
            sb.Children.Add(animX);
        }

        if (Math.Abs(targetOffsetY) > 0.01)
        {
            var animY = new DoubleAnimation
            {
                To = targetOffsetY,
                Duration = TimeSpan.FromMilliseconds(durationMs),
                EasingFunction = easing
            };
            Storyboard.SetTarget(animY, translate);
            Storyboard.SetTargetProperty(animY, "Y");
            sb.Children.Add(animY);
        }

        if (Math.Abs(scaleTo - 1.0) > 0.001)
        {
            var animScaleX = new DoubleAnimation
            {
                To = scaleTo,
                Duration = TimeSpan.FromMilliseconds(durationMs),
                EasingFunction = easing
            };
            Storyboard.SetTarget(animScaleX, scale);
            Storyboard.SetTargetProperty(animScaleX, "ScaleX");
            sb.Children.Add(animScaleX);

            var animScaleY = new DoubleAnimation
            {
                To = scaleTo,
                Duration = TimeSpan.FromMilliseconds(durationMs),
                EasingFunction = easing
            };
            Storyboard.SetTarget(animScaleY, scale);
            Storyboard.SetTargetProperty(animScaleY, "ScaleY");
            sb.Children.Add(animScaleY);
        }

        var animFade = new DoubleAnimation
        {
            To = 0.0,
            Duration = TimeSpan.FromMilliseconds(durationMs),
            EasingFunction = easing
        };
        Storyboard.SetTarget(animFade, element);
        Storyboard.SetTargetProperty(animFade, "Opacity");
        sb.Children.Add(animFade);

        sb.Completed += (s, e) => tcs.TrySetResult(true);
        sb.Begin();

        await tcs.Task;
    }

    /// <summary>
    /// Executes a liquid spring pulse on orb/status indicators (Scale 0.4 -> 1.12 -> 1.0).
    /// </summary>
    public static void AnimateOrbLiquidPulse(
        UIElement element,
        int durationMs = 320,
        int delayMs = 0)
    {
        EnsureRenderTransforms(element, out _, out var scale);

        scale.ScaleX = 0.4;
        scale.ScaleY = 0.4;
        element.Opacity = 0.0;

        var sb = new Storyboard();
        if (delayMs > 0)
        {
            sb.BeginTime = TimeSpan.FromMilliseconds(delayMs);
        }

        var backEasing = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.6 };

        var animScaleX = new DoubleAnimation
        {
            To = 1.0,
            Duration = TimeSpan.FromMilliseconds(durationMs),
            EasingFunction = backEasing
        };
        Storyboard.SetTarget(animScaleX, scale);
        Storyboard.SetTargetProperty(animScaleX, "ScaleX");
        sb.Children.Add(animScaleX);

        var animScaleY = new DoubleAnimation
        {
            To = 1.0,
            Duration = TimeSpan.FromMilliseconds(durationMs),
            EasingFunction = backEasing
        };
        Storyboard.SetTarget(animScaleY, scale);
        Storyboard.SetTargetProperty(animScaleY, "ScaleY");
        sb.Children.Add(animScaleY);

        var animFade = new DoubleAnimation
        {
            To = 1.0,
            Duration = TimeSpan.FromMilliseconds(durationMs / 2),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(animFade, element);
        Storyboard.SetTargetProperty(animFade, "Opacity");
        sb.Children.Add(animFade);

        sb.Begin();
    }

    /// <summary>
    /// Executes exit animation for the given page.
    /// </summary>
    public static async Task AnimatePageExitAsync(Page page)
    {
        if (page == null) return;

        // General page fade and subtle scale down exit
        await AnimateElementExitAsync(page, 0, 15, scaleTo: 0.96, durationMs: 130);
    }
}
