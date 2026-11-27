using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using NOVAIsland.Core.Models;
using NOVAIsland.Core.Services;

namespace NOVAIsland.App.Animations;

/// <summary>
/// Windows.UI / Microsoft.UI.Composition damped spring animator for Venom-style
/// top-edge liquid expansion, retraction, hover scale, and ambient pulse.
/// </summary>
public sealed class CompositionSpringAnimator
{
    public void AnimateVenomOpen(
        UIElement shell,
        UIElement content,
        double targetWidth,
        AnimationSettings animSettings,
        double speed)
    {
        var shellVisual = ElementCompositionPreview.GetElementVisual(shell);
        var compositor = shellVisual.Compositor;
        var cfg = SpringPhysicsConfigMapper.FromPreset(animSettings.Preset, speed, animSettings.ReduceMotion);

        var centerX = (float)Math.Max(60.0, targetWidth / 2.0);
        shellVisual.CenterPoint = new Vector3(centerX, 0f, 0f);
        shellVisual.StopAnimation(nameof(Visual.Scale));
        shellVisual.StopAnimation(nameof(Visual.Opacity));

        if (cfg.Instant)
        {
            shellVisual.Scale = Vector3.One;
            shellVisual.Opacity = 1f;
            AnimateContentEntrance(content, animSettings, speed);
            return;
        }

        shellVisual.Scale = new Vector3(0.58f, 0.14f, 1f);
        shellVisual.Opacity = 0.45f;

        var scaleSpring = compositor.CreateSpringVector3Animation();
        scaleSpring.FinalValue = Vector3.One;
        scaleSpring.DampingRatio = Math.Clamp(cfg.DampingRatio, 0.80f, 0.92f);
        scaleSpring.Period = TimeSpan.FromMilliseconds(Math.Clamp(220 / Math.Max(0.5, speed), 130, 290));
        shellVisual.StartAnimation(nameof(Visual.Scale), scaleSpring);

        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(0f, 0.45f);
        fade.InsertKeyFrame(1f, 1f, compositor.CreateCubicBezierEasingFunction(new Vector2(0.2f, 0.9f), new Vector2(0.3f, 1f)));
        fade.Duration = TimeSpan.FromMilliseconds(Math.Max(100, 160 / Math.Max(0.5, speed)));
        shellVisual.StartAnimation(nameof(Visual.Opacity), fade);

        AnimateContentEntrance(content, animSettings, speed);
    }

