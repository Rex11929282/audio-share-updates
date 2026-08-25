using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace AudioShare.App;

internal sealed class MotionController
{
    private const int MaximumFramesPerSecond = 60;
    private static readonly TimeSpan AudioPulseCooldown = TimeSpan.FromMilliseconds(850);

    // Launch draw-on timings, indexed to match the launchStrokes / launchAccents lists.
    private static readonly (double BeginMs, double DurationMs)[] LaunchStrokeTimings =
    [
        (90, 620),    // flow wave
        (540, 300),   // note stem + flag
        (780, 250),   // cast arc one
        (900, 270),   // cast arc two
    ];

    private static readonly (double BeginMs, double DurationMs)[] LaunchAccentTimings =
    [
        (700, 240),   // note head
        (1140, 230),  // play triangle
    ];

    private readonly Window owner;
    private readonly FrameworkElement mainContent;
    private readonly FrameworkElement logo;
    private readonly FrameworkElement launchOverlay;
    private readonly FrameworkElement launchBrand;
    private readonly FrameworkElement launchTitle;
    private readonly IReadOnlyList<Shape> launchStrokes;
    private readonly IReadOnlyList<FrameworkElement> launchAccents;
    private readonly FrameworkElement topCard;
    private readonly FrameworkElement statusPulse;
    private readonly FrameworkElement b1Fill;
    private readonly FrameworkElement b1ActivityBars;
    private readonly IReadOnlyList<FrameworkElement> atmosphereBars;
    private readonly FrameworkElement routeFlowPath;
    private readonly FrameworkElement routeBeaconOne;
    private readonly FrameworkElement routeBeaconTwo;
    private readonly FrameworkElement listPanel;
    private readonly List<RunningStoryboard> runningStoryboards = [];
    private DateTimeOffset lastAudioPulseAt;
    private double lastAudioLevel;
    private bool reduceMotion;
    private bool suspended;
    private bool routeIsActive;

    public MotionController(
        Window owner,
        FrameworkElement mainContent,
        FrameworkElement logo,
        FrameworkElement launchOverlay,
        FrameworkElement launchBrand,
        FrameworkElement launchTitle,
        IReadOnlyList<Shape> launchStrokes,
        IReadOnlyList<FrameworkElement> launchAccents,
        FrameworkElement topCard,
        FrameworkElement statusPulse,
        FrameworkElement b1Fill,
        FrameworkElement b1ActivityBars,
        IReadOnlyList<FrameworkElement> atmosphereBars,
        FrameworkElement routeFlowPath,
        FrameworkElement routeBeaconOne,
        FrameworkElement routeBeaconTwo,
        FrameworkElement listPanel,
        bool reduceMotion)
    {
        this.owner = owner;
        this.mainContent = mainContent;
        this.logo = logo;
        this.launchOverlay = launchOverlay;
        this.launchBrand = launchBrand;
        this.launchTitle = launchTitle;
        this.launchStrokes = launchStrokes;
        this.launchAccents = launchAccents;
        this.topCard = topCard;
        this.statusPulse = statusPulse;
        this.b1Fill = b1Fill;
        this.b1ActivityBars = b1ActivityBars;
        this.atmosphereBars = atmosphereBars;
        this.routeFlowPath = routeFlowPath;
        this.routeBeaconOne = routeBeaconOne;
        this.routeBeaconTwo = routeBeaconTwo;
        this.listPanel = listPanel;
        this.reduceMotion = reduceMotion;

        ApplyFinalValues();
        PrepareLaunch();
    }

    public void SetReduceMotion(bool value)
    {
        if (reduceMotion == value)
        {
            return;
        }

        reduceMotion = value;
        ApplyFinalValues();
    }

