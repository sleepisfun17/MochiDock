using System.Diagnostics;

using System.Runtime.InteropServices;

using System.Windows;

using System.Windows.Controls;

using System.Windows.Input;

using System.Windows.Media;

using System.Windows.Media.Animation;

using System.Windows.Threading;

using MochiDock.Core;
using MochiDock.Services;

using Point = System.Windows.Point;



namespace MochiDock.Controls;







public partial class MascotControl : System.Windows.Controls.UserControl

{

    [StructLayout(LayoutKind.Sequential)]

    private struct POINT

    {

        public int X;

        public int Y;

    }



    [DllImport("user32.dll")]

    private static extern bool GetCursorPos(out POINT lpPoint);



    public static readonly DependencyProperty MoodProperty =

        DependencyProperty.Register(

            nameof(Mood),

            typeof(MascotMood),

            typeof(MascotControl),

            new PropertyMetadata(MascotMood.Idle, (d, e) =>

            {

                if (d is MascotControl mascot && e.NewValue is MascotMood mood)

                {

                    mascot.RequestMood(mood);

                }

            }));



    public MascotMood Mood

    {

        get => (MascotMood)GetValue(MoodProperty);

        set => SetValue(MoodProperty, value);

    }



    private readonly DispatcherTimer _blinkTimer;

    private readonly DispatcherTimer _idleSleepTimer;
    private readonly DispatcherTimer _winkTimer;
    private readonly DispatcherTimer _tailSwayTimer;   // Kuro's slow tail sway, now and then (KuroTailSway)
    private readonly DispatcherTimer _cursorWatchTimer;

    private readonly Random _random = new();



    // V-Sync smooth gaze tracking for 200Hz / 144Hz / 60Hz monitors

    private double _curX;

    private double _curY;

    private double _targetX;

    private double _targetY;

    private readonly Stopwatch _frameStopwatch = Stopwatch.StartNew();

    private bool _isRenderingHooked;

    private POINT _lastMousePos;

    private DateTime _lastMouseMoveTime = DateTime.Now;



    public MascotControl()

