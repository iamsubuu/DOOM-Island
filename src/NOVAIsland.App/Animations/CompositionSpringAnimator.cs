using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using NOVAIsland.Core.Models;
using NOVAIsland.Core.Services;

namespace NOVAIsland.App.Animations;

/// <summary>
/// Windows.UI / Microsoft.UI.Composition damped spring animator for Dynamic Island
/// scale, bounce, fade, and subtle ambient idle breathing.
/// </summary>
public sealed class CompositionSpringAnimator
{
    public void AnimateHoverScale(UIElement element, bool isHovered, AnimationSettings animSettings, double speed)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        var compositor = visual.Compositor;
        var targetScale = isHovered && animSettings.HoverExpand ? 1.022f : 1.0f;

        if (element is FrameworkElement fe && fe.ActualWidth > 0 && fe.ActualHeight > 0)
        {
            visual.CenterPoint = new Vector3((float)(fe.ActualWidth / 2.0), (float)(fe.ActualHeight / 2.0), 0f);
        }

        var cfg = SpringPhysicsConfigMapper.FromPreset(animSettings.Preset, speed, animSettings.ReduceMotion);
        if (cfg.Instant)
        {
            visual.Scale = new Vector3(targetScale, targetScale, 1f);
            return;
        }

        var spring = compositor.CreateSpringVector3Animation();
        spring.FinalValue = new Vector3(targetScale, targetScale, 1f);
        spring.DampingRatio = cfg.DampingRatio;
        spring.Period = cfg.Period;
        visual.StartAnimation(nameof(Visual.Scale), spring);
    }

    public void PulseNotificationArrival(UIElement element, AnimationSettings animSettings)
    {
        if (!animSettings.NotificationBounce || animSettings.ReduceMotion)
        {
            return;
        }

        var visual = ElementCompositionPreview.GetElementVisual(element);
        var compositor = visual.Compositor;
        if (element is FrameworkElement fe && fe.ActualWidth > 0 && fe.ActualHeight > 0)
        {
            visual.CenterPoint = new Vector3((float)(fe.ActualWidth / 2.0), 0f, 0f);
        }

        visual.Scale = new Vector3(1.036f, 1.036f, 1f);
        var spring = compositor.CreateSpringVector3Animation();
        spring.FinalValue = new Vector3(1f, 1f, 1f);
        spring.DampingRatio = 0.65f;
        spring.Period = TimeSpan.FromMilliseconds(260);
        visual.StartAnimation(nameof(Visual.Scale), spring);
    }

    public void AnimateContentEntrance(UIElement element, AnimationSettings animSettings, double speed)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        var compositor = visual.Compositor;
        var cfg = SpringPhysicsConfigMapper.FromPreset(animSettings.Preset, speed, animSettings.ReduceMotion);

        if (cfg.Instant)
        {
            visual.Opacity = 1f;
            visual.Offset = Vector3.Zero;
            return;
        }

        visual.Opacity = 0f;
        visual.Offset = new Vector3(0f, -6f, 0f);

        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(1f, 1f);
        fade.Duration = TimeSpan.FromMilliseconds(Math.Max(90, 170 / Math.Max(0.5, speed)));
        visual.StartAnimation(nameof(Visual.Opacity), fade);

        var slide = compositor.CreateSpringVector3Animation();
        slide.FinalValue = Vector3.Zero;
        slide.DampingRatio = cfg.DampingRatio;
        slide.Period = cfg.Period;
        visual.StartAnimation(nameof(Visual.Offset), slide);
    }

    public void SetIdleBreathing(UIElement element, bool enabled, bool reduceMotion)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        var compositor = visual.Compositor;
        visual.StopAnimation(nameof(Visual.Opacity));

        if (!enabled || reduceMotion)
        {
            visual.Opacity = 0.65f;
            return;
        }

        var anim = compositor.CreateScalarKeyFrameAnimation();
        anim.InsertKeyFrame(0f, 0.35f);
        anim.InsertKeyFrame(0.5f, 0.95f);
        anim.InsertKeyFrame(1f, 0.35f);
        anim.Duration = TimeSpan.FromSeconds(3.6);
        anim.IterationBehavior = AnimationIterationBehavior.Forever;
        visual.StartAnimation(nameof(Visual.Opacity), anim);
    }
}