    public void PlayLaunch()
    {
        if (reduceMotion)
        {
            PlayReducedLaunch();
            return;
        }

        if (!CanPlay())
        {
            ApplyFinalValues();
            return;
        }

        PrepareLaunch();
        PlayLaunchReveal();
        PlayLaunchTitle();

        for (var index = 0; index < launchStrokes.Count && index < LaunchStrokeTimings.Length; index++)
        {
            var (beginMs, durationMs) = LaunchStrokeTimings[index];
            PlayStrokeDrawOn(launchStrokes[index], TimeSpan.FromMilliseconds(beginMs), TimeSpan.FromMilliseconds(durationMs));
        }

        for (var index = 0; index < launchAccents.Count && index < LaunchAccentTimings.Length; index++)
        {
            var (beginMs, durationMs) = LaunchAccentTimings[index];
            PlayAccentPop(launchAccents[index], TimeSpan.FromMilliseconds(beginMs), TimeSpan.FromMilliseconds(durationMs));
        }

        _ = PlayStartupChimeAsync();
    }

    public void PlaySharingConfirmed()
    {
        if (!CanPlay())
        {
            return;
        }

        SetRouteActive(true);
        PlayPulse(statusPulse, 1.08, TimeSpan.FromMilliseconds(340));
        Play(routeFlowPath, TimeSpan.FromMilliseconds(320), 14, 1, 0.25, 0.82);
        PlayBeacon(routeBeaconOne, -20, 72, TimeSpan.Zero);
        PlayBeacon(routeBeaconTwo, 18, 126, TimeSpan.FromMilliseconds(130));
        PlayPulse(b1Fill, 1.05, TimeSpan.FromMilliseconds(240));
    }

    public void PlayLocalOnlyConfirmed()
    {
        if (!CanPlay())
        {
            return;
        }

        SetRouteActive(false);
        PlayPulse(statusPulse, 1.05, TimeSpan.FromMilliseconds(260));
        Play(routeFlowPath, TimeSpan.FromMilliseconds(260), -10, 1, 0.82, 0);
        PlayBeacon(routeBeaconOne, 126, 42, TimeSpan.Zero);
        PlayBeacon(routeBeaconTwo, 82, -16, TimeSpan.FromMilliseconds(110));
    }

    public void PlayAudioLevelPulse(double level)
    {
        SetAtmosphereLevel(level);
        if (!CanPlay() || level < 2)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (Math.Abs(level - lastAudioLevel) < 8 && now - lastAudioPulseAt < AudioPulseCooldown)
        {
            return;
        }

        lastAudioLevel = level;
        lastAudioPulseAt = now;
        PlayPulse(b1ActivityBars, Math.Clamp(1.02 + level / 500, 1.02, 1.16), TimeSpan.FromMilliseconds(220));
    }

    public void PlaySelectionConfirmed(FrameworkElement target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (CanPlay())
        {
            PlayPulse(target, 1.16, TimeSpan.FromMilliseconds(220));
        }
    }

    public void PlayAttention()
    {
        if (CanPlay())
        {
            PlayPulse(topCard, 1.012, TimeSpan.FromMilliseconds(220));
        }
    }

    public void PlayWindowResume()
    {
        if (CanPlay())
        {
            Play(topCard, TimeSpan.FromMilliseconds(180), 0, 0.99, 0.86, 1);
        }
    }

    public void SetRouteActive(bool value)
    {
        if (routeIsActive == value)
        {
            return;
        }

        routeIsActive = value;
        StopRunningStoryboards(routeFlowPath);
        SetVisual(routeFlowPath, routeIsActive ? 0.64 : 0, 0, 0, 1);
        if (!routeIsActive)
        {
            SetAtmosphereBars(0);
        }
    }

    public void SetAtmosphereLevel(double level)
    {
        var normalized = Math.Clamp(level / 100, 0, 1);
        if (!CanPlay() || normalized < 0.02)
        {
            SetAtmosphereBars(0);
            return;
        }

        SetAtmosphereBars(normalized);
    }

    public void Suspend()
    {
        suspended = true;
        ApplyFinalValues();
    }

    public void Resume() => suspended = false;

    private bool CanPlay() =>
        !reduceMotion &&
        !suspended &&
        owner.IsVisible &&
        owner.WindowState != WindowState.Minimized;

    private void PlayEntrance(FrameworkElement target, TimeSpan beginTime, TimeSpan duration, double translateY, double scale)
    {
        SetVisual(target, 0, 0, translateY, scale);
        var storyboard = CreateTransformStoryboard(target, duration, beginTime, 0, 1, 0, translateY, scale, 1);
        Start(target, storyboard, () => SetVisual(target, 1, 0, 0, 1));
    }