    {

        InitializeComponent();
        // The status badge stays still while the body bounces or sways (Music, a new message, Victory, the overdue shake...): it follows the
        // body's offset, negated, every frame. The badge is only a few pixels wide, and moving it by fractions of a pixel made it shimmer.
        foreach (var axis in new[] { TranslateTransform.XProperty, TranslateTransform.YProperty })
            System.Windows.Data.BindingOperations.SetBinding(BadgeSteady, axis,
                new System.Windows.Data.Binding(axis.Name) { Source = BodyBounceTransform, Converter = NegateConverter.Instance });
        // The cats' back band is shown whenever the headset is (many faces switch HeadphonesLayer; one place keeps the two together).
        System.ComponentModel.DependencyPropertyDescriptor.FromProperty(UIElement.VisibilityProperty, typeof(UIElement))
            .AddValueChanged(HeadphonesLayer, (_, _) => HeadbandBackLayer.Visibility = HeadphonesLayer.Visibility);
        ApplyCatParts();   // the ears, the tail and (for Kuro) the light face ink, when the mascot is a cat
        if (_kind != MochiDock.Core.MascotKind.Mochi || _theme != MascotTheme.Default) ApplyMood(Mood);   // the XAML body is the grey Mochi
        BuildWind();
        Unloaded += (_, _) => { if (_spinning) EndSpin(landing: false); };
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible && _spinning) EndSpin(landing: false);
            UpdateActivityTimers();
        };



        // Periodic blink timer (2.5 to 5.5s intervals)

        _blinkTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.5) };

        _blinkTimer.Tick += BlinkTimer_Tick;



        // Idle check for restful Mochi sleep mode

        _idleSleepTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };

        _idleSleepTimer.Tick += IdleSleepTimer_Tick;



        // An occasional wink while he is only idling (3 to 6 minutes apart)
        _winkTimer = new DispatcherTimer { Interval = MochiWink.NextDelay(_random.NextDouble()) };
        _winkTimer.Tick += (s, e) =>
        {
            _winkTimer.Interval = MochiWink.NextDelay(_random.NextDouble());
            if (IsVisible && MochiWink.CanWink(Mood) && AnimationsEnabled) PerformWink();
        };

        // Kuro's tail sways once every 6 to 12 seconds while he rests: a one-shot animation, never a loop, so nothing renders between sways.
        _tailSwayTimer = new DispatcherTimer { Interval = KuroTailSway.NextDelay(_random.NextDouble()) };
        _tailSwayTimer.Tick += (s, e) =>
        {
            _tailSwayTimer.Interval = KuroTailSway.NextDelay(_random.NextDouble());
            if (_isCat && AnimationsEnabled && IsVisible && !_spinning && KuroTailSway.CanSway(Mood)) SwayTail();
        };

        // Lightweight cursor-movement check when gaze has converged so CompositionTarget.Rendering stays unhooked at idle
        _cursorWatchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _cursorWatchTimer.Tick += CursorWatchTimer_Tick;

        Loaded += (s, e) =>

        {

            UpdateActivityTimers();

        };



        Unloaded += (s, e) =>

        {

            StopRenderingHook();

            _cursorWatchTimer.Stop();

            _blinkTimer.Stop();
            _winkTimer.Stop();
            _tailSwayTimer.Stop();

            _idleSleepTimer.Stop();

        };

    }



    private void UpdateActivityTimers()
    {
        if (!IsLoaded || !IsVisible)
        {
            StopRenderingHook();
            _cursorWatchTimer.Stop();
            _blinkTimer.Stop();
            _winkTimer.Stop();
            _tailSwayTimer.Stop();
            _idleSleepTimer.Stop();
            UpdateKuroSpark();
            return;
        }

        _blinkTimer.Start();
        _winkTimer.Start();
        _tailSwayTimer.Start();
        _idleSleepTimer.Start();
        UpdateKuroSpark();
        if (IsStill(Mood))
        {
            StopRenderingHook();
            _cursorWatchTimer.Stop();
        }
        else
        {
            StartRenderingHook();
        }
    }

    private void CursorWatchTimer_Tick(object? sender, EventArgs e)
    {
        if (!IsLoaded || !IsVisible || IsStill(Mood))
        {
            _cursorWatchTimer.Stop();
            return;
        }
        if (_isRenderingHooked)
        {
            _cursorWatchTimer.Stop();
            return;
        }
        if (GetCursorPos(out var pt) && (pt.X != _lastMousePos.X || pt.Y != _lastMousePos.Y))
        {
            _cursorWatchTimer.Stop();
            StartRenderingHook();
        }
    }

    private void StartRenderingHook()
    {
        if (!_isRenderingHooked)
        {
            _frameStopwatch.Restart();
            CompositionTarget.Rendering += OnCompositionRendering;
            _isRenderingHooked = true;
        }
    }

    private void StopRenderingHook()
    {
        if (_isRenderingHooked)
        {
            CompositionTarget.Rendering -= OnCompositionRendering;
            _isRenderingHooked = false;
        }
    }

    private void OnCompositionRendering(object? sender, EventArgs e)
    {
        if (!IsLoaded || !IsVisible || IsStill(Mood))
        {
            StopRenderingHook();
            return;
        }

        double dt = _frameStopwatch.Elapsed.TotalSeconds;
        _frameStopwatch.Restart();

        if (dt > 0.1) dt = 0.016;

        bool mouseMoved = false;
        try
        {
            if (GetCursorPos(out var pt))
            {
                if (pt.X != _lastMousePos.X || pt.Y != _lastMousePos.Y)
                {
                    mouseMoved = true;
                    _lastMousePos = pt;
                    _lastMouseMoveTime = DateTime.Now;

                    if (Mood == MascotMood.Sleeping)
                    {
                        var mascotCenter = PointToScreen(new Point(ActualWidth / 2.0, ActualHeight / 2.0));
                        double mdx = pt.X - mascotCenter.X;
                        double mdy = pt.Y - mascotCenter.Y;
                        if (Math.Sqrt(mdx * mdx + mdy * mdy) < 65)
                        {
                            RequestMood(MascotMood.Idle);
                        }
                    }
                }

                if (Mood != MascotMood.Sleeping)
                {
                    var center = PointToScreen(new Point(ActualWidth / 2.0, ActualHeight / 2.0));
                    double dx = pt.X - center.X;
                    double dy = pt.Y - center.Y;
                    double dist = Math.Sqrt(dx * dx + dy * dy);

                    double maxOffset = 2.2;
                    double factor = (dist > 0) ? Math.Min(dist / 80.0, 1.0) : 0;
                    _targetX = (dist > 0) ? (dx / dist) * factor * maxOffset : 0;
                    _targetY = (dist > 0) ? (dy / dist) * factor * maxOffset : 0;
                }
                else
                {
                    _targetX = 0;
                    _targetY = 1.0;
                }
            }
        }
        catch { }

        // Exponential smoothing (Coucou math) - perfectly fluid at 60Hz, 144Hz, 200Hz, 240Hz
        double k = 1.0 - Math.Pow(0.0005, dt);
        _curX += (_targetX - _curX) * k;
        _curY += (_targetY - _curY) * k;

        if (!mouseMoved && Math.Abs(_targetX - _curX) < 0.01 && Math.Abs(_targetY - _curY) < 0.01)
        {
            _curX = _targetX;
            _curY = _targetY;
            FaceTransform.X = _curX;
            FaceTransform.Y = _curY;
            StopRenderingHook();
            _cursorWatchTimer.Start();
            return;
        }

        FaceTransform.X = _curX;
        FaceTransform.Y = _curY;
    }



    private void IdleSleepTimer_Tick(object? sender, EventArgs e)

    {

        // Nobody at the PC (no keyboard or mouse input for 10 minutes) and nothing else going on: Mochi takes a nap. Typing counts.
        if (Mood == MascotMood.Idle && MochiDock.Services.UserIdleService.IdleTime() >= MascotTriggers.AwayAfter)

        {

            RequestMood(MascotMood.Sleeping);

        }

    }



    private void BlinkTimer_Tick(object? sender, EventArgs e)

    {

        if (Mood != MascotMood.Music && Mood != MascotMood.Sleeping && !IsStill(Mood))

        {

            PerformBlink();

        }

        _blinkTimer.Interval = TimeSpan.FromMilliseconds(_random.Next(2500, 5500));

    }



    /// <summary>One eye closes for a moment (the right one).</summary>
    public void PerformWink()
    {
        var close = new DoubleAnimation(1.0, 0.08, TimeSpan.FromMilliseconds(180)) { AutoReverse = true, EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut } };
        RightEyeBlinkTransform.BeginAnimation(ScaleTransform.ScaleYProperty, close);
    }

    public void PerformBlink()

    {

        var closeAnim = new DoubleAnimation(1.0, 0.08, TimeSpan.FromMilliseconds(75))

        {

            AutoReverse = true,

            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }

        };



        LeftEyeBlinkTransform.BeginAnimation(ScaleTransform.ScaleYProperty, closeAnim);

        RightEyeBlinkTransform.BeginAnimation(ScaleTransform.ScaleYProperty, closeAnim);

    }



    // The plain faces (Idle, Thinking, Notice, Music, Sleeping, Gaming, Happy): pill eyes and nothing else unless the case changes it. The faces
    // that look alike are told apart by their eyes (Thinking glances, Notice is wide-eyed, Sleeping has closed arcs) and, for Notice and
    // Sleeping, by the status badge (ApplyBadge).
    private void ApplyBaseMood(MascotMood mood)
    {
        ShowEyes(Eyes.Pill);
        HeadphonesLayer.Visibility = Visibility.Collapsed;
        SweatDrop.Visibility = Visibility.Collapsed;
        BodyBounceTransform.BeginAnimation(TranslateTransform.YProperty, null);
        switch (mood)
        {
            case MascotMood.Waiting:
                // Thinking: both pills glance up and to the side.
                LeftPillShift.X = RightPillShift.X = 1.7;
                LeftPillShift.Y = RightPillShift.Y = -1.9;
                break;
            case MascotMood.Alert:
                // A notice: wide eyes (the red "!" badge says what it is).
                ShowEyes(Eyes.Wide);
                PerformBlink();
                break;
            case MascotMood.Music:
                ShowEyes(Eyes.Happy);
                ShowHeadset(HeadsetMood.Music);
                var bounceAnim = new DoubleAnimation(0, -2.5, TimeSpan.FromMilliseconds(380))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                };
                BodyBounceTransform.BeginAnimation(TranslateTransform.YProperty, bounceAnim);
                // Dancing: a side sway at half the bounce rate (a canned rhythm; the app has no audio level to follow).
                if (AnimationsEnabled) BodyBounceTransform.BeginAnimation(TranslateTransform.XProperty, Loop(-1.3, 1.3, 760, easing: new SineEase { EasingMode = EasingMode.EaseInOut }));
                break;
            case MascotMood.Sleeping:
                ShowEyes(Eyes.Sleepy);
                // Deep relaxed sleeping breathing sway (the "z" is the status badge)
                var sleepBreathing = new DoubleAnimation(0, 1.8, TimeSpan.FromMilliseconds(1600))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                };
                BodyBounceTransform.BeginAnimation(TranslateTransform.YProperty, sleepBreathing);
                break;
            case MascotMood.Focused:
                FaceTransform.X = 0;
                FaceTransform.Y = 0;
                _curX = 0;
                _curY = 0;
                _targetX = 0;
                _targetY = 0;
                break;
            case MascotMood.Happy:
                ShowEyes(Eyes.Happy);
                BodyBounceTransform.BeginAnimation(TranslateTransform.YProperty,
                    new DoubleAnimation(0, -1.5, TimeSpan.FromMilliseconds(300))
                    { AutoReverse = true, RepeatBehavior = new RepeatBehavior(2) });
                break;
        }
    }

    // ---------------- Faces: angry, hot, freezing, dizzy and drained ----------------
    // Every mood first goes through ResetExtras, which hides all extra layers, stops their animations and restores the body colours,
    // so one face can never leak into the next. Animations are skipped when the system has client-area animations turned off.

    /// <summary>
    /// Raised by every Mochi (the capsule and the cards each have one) when it is clicked, before it reacts. The view model counts the
    /// clicks and may turn Mochi dizzy; the binding updates <see cref="Mood"/> synchronously, so the click handler can look at it afterwards.
    /// </summary>
    public static event Action? AnyMochiPetted;

    // Too dizzy to enjoy it: a short wobble, no heart, no purr, no happy squint.
    private void WobbleDizzy()
    {
        if (!AnimationsEnabled) return;
        var ease = new ElasticEase { EasingMode = EasingMode.EaseOut, Oscillations = 2, Springiness = 6 };
        BodyScaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1.08, 1.0, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease });
        BodyScaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.94, 1.0, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease });
    }

    private static bool IsExtraMood(MascotMood mood) =>
        mood is MascotMood.Angry or MascotMood.Hot or MascotMood.Freezing or MascotMood.Dizzy or MascotMood.Drained or MascotMood.Victory or MascotMood.Defeat or MascotMood.Worried or MascotMood.BreakTime
            or MascotMood.Excited or MascotMood.Surprised or MascotMood.Cheering or MascotMood.Praying or MascotMood.Waking or MascotMood.Bored
            or MascotMood.Electric or MascotMood.Money or MascotMood.MoneyDown or MascotMood.Sunny or MascotMood.Rainy or MascotMood.Snowy
            or MascotMood.Cloudy or MascotMood.Studying or MascotMood.Coffee
            or MascotMood.Yawning or MascotMood.Birthday or MascotMood.Meeting or MascotMood.Proud;

    // The gaming faces keep still: the eyes do not follow the pointer and do not blink.
    private static bool IsStill(MascotMood mood) => mood is MascotMood.Focused or MascotMood.Worried or MascotMood.BreakTime or MascotMood.Waking or MascotMood.Bored or MascotMood.Studying or MascotMood.Meeting;

    // The render harness sets the override (by reflection) to test the turn on a PC or session with Windows animations off.
    private static bool? _animationsOverride = null;   // only the harness assigns it (by reflection)
    private static bool AnimationsEnabled => _animationsOverride ?? SystemParameters.ClientAreaAnimation;

    private static SolidColorBrush Ink(string hex) => new((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex));

    // The face last drawn: the spin policy compares the next face with it (a re-apply of the same face does not turn).
    private MascotMood? _appliedMood;

    private void ApplyMood(MascotMood mood)
    {
        ResetExtras();
        if (IsExtraMood(mood)) ApplyExtraMood(mood);
        else ApplyBaseMood(mood);
        if (mood == MascotMood.Focused) ApplyFocusedFace();
        ApplyBadge(mood);
        _appliedMood = mood;
    }

    /// <summary>Off in the render harnesses, which draw faces without running animations; on in the app.</summary>
    public static bool SpinEnabled { get; set; } = true;

    private bool _spinning;
    private bool _swapped;
    private MascotMood _pendingMood;
    private static readonly TimeSpan TurnDuration = TimeSpan.FromMilliseconds(600);   // quick enough not to hold up a tab change

    // The spin's angle (0 to MascotTurn.TotalDegrees, three turns); every frame redraws Mochi from it (MascotTurn).
    private static readonly DependencyProperty SpinAngleProperty = DependencyProperty.Register(
        "SpinAngle", typeof(double), typeof(MascotControl), new PropertyMetadata(0.0, (d, e) => ((MascotControl)d).OnSpinAngle((double)e.NewValue)));

    /// <summary>
    /// Shows a new face: with a fast spin round the vertical axis (four turns in about a second, inside a whirlwind, the new face coming
    /// round for the last turn and a half) when <see cref="MascotSpinPolicy"/> says so and animations are on, otherwise at once. A face
    /// asked for during a spin is the one that comes round.
    /// </summary>
    private void RequestMood(MascotMood mood)
    {
        if (_spinning) { _pendingMood = mood; return; }
        if (SpinEnabled && AnimationsEnabled && IsLoaded && IsVisible && MascotSpinPolicy.ShouldSpin(_appliedMood, mood)) Spin(mood);
        else ApplyMood(mood);
        if (IsLoaded && IsVisible)
        {
            if (IsStill(mood)) { StopRenderingHook(); _cursorWatchTimer.Stop(); }
            else { StartRenderingHook(); }
        }
    }

    private void Spin(MascotMood mood)
    {
        _spinning = true;
        _swapped = false;
        _pendingMood = mood;
        StartWind();
        // Slow start, very fast middle, soft landing.
        var turn = new DoubleAnimation(0, MascotTurn.TotalDegrees, TurnDuration) { EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }, FillBehavior = FillBehavior.Stop };
        turn.Completed += (_, _) => { if (_spinning) EndSpin(landing: true); };
        BeginAnimation(SpinAngleProperty, turn);
    }

    // One frame of the spin: the body narrows but never goes flat, the face rides round and hides on his back (and fades a little at
    // full speed, a soft strobe instead of a flash), the shading moves, and the accessories fade out at the start and back in at the
    // end (hats do not tumble with him). The new face goes on at his back in the third turn.
    private void OnSpinAngle(double angle)
    {
        ApplyTurnFrame(MascotTurn.At(angle), MascotTurn.SpinBlur(angle));
        double t = angle / MascotTurn.TotalDegrees;
        if (WindFront.Visibility == Visibility.Visible) UpdateWind(t);
        ExtrasLayer.Opacity = t < 0.1 ? 1 - t / 0.1 : t > 0.9 ? (t - 0.9) / 0.1 : 0;
        if (_spinning && !_swapped && angle >= MascotTurn.FaceSwapAt)
        {
            _swapped = true;
            ApplyPendingKind();   // a mascot change: the new mascot comes round at his back, like a new face
            ApplyMood(_pendingMood);
        }
    }

    private void ApplyTurnFrame(MascotTurnFrame f, double blur = 0)
    {
        FlipScaleTransform.ScaleX = f.BodyScaleX;
        FlipScaleTransform.ScaleY = f.BodyScaleY;
        FaceTurnScale.ScaleX = f.FaceScaleX;
        FaceTurnShift.X = f.FaceShiftX;
        FaceLayer.Opacity = f.FaceOpacity * (1 - 0.55 * blur);
        MochiBodyGradient.StartPoint = new Point(0.3 + f.ShadeShift, 0);
        MochiBodyGradient.EndPoint = new Point(0.7 + f.ShadeShift, 1);
    }

    /// <summary>Ends a spin: Mochi faces front with the latest face; after a normal landing the wind settles and he gives a little squash.</summary>
    private void EndSpin(bool landing)
    {
        _spinning = false;
        BeginAnimation(SpinAngleProperty, null);
        ApplyTurnFrame(MascotTurn.At(0));
        ExtrasLayer.Opacity = 1;
        bool kindPending = _pendingKind != null;
        ApplyPendingKind();   // a spin cut short (hidden, unloaded) still ends on the chosen mascot
        if (!_swapped || kindPending || _appliedMood != _pendingMood) ApplyMood(_pendingMood);
        StopWind(fade: landing && AnimationsEnabled);
        if (landing) WobbleDizzy();
    }

    // ---- The whirlwind: anime brush strokes ----
    // Three rings stacked like a small tornado that opens upward (a small flat one at his feet, a tilted one at his middle, the
    // biggest round his head, tilted the other way), each made of tapered, glowing brush strokes orbiting him. Every frame the strokes
    // are drawn on their ellipse in screen space (so they stay fat where they pass in front, unlike a flattened circle), split into the
    // part behind him (WindBack: thinner, dimmer) and the part in front (WindFront: thicker), and moved round by the spin's own eased
    // progress, so the wind races in the fast middle. A curl sweeps in at the top left. Colours follow the theme: a silver-white core
    // with a champagne-gold glow on Default, a cream core with a matcha-green glow (and leaf-green inner strands) on Matcha.
    private sealed record WindRing(double CenterY, double RadiusX, double RadiusY, double Tilt, double Turns);

    private static readonly WindRing[] WindRings =
    {
        new(21.9, 11.5, 2.4, 0, 4),        // at his feet: smallest and flattest, fastest
        new(13.6, 14.0, 3.6, -8, -3),      // at his middle, turning the other way: the twist
        new(4.6, 15.6, 4.4, 11, 2.5),      // round his head: biggest, tilted the other way
    };

    // Strands of one ring: where they start on the ring (degrees), how far they run, the distance from the ring, their greatest width,
    // and whether they take the second (inner) colour. Wisps are the hair-thin flicks off the outer edge.
    private sealed record WindStrand(double Start, double Span, double Offset, double Width, bool Inner, bool Wisp);

    private static readonly WindStrand[] WindStrands =
    {
        new(0, 220, 0, 1.8, false, false),
        new(30, 160, -1.1, 1.1, true, false),
        new(215, 120, 0.8, 1.0, false, false),
        new(15, 26, 1.2, 0.45, false, true),
        new(150, 20, 1.4, 0.45, false, true),
        new(262, 24, 1.1, 0.45, false, true),
    };

    // One strand's four shapes: its glow and core, behind him and in front of him.
    private sealed record WindStroke(WindRing Ring, WindStrand Strand, System.Windows.Shapes.Path BackGlow, System.Windows.Shapes.Path BackCore,
        System.Windows.Shapes.Path FrontGlow, System.Windows.Shapes.Path FrontCore);

    private readonly List<WindStroke> _windStrokes = new();
    private readonly List<System.Windows.Shapes.Path> _windCurl = new();
    private RotateTransform? _curlSwing;
    private const double WindGlowScale = 2.4;

    private void BuildWind()
    {
        System.Windows.Shapes.Path Shape(bool glow) => new() { IsHitTestVisible = false, Effect = glow ? new System.Windows.Media.Effects.BlurEffect { Radius = 1.8 } : null };
        foreach (var ring in WindRings)
            foreach (var strand in WindStrands)
            {
                var stroke = new WindStroke(ring, strand, Shape(!strand.Wisp), Shape(false), Shape(!strand.Wisp), Shape(false));
                if (!strand.Wisp) { WindBack.Children.Add(stroke.BackGlow); WindFront.Children.Insert(0, stroke.FrontGlow); }
                WindBack.Children.Add(stroke.BackCore);
                WindFront.Children.Insert(strand.Wisp ? 0 : 1, stroke.FrontCore);
                _windStrokes.Add(stroke);
            }
        // The curl: a hooked stroke sweeping in round the top left of his head (in front).
        _curlSwing = new RotateTransform(0, 7, 0);
        var curl = new Canvas { IsHitTestVisible = false, RenderTransform = _curlSwing };
        var curlGlow = new System.Windows.Shapes.Path { Data = Spiral(7, 0, 6.2, 2.2, 190, 400, 2.8), Effect = new System.Windows.Media.Effects.BlurEffect { Radius = 1.8 } };
        var curlCore = new System.Windows.Shapes.Path { Data = Spiral(7, 0, 6.2, 2.2, 190, 400, 1.3) };
        curl.Children.Add(curlGlow);
        curl.Children.Add(curlCore);
        _windCurl.Add(curlGlow);
        _windCurl.Add(curlCore);
        WindFront.Children.Insert(0, curl);
        UpdateWind(0);
    }

    /// <summary>Redraws every brush stroke for a point of the spin (0 to 1): each ring has orbited that share of its turns.</summary>
    private void UpdateWind(double progress)
    {
        foreach (var s in _windStrokes)
        {
            double phase = s.Ring.Turns * 360 * progress + s.Strand.Start;
            var (back, front) = RingStroke(s.Ring, phase, s.Strand.Span, s.Strand.Offset, s.Strand.Width);
            s.BackCore.Data = back;
            s.FrontCore.Data = front;
            if (!s.Strand.Wisp)
            {
                var (backGlow, frontGlow) = RingStroke(s.Ring, phase, s.Strand.Span, s.Strand.Offset, s.Strand.Width * WindGlowScale);
                s.BackGlow.Data = backGlow;
                s.FrontGlow.Data = frontGlow;
            }
        }
    }

    // A tapered stroke along a tilted ellipse, in screen space: thickest a little after its head, sharp at both ends, thicker on the
    // near side; returned as the part behind him (far half of the ring) and the part in front (near half).
    private static (Geometry Back, Geometry Front) RingStroke(WindRing ring, double startDegrees, double spanDegrees, double offset, double width, int steps = 36)
    {
        double tilt = ring.Tilt * Math.PI / 180, cosT = Math.Cos(tilt), sinT = Math.Sin(tilt);
        double rx = ring.RadiusX + offset, ry = ring.RadiusY + offset * ring.RadiusY / ring.RadiusX;
        var back = new PathGeometry();
        var front = new PathGeometry();
        var outer = new List<Point>();
        var inner = new List<Point>();
        bool? runFront = null;
        void Flush()
        {
            if (outer.Count > 1 && runFront is { } isFront)
            {
                inner.Reverse();
                var figure = new PathFigure { StartPoint = outer[0], IsClosed = true, IsFilled = true };
                figure.Segments.Add(new PolyLineSegment(outer.Skip(1), false));
                figure.Segments.Add(new PolyLineSegment(inner, false));
                (isFront ? front : back).Figures.Add(figure);
            }
            outer.Clear();
            inner.Clear();
        }
        for (int i = 0; i <= steps; i++)
        {
            double u = (double)i / steps;
            double a = (startDegrees + spanDegrees * u) * Math.PI / 180, cos = Math.Cos(a), sin = Math.Sin(a);
            // The point and the tangent on the ellipse, tilted, placed round his body.
            double ex = rx * cos, ey = ry * sin, tx = -rx * sin, ty = ry * cos;
            double x = 15 + ex * cosT - ey * sinT, y = ring.CenterY + ex * sinT + ey * cosT;
            double dx = tx * cosT - ty * sinT, dy = tx * sinT + ty * cosT, length = Math.Max(1e-6, Math.Sqrt(dx * dx + dy * dy));
            double nx = -dy / length, ny = dx / length;
            bool isFront = sin > 0;   // the lower half of the ellipse is the side nearer the viewer
            double near = 0.72 + 0.5 * (sin + 1) / 2;
            double half = width / 2 * near * Math.Pow(Math.Sin(Math.PI * Math.Pow(u, 0.65)), 0.8);
            if (runFront is { } current && current != isFront)
            {
                // Close the run on the edge point so behind and in front meet without a gap.
                outer.Add(new Point(x + nx * half, y + ny * half));
                inner.Add(new Point(x - nx * half, y - ny * half));
                Flush();
            }
            runFront = isFront;
            outer.Add(new Point(x + nx * half, y + ny * half));
            inner.Add(new Point(x - nx * half, y - ny * half));
        }
        Flush();
        back.Freeze();
        front.Freeze();
        return (back, front);
    }

    // A brush stroke along a spiral round (cx, cy), from radius r0 to r1 between two angles: the curl.
    private static Geometry Spiral(double cx, double cy, double r0, double r1, double fromDegrees, double toDegrees, double width, int steps = 32)
    {
        var outer = new List<Point>(steps + 1);
        var inner = new List<Point>(steps + 1);
        for (int i = 0; i <= steps; i++)
        {
            double u = (double)i / steps;
            double a = (fromDegrees + (toDegrees - fromDegrees) * u) * Math.PI / 180, r = r0 + (r1 - r0) * u;
            double half = width / 2 * Math.Pow(Math.Sin(Math.PI * Math.Pow(u, 0.65)), 0.8);
            outer.Add(new Point(cx + (r + half) * Math.Cos(a), cy + (r + half) * Math.Sin(a)));
            inner.Add(new Point(cx + (r - half) * Math.Cos(a), cy + (r - half) * Math.Sin(a)));
        }
        inner.Reverse();
        var figure = new PathFigure { StartPoint = outer[0], IsClosed = true, IsFilled = true };
        figure.Segments.Add(new PolyLineSegment(outer.Skip(1), false));
        figure.Segments.Add(new PolyLineSegment(inner, false));
        var geometry = new PathGeometry(new[] { figure });
        geometry.Freeze();
        return geometry;
    }

    private void StartWind()
    {
        // The wind takes the colours of the mascot coming round: when the mascot itself changes, the one he turns into.
        var windKind = _pendingKind ?? _kind;
        bool electricWind = windKind == MochiDock.Core.MascotKind.Kuro;   // Kuro's whirlwind is electric yellow; the others follow their own colours
        bool matchaWind = windKind == MochiDock.Core.MascotKind.MatchaMochi, hojichaWind = windKind == MochiDock.Core.MascotKind.HojichaMochi;
        var core = Ink(electricWind ? "#FFF1B0" : matchaWind ? "#F5F0DF" : hojichaWind ? "#F6EBDC" : "#F4F4F8");
        var glow = Ink(electricWind ? "#CCFFD43B" : matchaWind ? "#CC4E6B36" : hojichaWind ? "#CCE0A96D" : "#CCD4BC8E");
        var inner = Ink(electricWind ? "#FFD43B" : matchaWind ? "#85B957" : hojichaWind ? "#E8C38A" : "#F3E2BD");
        var wisp = Ink(electricWind ? "#CC4A5190" : matchaWind ? "#CC4E6B36" : hojichaWind ? "#CCE8C38A" : "#CCEBEBF0");
        foreach (var s in _windStrokes)
        {
            var fill = s.Strand.Wisp ? wisp : s.Strand.Inner ? inner : core;
            s.FrontCore.Fill = fill;
            s.BackCore.Fill = fill;
            s.BackCore.Opacity = 0.7;              // the far side is dimmer
            s.FrontGlow.Fill = s.BackGlow.Fill = glow;
            s.BackGlow.Opacity = 0.6;
            if (s.Strand.Wisp) { s.FrontCore.BeginAnimation(OpacityProperty, Loop(1, 0.2, 110)); s.BackCore.BeginAnimation(OpacityProperty, Loop(0.6, 0.1, 130)); }
        }
        _windCurl[0].Fill = glow;
        _windCurl[1].Fill = core;
        DustA.Fill = DustB.Fill = DustC.Fill = Ink(electricWind ? "#55FFD43B" : _isMatcha ? "#554E6B36" : _isHojicha ? "#55E0A96D" : "#55D4BC8E");
        UpdateWind(0);

        WindBack.Visibility = WindFront.Visibility = Visibility.Visible;
        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(70));
        WindBack.BeginAnimation(OpacityProperty, fadeIn);
        WindFront.BeginAnimation(OpacityProperty, fadeIn);
        // The curl swings in over the spin; the funnel rises a little like a small tornado lifting.
        _curlSwing?.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(-75, 20, TurnDuration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        var rise = new DoubleAnimation(0.8, -1.5, TurnDuration) { EasingFunction = new SineEase { EasingMode = EasingMode.EaseOut } };
        WindBackRise.BeginAnimation(TranslateTransform.YProperty, rise);
        WindFrontRise.BeginAnimation(TranslateTransform.YProperty, rise);
        var puff = new DoubleAnimation(0.5, 1.45, TimeSpan.FromMilliseconds(450)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        DustScale.BeginAnimation(ScaleTransform.ScaleXProperty, puff);
        DustScale.BeginAnimation(ScaleTransform.ScaleYProperty, puff);
        DustPuffs.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(550)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } });
    }

    private void StopWind(bool fade)
    {
        void Clear()
        {
            WindBack.Visibility = WindFront.Visibility = Visibility.Collapsed;
            foreach (var s in _windStrokes) { s.FrontCore.BeginAnimation(OpacityProperty, null); s.BackCore.BeginAnimation(OpacityProperty, null); }
            _curlSwing?.BeginAnimation(RotateTransform.AngleProperty, null);
            WindBackRise.BeginAnimation(TranslateTransform.YProperty, null);
            WindFrontRise.BeginAnimation(TranslateTransform.YProperty, null);
            DustScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            DustScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            DustPuffs.BeginAnimation(OpacityProperty, null);
            WindBack.BeginAnimation(OpacityProperty, null);
            WindFront.BeginAnimation(OpacityProperty, null);
            WindBack.Opacity = WindFront.Opacity = 0;
        }
        if (!fade) { Clear(); return; }
        var fadeOut = new DoubleAnimation(0, TimeSpan.FromMilliseconds(150)) { FillBehavior = FillBehavior.HoldEnd };
        fadeOut.Completed += (_, _) => { if (!_spinning) Clear(); };
        WindBack.BeginAnimation(OpacityProperty, fadeOut);
        WindFront.BeginAnimation(OpacityProperty, fadeOut);
    }

    private enum HeadsetMood { Music, Gaming, Meeting }

    private string ThemeName => ThemeNameOf(_theme);

    // The light Matcha ground: Kuro shows no glow there (it would smear into a muddy halo on oat) and wears the soft black shadow instead.
    private bool _onLightGround => MochiDock.Core.MascotRim.IsLightGround(ThemeName);
    private bool _kuroGlows => _isKuro && !_onLightGround;

    private static string ThemeNameOf(MascotTheme theme) => theme switch
    {
        MascotTheme.Matcha => MochiDock.Core.ThemePolicy.Matcha,
        MascotTheme.Hojicha => MochiDock.Core.ThemePolicy.Hojicha,
        MascotTheme.KuroNight => MochiDock.Core.ThemePolicy.KuroNight,
        _ => MochiDock.Core.ThemePolicy.Default,
    };

    // The headset matches the mascot (MascotHeadset); the face is told by marks: red lights on the cups for the gaming faces, the
    // microphone for a meeting. The cats wear the band behind their ears (HeadbandBackLayer), Mochi in front.
    // Shows the headset of a face. The cats wear none (a human headset sits badly on ears): their Music, Gaming and Meeting faces have a
    // badge instead (MascotBadge.For(mood, kind)), so every headset layer stays hidden for them.
    private void ShowHeadset(HeadsetMood mood)
    {
        ApplyHeadset(mood);
        if (_isCat)
        {
            HeadphonesLayer.Visibility = MicBoom.Visibility = Visibility.Collapsed;
            return;
        }
        HeadphonesLayer.Visibility = Visibility.Visible;
        if (mood == HeadsetMood.Meeting) MicBoom.Visibility = Visibility.Visible;
    }

    private void ApplyHeadset(HeadsetMood mood)    {
        var h = MochiDock.Core.MascotHeadset.For(_kind, ThemeName);
        var band = Ink(h.Band);
        var rim = Ink(h.BandRim);
        HeadbandArc.Stroke = HeadbandBack.Stroke = band;
        HeadbandRim.Stroke = HeadbandBackRim.Stroke = rim;
        HeadbandArc.Visibility = HeadbandRim.Visibility = _isCat ? Visibility.Collapsed : Visibility.Visible;
        HeadbandBackLayer.Opacity = _isCat ? 1 : 0;
        EarCupLeft.Background = EarCupRight.Background = Ink(h.Cup);
        var ring = Ink(h.CupRing);
        EarCupLeft.BorderBrush = EarCupRight.BorderBrush = ring;
        HeadsetLedLeft.Visibility = HeadsetLedRight.Visibility = mood == HeadsetMood.Gaming ? Visibility.Visible : Visibility.Collapsed;
        MicBoomArm.Stroke = ring;
        MicBoomTip.Fill = ring;
    }

    // Gaming: a determined face. The eyes are cut slanted at the top (the cartoon way to look focused; Mochi has no eyebrows and no mouth),
    // a cool slate tint and a red gaming headset.
    // Still on purpose: ApplyBaseMood has already frozen the gaze, and these eyes do not blink.
    private void ApplyFocusedFace()
    {
        SetTint(new[] { "#E3E8F2", "#C3CCE0", "#5B6B8C", "#40FF7896" }, new[] { "#9DB8A9", "#7A9A8A", "#3F6656", "#40FF7896" }, animate: false);
        ShowEyes(Eyes.Determ);
        ShowHeadset(HeadsetMood.Gaming);
    }

    private void ResetExtras()
    {
        foreach (UIElement element in new UIElement[]
        {
            TearDrop, TearDropRight, StartleLines, ConfettiGroup,
            LeftDetermEye, RightDetermEye, LeftFlatEye, RightFlatEye, LeftDroopEye, RightDroopEye, LeftSpiralEye, RightSpiralEye,
            LeftWideEye, RightWideEye, LeftRoundEye, RightRoundEye, LeftLidEye, RightLidEye,
            IcicleGroup, StarsGroup,
            StarEyes, CoinEyes, Sunglasses, BoltOrbit, RainGroup, ScarfGroup,
            PartyHat, MicBoom, StatusBadge,
        }) element.Visibility = Visibility.Collapsed;
        LeftPillShift.X = RightPillShift.X = LeftPillShift.Y = RightPillShift.Y = 0;
        BadgeScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        BadgeScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        BadgeScale.ScaleX = BadgeScale.ScaleY = 1;

        BoltOrbitRotate.BeginAnimation(RotateTransform.AngleProperty, null);
        BoltOrbitRotate.Angle = 0;
        foreach (var bolt in new[] { BoltA, BoltB, BoltC }) { bolt.BeginAnimation(OpacityProperty, null); bolt.Opacity = 1; }
        RainT.BeginAnimation(TranslateTransform.YProperty, null);
        RainGroup.BeginAnimation(OpacityProperty, null);
        RainT.Y = 0;
        RainGroup.Opacity = 1;
        ApplyHeadset(HeadsetMood.Music);   // the plain matching headset; the gaming faces add red lights, a meeting the microphone
        TearRightT.BeginAnimation(TranslateTransform.YProperty, null);
        TearDropRight.BeginAnimation(OpacityProperty, null);
        TearRightT.Y = 0;
        TearDropRight.Opacity = 1;
        ConfettiT.BeginAnimation(TranslateTransform.YProperty, null);
        ConfettiGroup.BeginAnimation(OpacityProperty, null);
        ConfettiGroup.Opacity = 1;
        ConfettiT.Y = 0;
        TearT.BeginAnimation(TranslateTransform.YProperty, null);
        TearDrop.BeginAnimation(OpacityProperty, null);
        TearT.Y = 0;
        TearDrop.Opacity = 1;
        SweatTransform.BeginAnimation(TranslateTransform.YProperty, null);
        SweatDrop.BeginAnimation(OpacityProperty, null);
        LeftSpiralRotate.BeginAnimation(RotateTransform.AngleProperty, null);
        RightSpiralRotate.BeginAnimation(RotateTransform.AngleProperty, null);
        foreach (var star in new[] { StarA, StarB, StarC }) star.BeginAnimation(OpacityProperty, null);
        BodyBounceTransform.BeginAnimation(TranslateTransform.XProperty, null);
        BodyBounceTransform.X = 0;
        SweatTransform.Y = 0;
        SweatDrop.Opacity = 1;
        StarA.Opacity = StarB.Opacity = StarC.Opacity = 1;
        _electricFace = false;
        _kuroSparkWanted = false;
        TailSpark.BeginAnimation(OpacityProperty, null);
        TailSpark.Opacity = KuroSparkRest;
        SetThemeBaseTint();
    }

    // Body colours: the gradient eases to the new tint; the edge and the cheeks switch at once.
    private void SetTint(string top, string bottom, string edge, string cheek, bool animate = true)
    {
        // Kuro: the body is always charcoal; the face's own colour goes into the rim and the glow instead.
        KuroTint? kuro = null;
        if (_isKuro)
        {
            kuro = MochiDock.Core.MascotTint.KuroNight(new[] { top, bottom, edge, cheek });
            if (_onLightGround) kuro = MochiDock.Core.MascotTint.OnLightGround(kuro.Value);   // no glow on Matcha: a deep gold rim instead
            top = kuro.Value.BodyTop;
            bottom = kuro.Value.BodyBottom;
        }
        else if (_isShiro)
        {
            // Shiro: Mochi's normal tints on every theme with a white body for the neutral faces; the Electric face stays white too.
            var shiro = MochiDock.Core.ShiroTint.For(new[] { top, bottom, edge, cheek }, ThemeName, forceNeutral: _electricFace);
            (top, bottom, edge, cheek) = (shiro.Top, shiro.Bottom, shiro.Edge, shiro.Cheek);
        }
        // Grey Mochi, Hojicha Mochi and Shiro on the Matcha ground: a darker rim (and a thicker one, see ApplyCatParts) so they stay visible there.
        if (!_isKuro && MochiDock.Core.MascotRim.NeedsDarkRim(_kind, ThemeName)) edge = MochiDock.Core.MascotRim.OnLightGround(edge);
        var topColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(top);
        var bottomColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(bottom);
        BodyGradStopTop.BeginAnimation(GradientStop.ColorProperty, null);
        BodyGradStopBottom.BeginAnimation(GradientStop.ColorProperty, null);
        if (animate && AnimationsEnabled && IsLoaded)   // a control that is not shown yet takes the colour at once
        {
            BodyGradStopTop.BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(topColor, TimeSpan.FromMilliseconds(300)));
            BodyGradStopBottom.BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(bottomColor, TimeSpan.FromMilliseconds(300)));
        }
        else
        {
            BodyGradStopTop.Color = topColor;
            BodyGradStopBottom.Color = bottomColor;
        }
        if (kuro is KuroTint look) { ApplyKuroLook(look); return; }
        MochiBodyBorder.BorderBrush = Ink(edge);
        LeftCheek.Fill = Ink(cheek);
        RightCheek.Fill = Ink(cheek);
        if (_isShiro)
        {
            // The ears and the tail take the body's top colour and its edge colour, so a red Angry Shiro has red ears too.
            EarLeft.Fill = EarRight.Fill = TailShape.Fill = Ink(top);
            EarLeft.Stroke = EarRight.Stroke = TailShape.Stroke = Ink(edge);
        }
    }

    // The plain starting point of every extra face: pill eyes visible and the accessories hidden.
    private void ResetEyesForExtraMood()
    {
        ShowEyes(Eyes.Pill);
        HeadphonesLayer.Visibility = Visibility.Collapsed;
        SweatDrop.Visibility = Visibility.Collapsed;
        BodyBounceTransform.BeginAnimation(TranslateTransform.YProperty, null);
    }

    // The eye shapes a face can wear (all drawn with MascotEyeBrush, so Kuro's yellow ink works). The overlays for Electric (star eyes), Money
    // (coin eyes) and Sunny (sunglasses) are separate canvases that the face shows on top of None.
    private enum Eyes { Pill, Happy, Sleepy, Determ, Flat, Droop, Spiral, Wide, Round, Lid, None }

    private void ShowEyes(Eyes kind)
    {
        static Visibility V(bool on) => on ? Visibility.Visible : Visibility.Collapsed;
        LeftPillEye.Visibility = RightPillEye.Visibility = V(kind == Eyes.Pill);
        LeftHappyEye.Visibility = RightHappyEye.Visibility = V(kind == Eyes.Happy);
        LeftSleepyEye.Visibility = RightSleepyEye.Visibility = V(kind == Eyes.Sleepy);
        LeftDetermEye.Visibility = RightDetermEye.Visibility = V(kind == Eyes.Determ);
        LeftFlatEye.Visibility = RightFlatEye.Visibility = V(kind == Eyes.Flat);
        LeftDroopEye.Visibility = RightDroopEye.Visibility = V(kind == Eyes.Droop);
        LeftSpiralEye.Visibility = RightSpiralEye.Visibility = V(kind == Eyes.Spiral);
        LeftWideEye.Visibility = RightWideEye.Visibility = V(kind == Eyes.Wide);
        LeftRoundEye.Visibility = RightRoundEye.Visibility = V(kind == Eyes.Round);
        LeftLidEye.Visibility = RightLidEye.Visibility = V(kind == Eyes.Lid);
    }

    private static DoubleAnimation Loop(double from, double to, int milliseconds, bool reverse = true, IEasingFunction? easing = null) => new(from, to, TimeSpan.FromMilliseconds(milliseconds))
    {
        AutoReverse = reverse,
        RepeatBehavior = RepeatBehavior.Forever,
        EasingFunction = easing,
    };

    // ---------------- The status badge ----------------
    // One disc in the top-right corner (MascotBadge says which glyph and colour each face gets). The glyph shapes are filled paths drawn for
    // about 7 px, in a box of about -3..3 units around the disc's centre.
    private static readonly Dictionary<string, Geometry> BadgeGlyphs = new()
    {
        ["exclaim"] = Glyph("M -0.85,-2.9 L 0.85,-2.9 L 0.6,0.9 L -0.6,0.9 Z M 0,1.55 A 0.95,0.95 0 1 1 0,3.45 A 0.95,0.95 0 1 1 0,1.55 Z"),
        ["z"] = Glyph("M -2,-2.3 L 2,-2.3 L 2,-1.2 L -0.5,1.3 L 2.1,1.3 L 2.1,2.5 L -2.1,2.5 L -2.1,1.4 L 0.4,-1.1 L -2,-1.1 Z"),
        ["moon"] = Glyph("M 0.7,-3 A 3.1,3.1 0 1 0 2.8,1.2 A 2.5,2.5 0 0 1 0.7,-3 Z"),
        ["cup"] = Glyph("M -2.7,-2 L 1.2,-2 L 1.2,2.6 L -2.7,2.6 Z M 1.2,-1.1 L 3,-1.1 L 3,1.5 L 1.2,1.5 L 1.2,0.6 L 2.1,0.6 L 2.1,-0.2 L 1.2,-0.2 Z"),
        ["bell"] = Glyph("M 0,-3 C -1.9,-3 -2.4,-1.3 -2.4,0.2 L -2.4,1.2 L -3.1,2 L 3.1,2 L 2.4,1.2 L 2.4,0.2 C 2.4,-1.3 1.9,-3 0,-3 Z M -1,2.5 L 1,2.5 A 1,1 0 0 1 -1,2.5 Z"),
        ["pause"] = Glyph("M -2.1,-2.4 L -0.6,-2.4 L -0.6,2.4 L -2.1,2.4 Z M 0.6,-2.4 L 2.1,-2.4 L 2.1,2.4 L 0.6,2.4 Z"),
        ["trophy"] = Glyph("M -2.3,-2.8 L 2.3,-2.8 L 2.3,0 C 2.3,1.4 1.3,1.9 0,1.9 C -1.3,1.9 -2.3,1.4 -2.3,0 Z M -3.3,-2.2 L -2.3,-2.2 L -2.3,-0.5 L -3.3,-0.5 Z M 3.3,-2.2 L 2.3,-2.2 L 2.3,-0.5 L 3.3,-0.5 Z M -0.7,1.7 L 0.7,1.7 L 0.7,2.2 L 1.9,2.2 L 1.9,3.1 L -1.9,3.1 L -1.9,2.2 L -0.7,2.2 Z"),
        ["clock"] = Glyph("F0 M 0,-3 A 3,3 0 1 1 0,3 A 3,3 0 1 1 0,-3 Z M 0,-2.1 A 2.1,2.1 0 1 0 0,2.1 A 2.1,2.1 0 1 0 0,-2.1 Z M -0.45,-1.5 L 0.45,-1.5 L 0.45,0.3 L 1.5,0.3 L 1.5,1.1 L -0.45,1.1 Z"),
        ["battery"] = Glyph("F0 M -3,-1.9 L 2.1,-1.9 L 2.1,1.9 L -3,1.9 Z M -2.2,-1.1 L 1.3,-1.1 L 1.3,1.1 L -2.2,1.1 Z M -2,-0.8 L -1.1,-0.8 L -1.1,0.8 L -2,0.8 Z M 2.1,-0.9 L 3.1,-0.9 L 3.1,0.9 L 2.1,0.9 Z"),
        ["up"] = Glyph("M 0,-2.8 L 2.9,2.2 L -2.9,2.2 Z"),
        ["down"] = Glyph("M 0,2.8 L 2.9,-2.2 L -2.9,-2.2 Z"),
        ["sun"] = Glyph("M 0,-1.6 A 1.6,1.6 0 1 1 0,1.6 A 1.6,1.6 0 1 1 0,-1.6 Z M -0.4,-3.1 L 0.4,-3.1 L 0.4,-2.2 L -0.4,-2.2 Z M -0.4,2.2 L 0.4,2.2 L 0.4,3.1 L -0.4,3.1 Z M -3.1,-0.4 L -2.2,-0.4 L -2.2,0.4 L -3.1,0.4 Z M 2.2,-0.4 L 3.1,-0.4 L 3.1,0.4 L 2.2,0.4 Z M 1.6,-2.5 L 2.5,-1.6 L 1.9,-1.0 L 1.0,-1.9 Z M -1.6,-2.5 L -2.5,-1.6 L -1.9,-1.0 L -1.0,-1.9 Z M 1.6,2.5 L 2.5,1.6 L 1.9,1.0 L 1.0,1.9 Z M -1.6,2.5 L -2.5,1.6 L -1.9,1.0 L -1.0,1.9 Z"),
        ["umbrella"] = Glyph("M -3,0.4 A 3,3 0 0 1 3,0.4 Z M -0.25,0.4 L 0.25,0.4 L 0.25,2.3 L -0.25,2.3 Z M -1.3,2.3 L 0.25,2.3 L 0.25,1.9 L -0.9,1.9 L -0.9,2.9 L -1.3,2.9 Z"),
        ["snow"] = Glyph("M -3,-0.6 L 3,-0.6 L 3,0.6 L -3,0.6 Z M 1.02,2.87 L 1.98,2.32 L -1.02,-2.87 L -1.98,-2.32 Z M -1.02,2.87 L -1.98,2.32 L 1.02,-2.87 L 1.98,-2.32 Z"),
        ["cloud"] = Glyph("M -2.7,1.7 A 1.6,1.6 0 0 1 -2.1,-1.2 A 2.1,2.1 0 0 1 1.9,-1.4 A 1.7,1.7 0 0 1 2.7,1.7 Z"),
        ["timer"] = Glyph("F0 M 0,-2.3 A 2.6,2.6 0 1 1 0,2.9 A 2.6,2.6 0 1 1 0,-2.3 Z M 0,-1.5 A 1.8,1.8 0 1 0 0,2.1 A 1.8,1.8 0 1 0 0,-1.5 Z M -0.35,-1.1 L 0.35,-1.1 L 0.35,0.8 L -0.35,0.8 Z M -0.8,-3.4 L 0.8,-3.4 L 0.8,-2.6 L -0.8,-2.6 Z"),
        ["tick"] = Glyph("M -2.8,0.2 L -1.7,-0.9 L -0.6,0.2 L 1.8,-2.4 L 2.9,-1.3 L -0.6,2.4 Z"),
        ["note"] = Glyph("M 0.6,-3 L 1.8,-3 L 1.8,2 L 0.6,2 Z M 1.8,-3 L 3.2,-2 L 3.2,-0.6 L 1.8,-1.6 Z M -0.5,0.8 A 1.7,1.3 0 1 1 -0.5,3.4 A 1.7,1.3 0 1 1 -0.5,0.8 Z"),
        ["controller"] = Glyph("F0 M -3,-0.6 C -3,-1.8 -2.2,-2.3 -1.4,-2.3 L 1.4,-2.3 C 2.2,-2.3 3,-1.8 3,-0.6 L 3.3,1.7 C 3.4,2.6 2.9,3 2.3,3 C 1.7,3 1.4,2.7 1.1,2.2 L 0.7,1.6 L -0.7,1.6 L -1.1,2.2 C -1.4,2.7 -1.7,3 -2.3,3 C -2.9,3 -3.4,2.6 -3.3,1.7 Z M -1.9,-0.35 L -1.25,-0.35 L -1.25,-1 L -0.75,-1 L -0.75,-0.35 L -0.1,-0.35 L -0.1,0.15 L -0.75,0.15 L -0.75,0.8 L -1.25,0.8 L -1.25,0.15 L -1.9,0.15 Z M 1.6,-0.7 A 0.5,0.5 0 1 1 0.6,-0.7 A 0.5,0.5 0 1 1 1.6,-0.7 Z M 2.4,0.3 A 0.5,0.5 0 1 1 1.4,0.3 A 0.5,0.5 0 1 1 2.4,0.3 Z"),
        ["mic"] = Glyph("M -1.2,-1.9 A 1.2,1.2 0 0 1 1.2,-1.9 L 1.2,0.3 A 1.2,1.2 0 0 1 -1.2,0.3 Z M -2.4,-0.5 L -1.7,-0.5 A 1.7,1.7 0 0 0 1.7,-0.5 L 2.4,-0.5 A 2.4,2.4 0 0 1 -2.4,-0.5 Z M -0.4,1.8 L 0.4,1.8 L 0.4,2.7 L -0.4,2.7 Z M -1.6,2.6 L 1.6,2.6 L 1.6,3.2 L -1.6,3.2 Z"),
    };

    private static Geometry Glyph(string data)
    {
        var g = Geometry.Parse(data);
        g.Freeze();
        return g;
    }

    // Shows the badge of the face (none for the faces the eyes tell). Notice and New message pop it in once and pulse it once, no loop.
    private void ApplyBadge(MascotMood mood)
    {
        if (MochiDock.Core.MascotBadge.For(mood, _kind) is not { } b) { StatusBadge.Visibility = Visibility.Collapsed; return; }
        BadgeDisc.Fill = Ink(b.Fill);
        BadgeGlyph.Fill = Ink(b.Ink);
        BadgeRing.Fill = Ink(MochiDock.Core.MascotBadge.RingFor(ThemeName, b.Fill));
        BadgeGlyph.Data = BadgeGlyphs.TryGetValue(b.Glyph, out var glyph) ? glyph : Geometry.Empty;
        // The Mochi kinds: the top-right corner. The cats: the top centre, between the ears, a little smaller (radius 4.4 with its ring).
        BadgePos.X = _isCat ? 15 : 25.2;
        BadgePos.Y = _isCat ? 0.5 : 3.8;
        BadgeSize.ScaleX = BadgeSize.ScaleY = _isCat ? 0.9 : 1;
        StatusBadge.Visibility = Visibility.Visible;
        if (mood is MascotMood.Alert or MascotMood.Excited && AnimationsEnabled && IsLoaded && IsVisible) PopBadge();
    }

    private void PopBadge()
    {
        foreach (var property in new[] { ScaleTransform.ScaleXProperty, ScaleTransform.ScaleYProperty })
        {
            var pop = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
            foreach (var (ms, value) in new[] { (0, 0.0), (150, 1.15), (250, 1.0), (420, 1.14), (560, 1.0) })
                pop.KeyFrames.Add(new EasingDoubleKeyFrame(value, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms)), new SineEase { EasingMode = EasingMode.EaseOut }));
            BadgeScale.BeginAnimation(property, pop);
        }
    }

    private void ApplyExtraMood(MascotMood mood)
    {
        ResetEyesForExtraMood();
        bool animate = AnimationsEnabled;
        if (IsStill(mood))
        {
            FaceTransform.X = 0;
            FaceTransform.Y = 0;
            _curX = _curY = _targetX = _targetY = 0;
        }
        switch (mood)
        {
            case MascotMood.Angry:
                // Overdue task: sharp slanted eyes on the red body, and the red clock badge. The body trembles a little.
                SetTint("#F8C9C2", "#EFA79C", "#C0392B", "#99E24B4A");
                ShowEyes(Eyes.Determ);
                if (animate) BodyBounceTransform.BeginAnimation(TranslateTransform.XProperty, Loop(-0.4, 0.4, 110));
                break;
            case MascotMood.Hot:
                // Hot: an orange body, flat eyes and a sweat drop.
                SetTint("#F9D2B2", "#F2B089", "#D9622B", "#99E24B4A");
                ShowEyes(Eyes.Flat);
                SweatDrop.Visibility = Visibility.Visible;
                if (animate)
                {
                    SweatTransform.BeginAnimation(TranslateTransform.YProperty, Loop(0, 4, 1000, reverse: false));
                    SweatDrop.BeginAnimation(OpacityProperty, Loop(1, 0.1, 1000, reverse: false));
                }
                break;
            case MascotMood.Freezing:
                // Freezing: a blue body, icicles hanging from the top edge and a shiver.
                SetTint(new[] { "#9CCBEA", "#72AEDB", "#3F7FB5", "#997FB5E8" }, new[] { "#9CCBEA", "#72AEDB", "#3F7FB5", "#997FB5E8" });
                IcicleGroup.Visibility = Visibility.Visible;
                if (animate) BodyBounceTransform.BeginAnimation(TranslateTransform.XProperty, Loop(-0.7, 0.7, 60));
                break;
            case MascotMood.Dizzy:
                SetTint(new[] { "#B9AEE0", "#9C8FD0", "#6F63B8", "#66B9A6E6" }, new[] { "#B9AEE0", "#9C8FD0", "#6F63B8", "#66B9A6E6" });
                ShowEyes(Eyes.Spiral);
                StarsGroup.Visibility = Visibility.Visible;
                if (animate)
                {
                    LeftSpiralRotate.BeginAnimation(RotateTransform.AngleProperty, Loop(0, 360, 1200, reverse: false));
                    RightSpiralRotate.BeginAnimation(RotateTransform.AngleProperty, Loop(0, 360, 1200, reverse: false));
                    StarA.BeginAnimation(OpacityProperty, Loop(1, 0.2, 500));
                    StarB.BeginAnimation(OpacityProperty, Loop(0.2, 1, 500));
                    StarC.BeginAnimation(OpacityProperty, Loop(1, 0.2, 650));
                }
                break;
            case MascotMood.Victory:
                // Won the match: a gold body, happy arc eyes and the gold trophy badge (Shiro keeps white with a gold rim).
                SetTint(new[] { "#FCEFC4", "#F4D98A", "#D9A21C", "#99FF7896" }, new[] { "#F2D56A", "#E6BC3A", "#B58A10", "#99FF7896" });
                ShowEyes(Eyes.Happy);
                if (animate) BodyBounceTransform.BeginAnimation(TranslateTransform.YProperty, Loop(0, -1.5, 300, easing: new SineEase { EasingMode = EasingMode.EaseOut }));
                break;
            case MascotMood.Defeat:
                // Lost the match: a cool grey-blue tint, sad drooping eyes and a tear from each eye; the body sinks a little.
                SetTint(new[] { "#9FB0CC", "#7F93B5", "#566A8E", "#40FF7896" }, new[] { "#9FB0CC", "#7F93B5", "#566A8E", "#40FF7896" });
                ShowEyes(Eyes.Droop);
                TearDrop.Visibility = TearDropRight.Visibility = Visibility.Visible;
                if (animate)
                {
                    TearT.BeginAnimation(TranslateTransform.YProperty, Loop(0, 3.2, 1400, reverse: false));
                    TearDrop.BeginAnimation(OpacityProperty, Loop(1, 0.1, 1400, reverse: false));
                    TearRightT.BeginAnimation(TranslateTransform.YProperty, Loop(0, 3.2, 1400, reverse: false));
                    TearDropRight.BeginAnimation(OpacityProperty, Loop(1, 0.1, 1400, reverse: false));
                    BodyBounceTransform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, 1.2, TimeSpan.FromMilliseconds(700)) { FillBehavior = FillBehavior.HoldEnd });
                }
                break;
            case MascotMood.Excited:
                // A new message: wide eyes, a bouncing blue body and the red "!" badge that pops in.
                SetTint(new[] { "#8FB0F2", "#6E94E6", "#3F62C8", "#80FF7896" }, new[] { "#8FB0F2", "#6E94E6", "#3F62C8", "#80FF7896" });
                ShowEyes(Eyes.Wide);
                if (animate) BodyBounceTransform.BeginAnimation(TranslateTransform.YProperty, Loop(0, -2, 260, easing: new SineEase { EasingMode = EasingMode.EaseOut }));
                break;
            case MascotMood.Surprised:
                // Something just arrived: round "O" eyes and bold startle lines at the top corners; one jump.
                SetTint(new[] { "#FFF6E0", "#F5E2B8", "#D9B35C", "#80FF7896" }, new[] { "#F2D98A", "#E5C05E", "#B8902A", "#80FF7896" });
                ShowEyes(Eyes.Round);
                StartleLines.Visibility = Visibility.Visible;
                if (animate) BodyBounceTransform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, -3, TimeSpan.FromMilliseconds(180)) { AutoReverse = true, EasingFunction = new SineEase { EasingMode = EasingMode.EaseOut } });
                break;
            case MascotMood.Cheering:
                // A cheer (a Pomodoro finished): the happy face with confetti falling at the sides of the head.
                SetTint(new[] { "#FFE4EC", "#F7B8CC", "#E0709A", "#99FF7896" }, new[] { "#F2A9C4", "#E98AAE", "#C25581", "#99FF7896" });
                ShowEyes(Eyes.Happy);
                ConfettiGroup.Visibility = Visibility.Visible;
                if (animate)
                {
                    ConfettiT.BeginAnimation(TranslateTransform.YProperty, Loop(0, 4, 900, reverse: false));
                    ConfettiGroup.BeginAnimation(OpacityProperty, Loop(1, 0.2, 900, reverse: false));
                    BodyBounceTransform.BeginAnimation(TranslateTransform.YProperty, Loop(0, -1.6, 320, easing: new SineEase { EasingMode = EasingMode.EaseOut }));
                }
                break;
            case MascotMood.Praying:
                // The prayer time: calm closed eyes, a warm tint and the teal reminder bell.
                SetTint(new[] { "#FFF3DC", "#F6DFA8", "#C9A24A", "#66FF7896" }, new[] { "#E8D08A", "#D6B866", "#A8873A", "#66FF7896" });
                ShowEyes(Eyes.Sleepy);
                if (animate) BodyBounceTransform.BeginAnimation(TranslateTransform.YProperty, Loop(0, 1.2, 2400, easing: new SineEase { EasingMode = EasingMode.EaseInOut }));
                break;
            case MascotMood.Bored:
                // Lonely (nobody has touched the PC for a long while): a faded body and heavy half-lidded eyes looking down and aside.
                SetTint(new[] { "#EBEBEE", "#D3D4DA", "#A9ABB4", "#40FF7896" }, new[] { "#B3C79F", "#98AE86", "#647D55", "#40FF7896" });
                ShowEyes(Eyes.Lid);
                FaceTransform.X = 1.7;
                FaceTransform.Y = 1.2;
                if (animate) BodyBounceTransform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(900)) { FillBehavior = FillBehavior.HoldEnd });
                break;
            case MascotMood.Waking:
                // Just woke up: half-lidded eyes and a slow stretch (no longer the same as sleeping).
                SetTint(new[] { "#F0EAF7", "#D9CDEA", "#8E7BC4", "#80FF7896" }, new[] { "#A7C58A", "#8BAA70", "#5B7E48", "#80FF7896" });
                ShowEyes(Eyes.Lid);
                if (animate) BodyBounceTransform.BeginAnimation(TranslateTransform.YProperty, Loop(0, -1.4, 1500, easing: new SineEase { EasingMode = EasingMode.EaseInOut }));
                break;
            case MascotMood.Worried:
                // Gaming, but it is going badly (the frame rate fell or the ping is high): drooping eyes, a sweat drop on the side of the head and the
                // matching headset with red lights.
                SetTint(new[] { "#E3E8F2", "#C3CCE0", "#5B6B8C", "#40FF7896" }, new[] { "#9DB8A9", "#7A9A8A", "#3F6656", "#40FF7896" });
                ShowEyes(Eyes.Droop);
                SweatDrop.Visibility = Visibility.Visible;
                ShowHeadset(HeadsetMood.Gaming);
                if (animate)
                {
                    SweatTransform.BeginAnimation(TranslateTransform.YProperty, Loop(0, 3, 1000, reverse: false));
                    SweatDrop.BeginAnimation(OpacityProperty, Loop(1, 0.1, 1000, reverse: false));
                }
                break;
            case MascotMood.BreakTime:
                // The break reminder: awake, a slow stretch and the green pause badge (Coffee has the cup).
                SetTint(new[] { "#FBEBD3", "#F3D3A6", "#C98A5B", "#80FF7896" }, new[] { "#F0C27A", "#E3A456", "#B07430", "#80FF7896" });
                if (animate) BodyBounceTransform.BeginAnimation(TranslateTransform.YProperty, Loop(0, -1.2, 1400, easing: new SineEase { EasingMode = EasingMode.EaseInOut }));
                break;
            case MascotMood.Coffee:
                // Resting while you work: awake, a gentle sway and the brown cup badge. No headset, no tint.
                if (animate) BodyBounceTransform.BeginAnimation(TranslateTransform.YProperty, Loop(0, -0.8, 1800, easing: new SineEase { EasingMode = EasingMode.EaseInOut }));
                break;
            case MascotMood.Electric:
                // The Stats Battery page: a charged yellow Mochi with star eyes and lightning bolts circling and flickering.
                // Kuro stays charcoal (never yellow): his glow is brighter instead, and his tail spark flickers with the bolts.
                _electricFace = true;
                _kuroSparkWanted = true;
                SetTint(new[] { "#FFF4C2", "#F5D96B", "#C9A227", "#66FFB347" }, new[] { "#F3E7A8", "#DCC45E", "#A88A1E", "#66FFB347" });
                UpdateKuroSpark();
                ShowEyes(Eyes.None);
                StarEyes.Visibility = BoltOrbit.Visibility = Visibility.Visible;
                if (animate)
                {
                    BoltOrbitRotate.BeginAnimation(RotateTransform.AngleProperty, Loop(0, 360, 2400, reverse: false));
                    BoltA.BeginAnimation(OpacityProperty, Loop(1, 0.35, 300));
                    BoltB.BeginAnimation(OpacityProperty, Loop(0.35, 1, 420));
                    BoltC.BeginAnimation(OpacityProperty, Loop(1, 0.35, 360));
                }
                break;
            case MascotMood.Money:
                // FX with the rate up or flat: coin eyes and a green up arrow badge.
                ShowEyes(Eyes.None);
                CoinEyes.Visibility = Visibility.Visible;
                if (animate) BodyBounceTransform.BeginAnimation(TranslateTransform.YProperty, Loop(0, -0.8, 700, easing: new SineEase { EasingMode = EasingMode.EaseInOut }));
                break;
            case MascotMood.MoneyDown:
                // FX with the rate down: the same coin eyes on a cooler body, a red down arrow badge and the body sinking a little.
                SetTint(new[] { "#DCE3EE", "#BCC8DA", "#7D8FAE", "#40FF7896" }, new[] { "#A9B8A0", "#8C9D82", "#5E6E55", "#40FF7896" });
                ShowEyes(Eyes.None);
                CoinEyes.Visibility = Visibility.Visible;
                if (animate) BodyBounceTransform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, 0.8, TimeSpan.FromMilliseconds(600)) { FillBehavior = FillBehavior.HoldEnd });
                break;
            case MascotMood.Sunny:
                // Weather, sunny: sunglasses on a warm body and the sun badge.
                SetTint(new[] { "#FFF6E0", "#F5E2B8", "#D9B35C", "#80FF7896" }, new[] { "#F2D98A", "#E5C05E", "#B8902A", "#80FF7896" });
                ShowEyes(Eyes.None);
                Sunglasses.Visibility = Visibility.Visible;
                break;
            case MascotMood.Rainy:
                // Weather, rain: a blue body, rain drops falling at the sides and the umbrella badge.
                SetTint(new[] { "#E3ECF7", "#C3D3EA", "#7F98BD", "#40FF7896" }, new[] { "#A9C1CC", "#8AA6B4", "#5A7786", "#40FF7896" });
                RainGroup.Visibility = Visibility.Visible;
                if (animate)
                {
                    RainT.BeginAnimation(TranslateTransform.YProperty, Loop(0, 5, 900, reverse: false));
                    RainGroup.BeginAnimation(OpacityProperty, Loop(1, 0.1, 900, reverse: false));
                }
                break;
            case MascotMood.Snowy:
                // Weather, snow: a red scarf and the snowflake badge.
                ScarfGroup.Visibility = Visibility.Visible;
                break;
            case MascotMood.Cloudy:
                // Weather, cloud or fog (or a clear night): the cloud badge.
                break;
            case MascotMood.Studying:
                // A Pomodoro focus session: half-lidded, concentrating eyes and the red timer badge.
                ShowEyes(Eyes.Lid);
                break;
            case MascotMood.Yawning:
                // Up late: heavy half-lidded eyes and the navy moon badge; a slow, sleepy sway.
                ShowEyes(Eyes.Lid);
                if (animate) BodyBounceTransform.BeginAnimation(TranslateTransform.YProperty, Loop(0, 1, 2600, easing: new SineEase { EasingMode = EasingMode.EaseInOut }));
                break;
            case MascotMood.Birthday:
                // The user's birthday: happy eyes and a small party hat all day.
                ShowEyes(Eyes.Happy);
                PartyHat.Visibility = Visibility.Visible;
                if (animate) BodyBounceTransform.BeginAnimation(TranslateTransform.YProperty, Loop(0, -1.2, 700, easing: new SineEase { EasingMode = EasingMode.EaseOut }));
                break;
            case MascotMood.Meeting:
                // In a calendar meeting: a calm face and the matching headset with a microphone. Still, so it does not distract.
                ShowHeadset(HeadsetMood.Meeting);
                break;
            case MascotMood.Proud:
                // Every task due today is done: happy eyes and the green tick badge.
                ShowEyes(Eyes.Happy);
                break;
            case MascotMood.Drained:
                SetTint(new[] { "#E4E4E8", "#C9C9CF", "#A0A0A8", "#40FF7896" }, new[] { "#9CB08C", "#82976F", "#58694B", "#40FF7896" });
                ShowEyes(Eyes.Droop);
                if (animate) BodyBounceTransform.BeginAnimation(TranslateTransform.YProperty, Loop(0, 1.2, 2400, easing: new SineEase { EasingMode = EasingMode.EaseInOut }));
                break;
        }
    }

    public void SetThermalAlert(bool isHot)

    {

        SweatDrop.Visibility = isHot ? Visibility.Visible : Visibility.Collapsed;

    }



    private void Mascot_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)

    {

        PetMochi();

    }



    // Hovering over Mochi: a short happy glance and a tiny hop, at most every 20 seconds, only on the plain idle faces (a petting
    // click, a sleeping or a busy face keeps its own reaction) and only with animations on.
    private static readonly TimeSpan GlanceEvery = TimeSpan.FromSeconds(20);
    private DateTime _lastGlance;
    private System.Windows.Threading.DispatcherTimer? _glanceTimer;

    private void Mascot_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        var now = DateTime.UtcNow;
        if (!AnimationsEnabled || _spinning || now - _lastGlance < GlanceEvery || Mood is not (MascotMood.Idle or MascotMood.Waiting)) return;
        _lastGlance = now;
        LeftPillEye.Visibility = RightPillEye.Visibility = Visibility.Collapsed;
        LeftHappyEye.Visibility = RightHappyEye.Visibility = Visibility.Visible;
        BodyBounceTransform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, -1.6, TimeSpan.FromMilliseconds(160)) { AutoReverse = true, EasingFunction = new SineEase { EasingMode = EasingMode.EaseOut } });
        var glancing = Mood;
        _glanceTimer?.Stop();
        _glanceTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
        _glanceTimer.Tick += (_, _) =>
        {
            _glanceTimer?.Stop();
            if (!_spinning && Mood == glancing) ApplyMood(Mood);   // back to the face he had; a face change meanwhile already redrew him
        };
        _glanceTimer.Start();
    }

    public void PetMochi()

    {

        // Report the click first: the view model counts clicks and may turn Mochi dizzy right now.
        AnyMochiPetted?.Invoke();
        if (Mood == MascotMood.Dizzy) { WobbleDizzy(); return; }
        // Interactive Petting reaction: Coucou love sound + Happy squint eye arcs + elastic squish spring!

        SoundService.PlayPetSound();
        if (_isCat) CatPet();

        LeftPillEye.Visibility = Visibility.Collapsed;

        RightPillEye.Visibility = Visibility.Collapsed;

        LeftHappyEye.Visibility = Visibility.Visible;

        RightHappyEye.Visibility = Visibility.Visible;



        var ease = new ElasticEase { EasingMode = EasingMode.EaseOut, Oscillations = 2, Springiness = 4 };

        var squishX = new DoubleAnimation(1.30, 1.0, TimeSpan.FromMilliseconds(450)) { EasingFunction = ease };

        var squishY = new DoubleAnimation(0.68, 1.0, TimeSpan.FromMilliseconds(450)) { EasingFunction = ease };



        squishX.Completed += (s, e) =>

        {

            if (_spinning) return;   // the turn puts the face on when it comes round
            if (IsExtraMood(Mood)) { ApplyMood(Mood); return; }
            if (Mood != MascotMood.Music && Mood != MascotMood.Sleeping && !IsStill(Mood))

            {

                ApplyMood(Mood);   // back to the eyes of the face he wears (wide, glancing or plain)

            }

        };



        BodyScaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, squishX);

        BodyScaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, squishY);



        // Spawn Floating Heart Animation

        if (FloatingHeart != null && HeartTranslate != null)

        {

            var heartFade = new DoubleAnimationUsingKeyFrames();

            heartFade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));

            heartFade.KeyFrames.Add(new LinearDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(120))));

            heartFade.KeyFrames.Add(new LinearDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(450))));

            heartFade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(750))));



            var heartRise = new DoubleAnimation(-4, -22, TimeSpan.FromMilliseconds(750))

            {

                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }

            };



            FloatingHeart.BeginAnimation(UIElement.OpacityProperty, heartFade);

            HeartTranslate.BeginAnimation(TranslateTransform.YProperty, heartRise);

        }

    }


    public enum MascotTheme { Default, Matcha, Hojicha, KuroNight }

    /// <summary>The theme the app is using; set by ThemeService so a Mochi created later starts with the right body. Setting it clears <see cref="CurrentKind"/> back to Auto.</summary>
    public static MascotTheme CurrentTheme { get => _currentTheme; set { _currentTheme = value; _currentKind = null; } }
    private static MascotTheme _currentTheme;
    private static MochiDock.Core.MascotKind? _currentKind;

    /// <summary>Who a mascot created later is (ThemeService sets it after the theme; until then Auto: Kuro in KuroNight, Mochi elsewhere).</summary>
    public static MochiDock.Core.MascotKind CurrentKind
    {
        get => _currentKind ?? MochiDock.Core.MascotChoice.Resolve(MochiDock.Core.MascotChoice.Auto, ThemeNameOf(_currentTheme));
        set => _currentKind = value;
    }

    /// <summary>The Mascot setting (Auto, Mochi, Kuro or Shiro), set by the view model; ThemeService resolves it against the theme.</summary>
    public static string CurrentChoice { get; set; } = MochiDock.Core.MascotChoice.Auto;

    /// <summary>Kept for the render harnesses: true means Matcha, false Default.</summary>
    public static bool CurrentIsMatcha
    {
        get => CurrentTheme == MascotTheme.Matcha;
        set => CurrentTheme = value ? MascotTheme.Matcha : MascotTheme.Default;
    }

    private MascotTheme _theme = CurrentTheme;
    // The mascot decides the body colours, not the theme: Matcha Mochi is green and Hojicha Mochi tan on every theme. The theme (_theme) only
    // gives the ground, which matters for the rim on Matcha and for grey Mochi's headset on Kuro Night.
    private bool _isMatcha => _kind == MochiDock.Core.MascotKind.MatchaMochi;
    private bool _isHojicha => _kind == MochiDock.Core.MascotKind.HojichaMochi;

    // Who is drawn (the theme above is only the palette): Mochi, Kuro (the black electric cat) or Shiro (the white cat).
    private MochiDock.Core.MascotKind _kind = CurrentKind;
    private bool _isKuro => _kind == MochiDock.Core.MascotKind.Kuro;
    private bool _isShiro => _kind == MochiDock.Core.MascotKind.Shiro;
    private bool _isCat => _kind is MochiDock.Core.MascotKind.Kuro or MochiDock.Core.MascotKind.Shiro;

    // Hojicha's roasted kinako-tan body, worn instead of the neutral (grey-white) faces; the cheek colour is kept.
    private static readonly string[] HojichaBody = { "#E9D3B4", "#CFAF86", "#B08A60" };

    /// <summary>
    /// Picks the body tint for the theme: the faces that are neutral in colour have their own Matcha tint, so they do not turn grey
    /// on the green theme. Hojicha wears the normal faces, with the neutral ones in its tan body.
    /// </summary>
    private void SetTint(string[] normal, string[] matcha, bool animate = true)
    {
        var tint = _isMatcha ? matcha : normal;   // Matcha Mochi has his own tints; grey Mochi and the cats use the normal ones
        // The faces Matcha shows in its plain green body are Hojicha's plain tan body; faces with a tint of their own keep it.
        if (_isHojicha && string.Equals(matcha[0], MatchaBodyTop, StringComparison.OrdinalIgnoreCase))
            tint = new[] { HojichaBody[0], HojichaBody[1], HojichaBody[2], normal[3] };
        SetTint(tint[0], tint[1], tint[2], tint[3], animate);
    }

    private const string MatchaBodyTop = "#85B957";

    // The resting body of the theme (every mood starts from it).
    private void SetThemeBaseTint() =>
        SetTint(new[] { "#EDEDEF", "#C4C5CA", "#B8B9C0", "#73FF7896" }, new[] { "#85B957", "#54863B", "#365D2A", "#73FF7896" }, animate: false);

    // ---- Kuro: the KuroNight form of Mochi (the same squircle body with ears, a tail, light face ink and a glow in place of the shadow) ----

    private const double KuroSparkRest = 0.85;
    private bool _electricFace;           // the Electric face: a brighter glow
    private bool _kuroSparkWanted;     // the Electric face: the tail spark flickers

    /// <summary>
    /// Shows or hides the ears and the tail (Kuro and Shiro), sets the light face ink and the glow for Kuro and puts the black shadow
    /// back for Mochi and Shiro. Inner ears and the tail spark take the cat's colours.
    /// </summary>
    private void ApplyCatParts()
    {
        if (KuroEars == null || KuroTail == null) return;
        bool kuro = _isKuro, cat = _isCat;
        KuroEars.Visibility = KuroTail.Visibility = cat ? Visibility.Visible : Visibility.Collapsed;
        BodyGlow.Visibility = _kuroGlows ? Visibility.Visible : Visibility.Collapsed;
        InnerEarLeft.Fill = InnerEarRight.Fill = Ink(kuro ? "#FFD43B" : "#F4B6C2");
        InnerEarLeft.Opacity = InnerEarRight.Opacity = kuro ? 0.55 : 0.9;
        // The light-bodied mascots need a firmer rim on Matcha's light ground (a thin line is only a fraction of a pixel at the capsule size).
        double rim = MochiDock.Core.MascotRim.Thickness(_kind, ThemeName);
        MochiBodyBorder.BorderThickness = new Thickness(rim);
        // One outline: body, ears and tail (round joins). WPF layout rounding would snap the border to whole units (0.8 to 1, 1.3 to 1, 1.5 to 2)
        // while the ears' strokes stay exact, so the cats' border is drawn exact too; the Mochi kinds keep the rounding (their look is unchanged).
        MochiBodyBorder.UseLayoutRounding = MochiBodyBorder.SnapsToDevicePixels = !_isCat;
        EarLeft.StrokeThickness = EarRight.StrokeThickness = TailShape.StrokeThickness = rim;
        TailSpark.Fill = Ink(kuro ? "#FFF3B0" : "#8FD3FF");
        if (!_kuroGlows)
        {
            // Mochi's and Shiro's ears and tail keep the black shadow (Shiro has no glow), and so do Kuro's on the light ground
            UseShadow(EarGlow, System.Windows.Media.Colors.Black, 4, 1, 0.25);
            UseShadow(TailGlow, System.Windows.Media.Colors.Black, 4, 1, 0.25);
        }
        Resources["MascotEyeBrush"] = Ink(kuro ? "#FFD43B" : "#1A1412");
        Resources["MascotInkBrush"] = Ink(kuro ? "#F3EEDC" : "#1A1412");
        Resources["MascotSleepyBrush"] = Ink(kuro ? "#D9B433" : "#2C2422");
        BodySheen.Opacity = kuro ? 0.3 : 1;
        // Kuro's glow replaces the black drop shadow on the dark grounds (its colour comes with the face, ApplyKuroLook); on the light ground he
        // keeps the soft black shadow like the others.
        UseShadow(BodyShadow, System.Windows.Media.Colors.Black, 4, 1, _kuroGlows ? 0 : 0.25);
        if (!cat) TailSwayRotate.BeginAnimation(RotateTransform.AngleProperty, null);
        ApplyHeadset(HeadsetMood.Music);   // the band moves in front of or behind the ears with the mascot; the face adds its marks later
    }

    private static void UseShadow(System.Windows.Media.Effects.DropShadowEffect fx, System.Windows.Media.Color color, double blur, double depth, double opacity)
    {
        fx.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.BlurRadiusProperty, null);
        fx.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.OpacityProperty, null);
        fx.Color = color;
        fx.BlurRadius = blur;
        fx.ShadowDepth = depth;
        fx.Opacity = opacity;
    }

    // The rim, the glow, the ears and the tail for one face (the body itself is charcoal for every face). The glow sits on BodyGlow, not on the body.
    private void ApplyKuroLook(KuroTint k)
    {
        var rim = Ink(k.Rim);
        var body = Ink(k.BodyTop);
        MochiBodyBorder.BorderBrush = rim;
        LeftCheek.Fill = RightCheek.Fill = System.Windows.Media.Brushes.Transparent;   // a black cat has no pink cheeks
        if (_kuroGlows)
        {
            var glow = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(_electricFace ? MochiDock.Core.MascotTint.ElectricYellow : k.Glow);
            double blur = _electricFace ? 8 : 5, opacity = _electricFace ? 0.95 : 0.55;
            UseShadow(BodyGlowFx, glow, blur, 0, opacity);
            UseShadow(EarGlow, glow, blur, 0, opacity);
            UseShadow(TailGlow, glow, blur, 0, opacity);
        }
        else
        {
            // The light ground: no glow, the same soft black shadow as the other mascots.
            UseShadow(BodyGlowFx, System.Windows.Media.Colors.Black, 0, 0, 0);
            UseShadow(EarGlow, System.Windows.Media.Colors.Black, 4, 1, 0.25);
            UseShadow(TailGlow, System.Windows.Media.Colors.Black, 4, 1, 0.25);
        }
        EarLeft.Fill = EarRight.Fill = body;
        TailShape.Fill = body;
        EarLeft.Stroke = EarRight.Stroke = TailShape.Stroke = rim;
    }

    // The tail spark flickers while the Electric face is worn, with animations on and the control shown.
    private void UpdateKuroSpark()
    {
        if (!_isCat) return;
        TailSpark.BeginAnimation(OpacityProperty, null);
        TailSpark.Opacity = KuroSparkRest;
        if (_kuroSparkWanted && AnimationsEnabled && IsLoaded && IsVisible)
            TailSpark.BeginAnimation(OpacityProperty, Loop(1, 0.25, 260));
    }

    // One slow sway of the tail from the hip, about 1.4 seconds; the animation stops by itself (FillBehavior.Stop).
    private void SwayTail()
    {
        var sway = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
        foreach (var (ms, angle) in new[] { (0, 0.0), (350, -8.0), (800, 5.0), (1150, -3.0), (1400, 0.0) })
            sway.KeyFrames.Add(new EasingDoubleKeyFrame(angle, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms)), new SineEase { EasingMode = EasingMode.EaseInOut }));
        TailSwayRotate.BeginAnimation(RotateTransform.AngleProperty, sway);
    }

    // A quick flick when he is petted, about 0.45 seconds.
    private void FlickTail()
    {
        var flick = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
        foreach (var (ms, angle) in new[] { (0, 0.0), (120, -14.0), (300, 6.0), (450, 0.0) })
            flick.KeyFrames.Add(new EasingDoubleKeyFrame(angle, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms)), new SineEase { EasingMode = EasingMode.EaseInOut }));
        TailSwayRotate.BeginAnimation(RotateTransform.AngleProperty, flick);
    }

    // Petting a cat: the tail flicks and the spark flashes; Kuro's glow also pulses once, like a short static crackle (Shiro has no glow).
    private void CatPet()
    {
        if (!AnimationsEnabled || !IsVisible) return;
        var flash = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.Stop };
        foreach (var (ms, value) in new[] { (0, 0.85), (60, 1.0), (120, 0.1), (190, 1.0), (260, 0.2), (400, 0.85) })
            flash.KeyFrames.Add(new LinearDoubleKeyFrame(value, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms))));
        TailSpark.BeginAnimation(OpacityProperty, flash);
        FlickTail();
        if (!_kuroGlows) return;   // Shiro has no glow, and neither has Kuro on the light ground
        foreach (var fx in new[] { BodyGlowFx, EarGlow, TailGlow })
        {
            fx.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.OpacityProperty, new DoubleAnimation(fx.Opacity, 1, TimeSpan.FromMilliseconds(140)) { AutoReverse = true, FillBehavior = FillBehavior.Stop });
            fx.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.BlurRadiusProperty, new DoubleAnimation(fx.BlurRadius, fx.BlurRadius + 5, TimeSpan.FromMilliseconds(140)) { AutoReverse = true, FillBehavior = FillBehavior.Stop });
        }
    }

    /// <summary>Default or Matcha (kept for the render harnesses).</summary>
    public void ApplyTheme(bool isMatcha) => ApplyTheme(isMatcha ? MascotTheme.Matcha : MascotTheme.Default);

    public void ApplyTheme(string theme) => ApplyTheme(ToMascotTheme(theme));

    public static MascotTheme ToMascotTheme(string? theme) => MochiDock.Core.ThemePolicy.Normalize(theme) switch
    {
        MochiDock.Core.ThemePolicy.Matcha => MascotTheme.Matcha,
        MochiDock.Core.ThemePolicy.Hojicha => MascotTheme.Hojicha,
        MochiDock.Core.ThemePolicy.KuroNight => MascotTheme.KuroNight,
        _ => MascotTheme.Default,
    };

    /// <summary>The palette with the mascot Auto would pick (Kuro in KuroNight, Mochi elsewhere); kept for the render harnesses.</summary>
    public void ApplyTheme(MascotTheme theme) =>
        ApplyTheme(theme, MochiDock.Core.MascotChoice.Resolve(MochiDock.Core.MascotChoice.Auto, ThemeNameOf(theme)));

    /// <summary>The palette (theme) and who is drawn (kind) are separate: any mascot works with any theme.</summary>
    public void ApplyTheme(MascotTheme theme, MochiDock.Core.MascotKind kind)
    {
        if (MochiBodyBorder == null || BodyGradStopTop == null || BodyGradStopBottom == null) return;
        _theme = theme;
        bool kindChanged = kind != (_pendingKind ?? _kind);
        // A new mascot (the Mascot picker, or Auto on a theme change) turns into the other one inside the whirlwind, like a new face does;
        // without animations, or while he is not shown, he changes at once.
        if (kindChanged && (_spinning || (SpinEnabled && AnimationsEnabled && IsLoaded && IsVisible)))
        {
            _pendingKind = kind;
            if (!_spinning) Spin(Mood);   // the same face comes round on the new mascot
            return;
        }
        if (_spinning)
        {
            // Only the palette changed during a spin: a mascot change still waits for its swap; otherwise the parts follow the palette now.
            if (_pendingKind == null) { _kind = kind; ApplyCatParts(); }
            return;   // the turn applies the face (in the new theme) when it comes round
        }
        _pendingKind = null;
        _kind = kind;
        ApplyCatParts();   // right away: the ears and the tail are not part of a face
        ApplyMood(Mood);   // repaint the face being worn, not only the resting body
    }

    // The mascot a running spin turns into (null when the spin only changes the face).
    private MochiDock.Core.MascotKind? _pendingKind;

    private void ApplyPendingKind()
    {
        if (_pendingKind is not { } kind) return;
        _pendingKind = null;
        _kind = kind;
        ApplyCatParts();
    }

    // -x for the badge's counter-offset (BadgeSteady follows BodyBounceTransform, negated).
    private sealed class NegateConverter : System.Windows.Data.IValueConverter
    {
        public static readonly NegateConverter Instance = new();
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => value is double d ? -d : 0.0;
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => value is double d ? -d : 0.0;
    }
}