    public async Task<bool> AnimateVenomRetractAsync(
        UIElement shell,
        double currentWidth,
        AnimationSettings animSettings,
        double speed,
        CancellationToken cancellationToken)
    {
        var shellVisual = ElementCompositionPreview.GetElementVisual(shell);
        var compositor = shellVisual.Compositor;
        var cfg = SpringPhysicsConfigMapper.FromPreset(animSettings.Preset, speed, animSettings.ReduceMotion);

        if (cfg.Instant)
        {
            shellVisual.Scale = Vector3.One;
            shellVisual.Opacity = 1f;
            return true;
        }

        var centerX = (float)Math.Max(60.0, currentWidth / 2.0);
        shellVisual.CenterPoint = new Vector3(centerX, 0f, 0f);

        var durationMs = Math.Clamp(175 / Math.Max(0.5, speed), 100, 240);
        var ease = compositor.CreateCubicBezierEasingFunction(new Vector2(0.4f, 0f), new Vector2(0.8f, 0.4f));

        var scaleAnim = compositor.CreateVector3KeyFrameAnimation();
        scaleAnim.InsertKeyFrame(1f, new Vector3(0.34f, 0.06f, 1f), ease);
        scaleAnim.Duration = TimeSpan.FromMilliseconds(durationMs);
        shellVisual.StartAnimation(nameof(Visual.Scale), scaleAnim);

        var fadeAnim = compositor.CreateScalarKeyFrameAnimation();
        fadeAnim.InsertKeyFrame(1f, 0f, ease);
        fadeAnim.Duration = TimeSpan.FromMilliseconds(durationMs);
        shellVisual.StartAnimation(nameof(Visual.Opacity), fadeAnim);

        try
        {
            await Task.Delay((int)Math.Ceiling(durationMs), cancellationToken);
            shellVisual.StopAnimation(nameof(Visual.Scale));
            shellVisual.StopAnimation(nameof(Visual.Opacity));
            shellVisual.Scale = Vector3.One;
            shellVisual.Opacity = 1f;
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    public void RestoreShellImmediate(UIElement shell)
    {
        var visual = ElementCompositionPreview.GetElementVisual(shell);
        visual.StopAnimation(nameof(Visual.Scale));
        visual.StopAnimation(nameof(Visual.Opacity));
        visual.Scale = Vector3.One;
        visual.Opacity = 1f;
    }

    public void AnimateStealthLineAppear(UIElement lineElement, AnimationSettings animSettings)
    {
        var visual = ElementCompositionPreview.GetElementVisual(lineElement);
        visual.StopAnimation(nameof(Visual.Opacity));
        if (animSettings.ReduceMotion)
        {
            visual.Opacity = 1f;
            return;
        }

        visual.Opacity = 0f;
        var fade = visual.Compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(1f, 1f);
        fade.Duration = TimeSpan.FromMilliseconds(180);
        visual.StartAnimation(nameof(Visual.Opacity), fade);
    }

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
        ElementCompositionPreview.SetIsTranslationEnabled(element, true);
        var visual = ElementCompositionPreview.GetElementVisual(element);
        var compositor = visual.Compositor;
        var cfg = SpringPhysicsConfigMapper.FromPreset(animSettings.Preset, speed, animSettings.ReduceMotion);

        if (cfg.Instant)
        {
            visual.Opacity = 1f;
            visual.Properties.InsertVector3("Translation", Vector3.Zero);
            return;
        }

        visual.Opacity = 0f;
        visual.Properties.InsertVector3("Translation", new Vector3(0f, -10f, 0f));

        var fade = compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(1f, 1f);
        fade.Duration = TimeSpan.FromMilliseconds(Math.Max(110, 190 / Math.Max(0.5, speed)));
        visual.StartAnimation(nameof(Visual.Opacity), fade);

        var slide = compositor.CreateSpringVector3Animation();
        slide.FinalValue = Vector3.Zero;
        slide.DampingRatio = cfg.DampingRatio;
        slide.Period = cfg.Period;
        visual.StartAnimation("Translation", slide);
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

    public void StartDoomEnergyAnimations(UIElement glowLayer, UIElement sweepBeam, double shellWidth, bool reduceMotion)
    {
        ElementCompositionPreview.SetIsTranslationEnabled(sweepBeam, true);
        var glowVisual = ElementCompositionPreview.GetElementVisual(glowLayer);
        var sweepVisual = ElementCompositionPreview.GetElementVisual(sweepBeam);

        SafeStopAnimation(glowVisual, nameof(Visual.Opacity));
        SafeStopAnimation(sweepVisual, "Translation");

        if (reduceMotion)
        {
            glowVisual.Opacity = 0.55f;
            sweepVisual.Properties.InsertVector3("Translation", Vector3.Zero);
            return;
        }

        var c = glowVisual.Compositor;
        var pulse = c.CreateScalarKeyFrameAnimation();
        pulse.InsertKeyFrame(0f, 0.40f);
        pulse.InsertKeyFrame(0.5f, 0.95f);
        pulse.InsertKeyFrame(1f, 0.40f);
        pulse.Duration = TimeSpan.FromSeconds(3.2);
        pulse.IterationBehavior = AnimationIterationBehavior.Forever;
        glowVisual.StartAnimation(nameof(Visual.Opacity), pulse);

        var span = (float)Math.Max(420.0, shellWidth + 180.0);
        var sweep = c.CreateVector3KeyFrameAnimation();
        sweep.InsertKeyFrame(0f, new Vector3(-160f, 0f, 0f));
        sweep.InsertKeyFrame(0.65f, new Vector3(span, 0f, 0f));
        sweep.InsertKeyFrame(1f, new Vector3(span, 0f, 0f));
        sweep.Duration = TimeSpan.FromSeconds(4.2);
        sweep.IterationBehavior = AnimationIterationBehavior.Forever;
        sweepVisual.StartAnimation("Translation", sweep);
    }

    public void StopDoomEnergyAnimations(UIElement glowLayer, UIElement sweepBeam)
    {
        ElementCompositionPreview.SetIsTranslationEnabled(sweepBeam, true);
        SafeStopAnimation(ElementCompositionPreview.GetElementVisual(glowLayer), nameof(Visual.Opacity));
        SafeStopAnimation(ElementCompositionPreview.GetElementVisual(sweepBeam), "Translation");
    }

    private static void SafeStopAnimation(Visual visual, string propertyName)
    {
        try
        {
            visual.StopAnimation(propertyName);
        }
        catch
        {
            // Ignore if property has no active animation yet
        }
    }
}