    private void Play(FrameworkElement target, TimeSpan duration, double translateX, double scale, double fromOpacity, double toOpacity)
    {
        SetVisual(target, fromOpacity, translateX, 0, scale);
        Start(
            target,
            CreateTransformStoryboard(target, duration, TimeSpan.Zero, fromOpacity, toOpacity, translateX, 0, scale, 1),
            () => SetVisual(target, toOpacity, 0, 0, 1));
    }

    private void PlayPulse(FrameworkElement target, double peakScale, TimeSpan duration)
    {
        var firstHalf = TimeSpan.FromMilliseconds(duration.TotalMilliseconds * 0.45);
        var secondHalf = duration - firstHalf;
        SetVisual(target, 1, 0, 0, 1);
        var transforms = GetTransforms(target);
        var storyboard = new Storyboard { FillBehavior = FillBehavior.HoldEnd };
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        Add(storyboard, transforms.Scale, ScaleTransform.ScaleXProperty, 1, peakScale, firstHalf, TimeSpan.Zero, easing);
        Add(storyboard, transforms.Scale, ScaleTransform.ScaleYProperty, 1, peakScale, firstHalf, TimeSpan.Zero, easing);
        Add(storyboard, transforms.Scale, ScaleTransform.ScaleXProperty, peakScale, 1, secondHalf, firstHalf, easing);
        Add(storyboard, transforms.Scale, ScaleTransform.ScaleYProperty, peakScale, 1, secondHalf, firstHalf, easing);
        Start(target, storyboard, () => SetVisual(target, 1, 0, 0, 1));
    }

    private void PlayBeacon(FrameworkElement target, double fromX, double toX, TimeSpan beginTime)
    {
        const double durationMilliseconds = 360;
        var duration = TimeSpan.FromMilliseconds(durationMilliseconds);
        var fadeIn = TimeSpan.FromMilliseconds(90);
        var fadeOutBegin = beginTime + TimeSpan.FromMilliseconds(220);
        SetVisual(target, 0, fromX, 0, 0.72);
        var transforms = GetTransforms(target);
        var storyboard = new Storyboard { FillBehavior = FillBehavior.HoldEnd };
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        Add(storyboard, target, UIElement.OpacityProperty, 0, 1, fadeIn, beginTime, easing);
        Add(storyboard, target, UIElement.OpacityProperty, 1, 0, TimeSpan.FromMilliseconds(140), fadeOutBegin, easing);
        Add(storyboard, transforms.Translate, TranslateTransform.XProperty, fromX, toX, duration, beginTime, easing);
        Add(storyboard, transforms.Scale, ScaleTransform.ScaleXProperty, 0.72, 1, fadeIn, beginTime, easing);
        Add(storyboard, transforms.Scale, ScaleTransform.ScaleYProperty, 0.72, 1, fadeIn, beginTime, easing);
        Start(target, storyboard, () => SetVisual(target, 0, 0, 0, 1));
    }

    private void PlayAtmosphereEntrance()
    {
        if (!CanPlay())
        {
            return;
        }

        for (var index = 0; index < atmosphereBars.Count; index++)
        {
            var bar = atmosphereBars[index];
            SetAtmosphereBar(bar, 0.08, 0);
            var transforms = GetTransforms(bar);
            bar.RenderTransformOrigin = new Point(0.5, 1);
            var storyboard = new Storyboard { FillBehavior = FillBehavior.HoldEnd };
            var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
            Add(storyboard, bar, UIElement.OpacityProperty, 0, 0.26, TimeSpan.FromMilliseconds(280), TimeSpan.FromMilliseconds(340 + index * 38), easing);
            Add(storyboard, transforms.Scale, ScaleTransform.ScaleYProperty, 0.08, 0.18, TimeSpan.FromMilliseconds(280), TimeSpan.FromMilliseconds(340 + index * 38), easing);
            Start(bar, storyboard, () => SetAtmosphereBar(bar, 0.18, 0.26));
        }
    }

