using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace AudioShare.App;

internal sealed class MotionController
{
    private const int MaximumFramesPerSecond = 30;
    private readonly Window owner;
    private readonly FrameworkElement logo;
    private readonly FrameworkElement topCard;
    private readonly FrameworkElement b1Fill;
    private readonly FrameworkElement routeFlowPath;
    private readonly FrameworkElement listPanel;
    private readonly List<Storyboard> runningStoryboards = [];
    private bool reduceMotion;
    private bool suspended;

    public MotionController(
        Window owner,
        FrameworkElement logo,
        FrameworkElement topCard,
        FrameworkElement b1Fill,
        FrameworkElement routeFlowPath,
        FrameworkElement listPanel,
        bool reduceMotion)
    {
        this.owner = owner;
        this.logo = logo;
        this.topCard = topCard;
        this.b1Fill = b1Fill;
        this.routeFlowPath = routeFlowPath;
        this.listPanel = listPanel;
        this.reduceMotion = reduceMotion;

        ApplyFinalValues();
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
        if (!CanPlay())
        {
            return;
        }

        PlayEntrance(logo, TimeSpan.Zero, TimeSpan.FromMilliseconds(450), 12, 0.94);
        PlayEntrance(topCard, TimeSpan.FromMilliseconds(120), TimeSpan.FromMilliseconds(620), 16, 0.98);
        PlayEntrance(listPanel, TimeSpan.FromMilliseconds(230), TimeSpan.FromMilliseconds(670), 18, 0.985);
    }

    public void PlayRouteFlow()
    {
        if (!CanPlay())
        {
            return;
        }

        Play(routeFlowPath, TimeSpan.FromMilliseconds(320), 14, 1, 0.25, 0.82);
        Play(b1Fill, TimeSpan.FromMilliseconds(240), 0, 0.96, 0.7, 1);
    }

    public void PlayStatusTransition()
    {
        if (!CanPlay())
        {
            return;
        }

        Play(topCard, TimeSpan.FromMilliseconds(180), 0, 0.985, 0.82, 1);
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
        var storyboard = CreateStoryboard(target, duration, beginTime, 0, 1, translateY, 0, scale, 1);
        Start(storyboard, () => SetVisual(target, 1, 0, 0, 1));
    }

    private void Play(FrameworkElement target, TimeSpan duration, double translateX, double scale, double fromOpacity, double toOpacity)
    {
        SetVisual(target, fromOpacity, translateX, 0, scale);
        Start(
            CreateStoryboard(target, duration, TimeSpan.Zero, fromOpacity, toOpacity, translateX, 0, scale, 1),
            () => SetVisual(target, toOpacity, 0, 0, 1));
    }

    private Storyboard CreateStoryboard(
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

    private void Start(Storyboard storyboard, Action applyFinalValues)
    {
        EventHandler? completed = null;
        completed = (_, _) =>
        {
            applyFinalValues();
            storyboard.Completed -= completed;
            storyboard.Remove(owner);
            runningStoryboards.Remove(storyboard);
        };
        storyboard.Completed += completed;
        runningStoryboards.Add(storyboard);
        storyboard.Begin(owner, true);
    }

    private void ApplyFinalValues()
    {
        StopRunningStoryboards();
        SetVisual(logo, 1, 0, 0, 1);
        SetVisual(topCard, 1, 0, 0, 1);
        SetVisual(b1Fill, 1, 0, 0, 1);
        SetVisual(routeFlowPath, 0.64, 0, 0, 1);
        SetVisual(listPanel, 1, 0, 0, 1);
    }

    private void StopRunningStoryboards()
    {
        foreach (var storyboard in runningStoryboards)
        {
            storyboard.Remove(owner);
        }

        runningStoryboards.Clear();
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

    private static MotionTransforms GetTransforms(FrameworkElement target)
    {
        if (target.RenderTransform is TransformGroup { Children.Count: 2 } group &&
            group.Children[0] is ScaleTransform scale &&
            group.Children[1] is TranslateTransform translate)
        {
            return new MotionTransforms(scale, translate);
        }

        var transforms = new TransformGroup();
        var newScale = new ScaleTransform(1, 1);
        var newTranslate = new TranslateTransform();
        transforms.Children.Add(newScale);
        transforms.Children.Add(newTranslate);
        target.RenderTransform = transforms;
        target.RenderTransformOrigin = new Point(0.5, 0.5);
        return new MotionTransforms(newScale, newTranslate);
    }

    private sealed record MotionTransforms(ScaleTransform Scale, TranslateTransform Translate);
}