    private void PlayLaunchReveal()
    {
        launchOverlay.Visibility = Visibility.Visible;
        launchOverlay.IsHitTestVisible = true;
        SetVisual(launchOverlay, 1, 0, 0, 1);
        SetVisual(launchBrand, 1, 0, 0, 1);
        mainContent.Visibility = Visibility.Hidden;
        SetVisual(mainContent, 0, 0, 12, 0.99);
        mainContent.Visibility = Visibility.Visible;
        var storyboard = new Storyboard { FillBehavior = FillBehavior.HoldEnd };
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };

        // The overlay holds the finished mark, then lifts away as the main content settles in.
        var brandTransforms = GetTransforms(launchBrand);
        Add(storyboard, launchBrand, UIElement.OpacityProperty, 1, 0, TimeSpan.FromMilliseconds(260), TimeSpan.FromMilliseconds(1780), easing);
        Add(storyboard, brandTransforms.Translate, TranslateTransform.YProperty, 0, -10, TimeSpan.FromMilliseconds(300), TimeSpan.FromMilliseconds(1780), easing);
        Add(storyboard, launchOverlay, UIElement.OpacityProperty, 1, 0, TimeSpan.FromMilliseconds(300), TimeSpan.FromMilliseconds(1820), easing);

        var mainTransforms = GetTransforms(mainContent);
        Add(storyboard, mainContent, UIElement.OpacityProperty, 0, 1, TimeSpan.FromMilliseconds(360), TimeSpan.FromMilliseconds(1740), easing);
        Add(storyboard, mainTransforms.Translate, TranslateTransform.YProperty, 12, 0, TimeSpan.FromMilliseconds(440), TimeSpan.FromMilliseconds(1740), easing);
        Add(storyboard, mainTransforms.Scale, ScaleTransform.ScaleXProperty, 0.99, 1, TimeSpan.FromMilliseconds(440), TimeSpan.FromMilliseconds(1740), easing);
        Add(storyboard, mainTransforms.Scale, ScaleTransform.ScaleYProperty, 0.99, 1, TimeSpan.FromMilliseconds(440), TimeSpan.FromMilliseconds(1740), easing);
        Start(launchOverlay, storyboard, () =>
        {
            SetVisual(launchOverlay, 0, 0, 0, 1);
            launchOverlay.IsHitTestVisible = false;
            launchOverlay.Visibility = Visibility.Collapsed;
            SetVisual(mainContent, 1, 0, 0, 1);
        });
    }

    private void PlayReducedLaunch()
    {
        SetVisual(launchOverlay, 1, 0, 0, 1);
        launchOverlay.Visibility = Visibility.Visible;
        SetVisual(launchBrand, 1, 0, 0, 1);
        SetVisual(launchTitle, 1, 0, 0, 1);
        foreach (var stroke in launchStrokes)
        {
            ShowStrokeSolid(stroke);
        }

        foreach (var accent in launchAccents)
        {
            SetVisual(accent, 1, 0, 0, 1);
        }

        mainContent.Visibility = Visibility.Hidden;
        SetVisual(mainContent, 0, 0, 0, 1);
        mainContent.Visibility = Visibility.Visible;
        var storyboard = new Storyboard { FillBehavior = FillBehavior.HoldEnd };
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        Add(storyboard, launchOverlay, UIElement.OpacityProperty, 1, 0, TimeSpan.FromMilliseconds(220), TimeSpan.FromMilliseconds(360), easing);
        Add(storyboard, mainContent, UIElement.OpacityProperty, 0, 1, TimeSpan.FromMilliseconds(220), TimeSpan.FromMilliseconds(360), easing);
        Start(launchOverlay, storyboard, () =>
        {
            SetVisual(launchOverlay, 0, 0, 0, 1);
            launchOverlay.IsHitTestVisible = false;
            launchOverlay.Visibility = Visibility.Collapsed;
            SetVisual(mainContent, 1, 0, 0, 1);
        });
    }

    private void PlayLaunchTitle()
    {
        SetVisual(launchTitle, 0, 0, 14, 0.98);
        var storyboard = new Storyboard { FillBehavior = FillBehavior.HoldEnd };
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        var transforms = GetTransforms(launchTitle);
        Add(storyboard, launchTitle, UIElement.OpacityProperty, 0, 1, TimeSpan.FromMilliseconds(360), TimeSpan.FromMilliseconds(1040), easing);
        Add(storyboard, transforms.Translate, TranslateTransform.YProperty, 14, 0, TimeSpan.FromMilliseconds(400), TimeSpan.FromMilliseconds(1040), easing);
        Add(storyboard, transforms.Scale, ScaleTransform.ScaleXProperty, 0.98, 1, TimeSpan.FromMilliseconds(400), TimeSpan.FromMilliseconds(1040), easing);
        Add(storyboard, transforms.Scale, ScaleTransform.ScaleYProperty, 0.98, 1, TimeSpan.FromMilliseconds(400), TimeSpan.FromMilliseconds(1040), easing);
        Start(launchTitle, storyboard, () => SetVisual(launchTitle, 1, 0, 0, 1));
    }

    private void PlayStrokeDrawOn(Shape stroke, TimeSpan beginTime, TimeSpan duration)
    {
        var dashUnit = GetStrokeDashUnit(stroke);
        stroke.Opacity = 1;
        stroke.StrokeDashArray = new DoubleCollection { dashUnit, dashUnit };
        stroke.StrokeDashOffset = dashUnit;
        var storyboard = new Storyboard { FillBehavior = FillBehavior.HoldEnd };
        var easing = new CubicEase { EasingMode = EasingMode.EaseInOut };
        var animation = new DoubleAnimation(dashUnit, 0, new Duration(duration))
        {
            BeginTime = beginTime,
            EasingFunction = easing,
        };
        Timeline.SetDesiredFrameRate(animation, MaximumFramesPerSecond);
        Storyboard.SetTarget(animation, stroke);
        Storyboard.SetTargetProperty(animation, new PropertyPath(Shape.StrokeDashOffsetProperty));
        storyboard.Children.Add(animation);
        Start(stroke, storyboard, () => ShowStrokeSolid(stroke));
    }

    private void PlayAccentPop(FrameworkElement accent, TimeSpan beginTime, TimeSpan duration)
    {
        SetVisual(accent, 0, 0, 0, 0.6);
        var transforms = GetTransforms(accent);
        var storyboard = new Storyboard { FillBehavior = FillBehavior.HoldEnd };
        var fade = new CubicEase { EasingMode = EasingMode.EaseOut };
        var overshoot = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.55 };
        Add(storyboard, accent, UIElement.OpacityProperty, 0, 1, TimeSpan.FromMilliseconds(160), beginTime, fade);
        Add(storyboard, transforms.Scale, ScaleTransform.ScaleXProperty, 0.6, 1, duration, beginTime, overshoot);
        Add(storyboard, transforms.Scale, ScaleTransform.ScaleYProperty, 0.6, 1, duration, beginTime, overshoot);
        Start(accent, storyboard, () => SetVisual(accent, 1, 0, 0, 1));
    }

    private async Task PlayStartupChimeAsync()
    {
        await Task.Delay(1050);
        if (!suspended && !reduceMotion)
        {
            StartupChime.Play();
        }
    }

    private static double GetStrokeDashUnit(Shape stroke)
    {
        var thickness = stroke.StrokeThickness > 0 ? stroke.StrokeThickness : 1;
        var length = GetGeometryLength(stroke);
        // Dash values are measured in multiples of the stroke thickness. One dash and one gap
        // that each span the whole path let the stroke draw on cleanly from start to end.
        return (length / thickness) + 2;
    }

    private static double GetGeometryLength(Shape stroke)
    {
        if (stroke is not Path path || path.Data is null)
        {
            return 0;
        }

        var flattened = path.Data.GetFlattenedPathGeometry(0.05, ToleranceType.Absolute);
        double total = 0;
        foreach (var figure in flattened.Figures)
        {
            var previous = figure.StartPoint;
            foreach (var segment in figure.Segments)
            {
                if (segment is PolyLineSegment poly)
                {
                    foreach (var point in poly.Points)
                    {
                        total += (point - previous).Length;
                        previous = point;
                    }
                }
                else if (segment is LineSegment line)
                {
                    total += (line.Point - previous).Length;
                    previous = line.Point;
                }
            }

            if (figure.IsClosed)
            {
                total += (figure.StartPoint - previous).Length;
            }
        }

        return total;
    }

    private static void ShowStrokeSolid(Shape stroke)
    {
        stroke.StrokeDashOffset = 0;
        stroke.StrokeDashArray = null;
        stroke.Opacity = 1;
    }

    private void HideStroke(Shape stroke)
    {
        var dashUnit = GetStrokeDashUnit(stroke);
        stroke.Opacity = 1;
        stroke.StrokeDashArray = new DoubleCollection { dashUnit, dashUnit };
        stroke.StrokeDashOffset = dashUnit;
    }

    private Storyboard CreateTransformStoryboard(
        FrameworkElement target,
        TimeSpan duration,
        TimeSpan beginTime,
        double fromOpacity,
        double toOpacity,
        double fromX,
        double fromY,
        double fromScale,
        double toScale)
    {
        var transforms = GetTransforms(target);
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        var storyboard = new Storyboard { FillBehavior = FillBehavior.HoldEnd };
        Add(storyboard, target, UIElement.OpacityProperty, fromOpacity, toOpacity, duration, beginTime, easing);
        Add(storyboard, transforms.Translate, TranslateTransform.XProperty, fromX, 0, duration, beginTime, easing);
        Add(storyboard, transforms.Translate, TranslateTransform.YProperty, fromY, 0, duration, beginTime, easing);
        Add(storyboard, transforms.Scale, ScaleTransform.ScaleXProperty, fromScale, toScale, duration, beginTime, easing);
        Add(storyboard, transforms.Scale, ScaleTransform.ScaleYProperty, fromScale, toScale, duration, beginTime, easing);
        return storyboard;
    }

    private static void Add(
        Storyboard storyboard,
        DependencyObject target,
        DependencyProperty property,
        double from,
        double to,
        TimeSpan duration,
        TimeSpan beginTime,
        IEasingFunction easing)
    {
        var animation = new DoubleAnimation(from, to, new Duration(duration))
        {
            BeginTime = beginTime,
            EasingFunction = easing,
        };
        Timeline.SetDesiredFrameRate(animation, MaximumFramesPerSecond);
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, new PropertyPath(property));
        storyboard.Children.Add(animation);
    }

    private void Start(FrameworkElement target, Storyboard storyboard, Action applyFinalValues)
    {
        StopRunningStoryboards(target);
        EventHandler? completed = null;
        completed = (_, _) =>
        {
            applyFinalValues();
            storyboard.Completed -= completed;
            storyboard.Remove(owner);
            runningStoryboards.RemoveAll(running => running.Storyboard == storyboard);
        };
        storyboard.Completed += completed;
        runningStoryboards.Add(new RunningStoryboard(target, storyboard, completed));
        storyboard.Begin(owner, true);
    }

    private void ApplyFinalValues()
    {
        StopRunningStoryboards();
        SetVisual(logo, 1, 0, 0, 1);
        mainContent.Visibility = Visibility.Visible;
        SetVisual(mainContent, 1, 0, 0, 1);
        launchOverlay.Visibility = Visibility.Collapsed;
        SetVisual(launchOverlay, 0, 0, 0, 1);
        launchOverlay.IsHitTestVisible = false;
        SetVisual(launchBrand, 1, 0, 0, 1);
        SetVisual(launchTitle, 1, 0, 0, 1);
        foreach (var stroke in launchStrokes)
        {
            ShowStrokeSolid(stroke);
        }
        foreach (var accent in launchAccents)
        {
            SetVisual(accent, 1, 0, 0, 1);
        }
        SetVisual(topCard, 1, 0, 0, 1);
        SetVisual(statusPulse, 1, 0, 0, 1);
        SetVisual(b1Fill, 1, 0, 0, 1);
        SetVisual(b1ActivityBars, 0.65, 0, 0, 1);
        SetAtmosphereBars(0);
        SetVisual(routeFlowPath, routeIsActive ? 0.64 : 0, 0, 0, 1);
        SetVisual(routeBeaconOne, 0, 0, 0, 1);
        SetVisual(routeBeaconTwo, 0, 0, 0, 1);
        SetVisual(listPanel, 1, 0, 0, 1);
    }

    private void PrepareLaunch()
    {
        mainContent.Visibility = Visibility.Hidden;
        SetVisual(mainContent, 0, 0, 12, 0.99);
        launchOverlay.Visibility = Visibility.Visible;
        SetVisual(launchOverlay, 1, 0, 0, 1);
        launchOverlay.IsHitTestVisible = true;
        SetVisual(launchBrand, 1, 0, 0, 1);
        SetVisual(launchTitle, 0, 0, 14, 0.98);
        foreach (var stroke in launchStrokes)
        {
            HideStroke(stroke);
        }
        foreach (var accent in launchAccents)
        {
            SetVisual(accent, 0, 0, 0, 0.6);
        }
    }

    private void StopRunningStoryboards(FrameworkElement? target = null)
    {
        foreach (var running in runningStoryboards.Where(running => target is null || running.Target == target).ToArray())
        {
            running.Storyboard.Completed -= running.Completed;
            running.Storyboard.Remove(owner);
            runningStoryboards.Remove(running);
        }
    }

    private static void SetVisual(FrameworkElement target, double opacity, double x, double y, double scale)
    {
        target.Opacity = opacity;
        var transforms = GetTransforms(target);
        transforms.Translate.X = x;
        transforms.Translate.Y = y;
        transforms.Scale.ScaleX = scale;
        transforms.Scale.ScaleY = scale;
    }

    private void SetAtmosphereBars(double normalizedLevel)
    {
        for (var index = 0; index < atmosphereBars.Count; index++)
        {
            var variation = 0.58 + ((index * 37) % 43) / 100d;
            var scaleY = normalizedLevel <= 0
                ? 0.18
                : Math.Clamp(0.22 + normalizedLevel * variation, 0.22, 1);
            var opacity = normalizedLevel <= 0
                ? 0.26
                : Math.Clamp(0.4 + normalizedLevel * 0.6, 0.4, 1);
            SetAtmosphereBar(atmosphereBars[index], scaleY, opacity);
        }
    }

    private static void SetAtmosphereBar(FrameworkElement target, double scaleY, double opacity)
    {
        var transforms = GetTransforms(target);
        target.RenderTransformOrigin = new Point(0.5, 1);
        target.Opacity = opacity;
        transforms.Translate.X = 0;
        transforms.Translate.Y = 0;
        transforms.Scale.ScaleX = 1;
        transforms.Scale.ScaleY = scaleY;
    }

    private static MotionTransforms GetTransforms(FrameworkElement target)
    {
        if (target.RenderTransform is TransformGroup group)
        {
            var scale = group.Children.OfType<ScaleTransform>().FirstOrDefault();
            var translate = group.Children.OfType<TranslateTransform>().FirstOrDefault();
            if (scale is null)
            {
                scale = new ScaleTransform(1, 1);
                group.Children.Add(scale);
            }

            if (translate is null)
            {
                translate = new TranslateTransform();
                group.Children.Add(translate);
            }

            return new MotionTransforms(scale, translate);
        }

        var transforms = new TransformGroup();
        if (target.RenderTransform is { Value.IsIdentity: false } existingTransform)
        {
            transforms.Children.Add(existingTransform);
        }

        var newScale = new ScaleTransform(1, 1);
        var newTranslate = new TranslateTransform();
        transforms.Children.Add(newScale);
        transforms.Children.Add(newTranslate);
        target.RenderTransform = transforms;
        target.RenderTransformOrigin = new Point(0.5, 0.5);
        return new MotionTransforms(newScale, newTranslate);
    }

    private sealed record MotionTransforms(ScaleTransform Scale, TranslateTransform Translate);

    private sealed record RunningStoryboard(FrameworkElement Target, Storyboard Storyboard, EventHandler Completed);
}
