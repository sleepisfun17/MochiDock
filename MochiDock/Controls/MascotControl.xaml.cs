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
        if (_isMatcha) ApplyMood(Mood);   // the XAML body is the grey one
        BuildWind();
        Unloaded += (_, _) => { if (_spinning) EndSpin(landing: false); };
        IsVisibleChanged += (_, _) => { if (!IsVisible && _spinning) EndSpin(landing: false); };



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

        Loaded += (s, e) =>

        {

            StartRenderingHook();

            _blinkTimer.Start();
            _winkTimer.Start();

            _idleSleepTimer.Start();

        };



        Unloaded += (s, e) =>

        {

            StopRenderingHook();

            _blinkTimer.Stop();
            _winkTimer.Stop();

            _idleSleepTimer.Stop();

        };

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

        if (!IsLoaded || !IsVisible || IsStill(Mood)) return;



        double dt = _frameStopwatch.Elapsed.TotalSeconds;

        _frameStopwatch.Restart();



        if (dt > 0.1) dt = 0.016;



        try

        {

            if (GetCursorPos(out var pt))

            {

                if (pt.X != _lastMousePos.X || pt.Y != _lastMousePos.Y)

                {

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



        FaceTransform.X = _curX;

        FaceTransform.Y = _curY;

    }



    private void IdleSleepTimer_Tick(object? sender, EventArgs e)

    {

        // If mouse has not moved for 5 minutes and mood is Idle, Mochi takes a nap

        if (Mood == MascotMood.Idle && (DateTime.Now - _lastMouseMoveTime).TotalMinutes >= 5)

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



    private void ApplyBaseMood(MascotMood mood)

    {

        switch (mood)

        {

            case MascotMood.Music:

                LeftPillEye.Visibility = Visibility.Collapsed;

                RightPillEye.Visibility = Visibility.Collapsed;

                LeftHappyEye.Visibility = Visibility.Visible;

                RightHappyEye.Visibility = Visibility.Visible;
                LeftSleepyEye.Visibility = Visibility.Collapsed;
                RightSleepyEye.Visibility = Visibility.Collapsed;
                SleepyParticle.BeginAnimation(OpacityProperty, null);
                SleepyParticle.Opacity = 0;

                HeadphonesLayer.Visibility = Visibility.Visible;

                SweatDrop.Visibility = Visibility.Collapsed;



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
                LeftPillEye.Visibility = Visibility.Collapsed;
                RightPillEye.Visibility = Visibility.Collapsed;
                LeftHappyEye.Visibility = Visibility.Collapsed;
                RightHappyEye.Visibility = Visibility.Collapsed;
                LeftSleepyEye.Visibility = Visibility.Visible;
                RightSleepyEye.Visibility = Visibility.Visible;
                HeadphonesLayer.Visibility = Visibility.Collapsed;
                SweatDrop.Visibility = Visibility.Collapsed;

                // Deep relaxed sleeping breathing sway
                var sleepBreathing = new DoubleAnimation(0, 1.8, TimeSpan.FromMilliseconds(1600))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                };
                BodyBounceTransform.BeginAnimation(TranslateTransform.YProperty, sleepBreathing);

                // Floating Zzz animation
                var zzzOpacity = new DoubleAnimation(0, 0.9, TimeSpan.FromMilliseconds(1600))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever
                };
                var zzzTranslate = new DoubleAnimation(0, -5, TimeSpan.FromMilliseconds(1600))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseOut }
                };
                SleepyParticle.BeginAnimation(OpacityProperty, zzzOpacity);
                SleepyTranslate.BeginAnimation(TranslateTransform.YProperty, zzzTranslate);
                break;



            case MascotMood.Alert:

                LeftPillEye.Visibility = Visibility.Visible;

                RightPillEye.Visibility = Visibility.Visible;

                LeftHappyEye.Visibility = Visibility.Collapsed;

                RightHappyEye.Visibility = Visibility.Collapsed;
                LeftSleepyEye.Visibility = Visibility.Collapsed;
                RightSleepyEye.Visibility = Visibility.Collapsed;
                SleepyParticle.BeginAnimation(OpacityProperty, null);
                SleepyParticle.Opacity = 0;

                HeadphonesLayer.Visibility = Visibility.Collapsed;

                SweatDrop.Visibility = Visibility.Collapsed;



                BodyBounceTransform.BeginAnimation(TranslateTransform.YProperty, null);

                PerformBlink();

                break;



            case MascotMood.Focused:

                LeftPillEye.Visibility = Visibility.Visible;

                RightPillEye.Visibility = Visibility.Visible;

                LeftHappyEye.Visibility = Visibility.Collapsed;

                RightHappyEye.Visibility = Visibility.Collapsed;
                LeftSleepyEye.Visibility = Visibility.Collapsed;
                RightSleepyEye.Visibility = Visibility.Collapsed;
                SleepyParticle.BeginAnimation(OpacityProperty, null);
                SleepyParticle.Opacity = 0;

                HeadphonesLayer.Visibility = Visibility.Collapsed;

                SweatDrop.Visibility = Visibility.Collapsed;



                BodyBounceTransform.BeginAnimation(TranslateTransform.YProperty, null);

                FaceTransform.X = 0;

                FaceTransform.Y = 0;

                _curX = 0;

                _curY = 0;

                _targetX = 0;

                _targetY = 0;

                break;



            case MascotMood.Happy:

                LeftPillEye.Visibility = Visibility.Collapsed;

                RightPillEye.Visibility = Visibility.Collapsed;

                LeftHappyEye.Visibility = Visibility.Visible;

                RightHappyEye.Visibility = Visibility.Visible;
                LeftSleepyEye.Visibility = Visibility.Collapsed;
                RightSleepyEye.Visibility = Visibility.Collapsed;
                SleepyParticle.BeginAnimation(OpacityProperty, null);
                SleepyParticle.Opacity = 0;

                HeadphonesLayer.Visibility = Visibility.Collapsed;

                SweatDrop.Visibility = Visibility.Collapsed;

                BodyBounceTransform.BeginAnimation(TranslateTransform.YProperty,

                    new DoubleAnimation(0, -1.5, TimeSpan.FromMilliseconds(300))

                    { AutoReverse = true, RepeatBehavior = new RepeatBehavior(2) });

                break;



            case MascotMood.Hot:

                LeftPillEye.Visibility = Visibility.Visible;

                RightPillEye.Visibility = Visibility.Visible;

                LeftHappyEye.Visibility = Visibility.Collapsed;

                RightHappyEye.Visibility = Visibility.Collapsed;
                LeftSleepyEye.Visibility = Visibility.Collapsed;
                RightSleepyEye.Visibility = Visibility.Collapsed;
                SleepyParticle.BeginAnimation(OpacityProperty, null);
                SleepyParticle.Opacity = 0;

                HeadphonesLayer.Visibility = Visibility.Collapsed;

                SweatDrop.Visibility = Visibility.Visible;

                BodyBounceTransform.BeginAnimation(TranslateTransform.YProperty, null);

                break;



            default:

                LeftPillEye.Visibility = Visibility.Visible;

                RightPillEye.Visibility = Visibility.Visible;

                LeftHappyEye.Visibility = Visibility.Collapsed;

                RightHappyEye.Visibility = Visibility.Collapsed;
                LeftSleepyEye.Visibility = Visibility.Collapsed;
                RightSleepyEye.Visibility = Visibility.Collapsed;
                SleepyParticle.BeginAnimation(OpacityProperty, null);
                SleepyParticle.Opacity = 0;

                HeadphonesLayer.Visibility = Visibility.Collapsed;

                SweatDrop.Visibility = Visibility.Collapsed;



                BodyBounceTransform.BeginAnimation(TranslateTransform.YProperty, null);

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
            or MascotMood.Cloudy or MascotMood.Studying;

    // The gaming faces keep still: the eyes do not follow the pointer and do not blink.
    private static bool IsStill(MascotMood mood) => mood is MascotMood.Focused or MascotMood.Worried or MascotMood.BreakTime or MascotMood.Waking or MascotMood.Bored or MascotMood.Studying;

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
    }

    private void Spin(MascotMood mood)
    {
        _spinning = true;
        _swapped = false;
        _pendingMood = mood;
        SleepyParticle.Visibility = Visibility.Hidden;   // its own opacity is animated (Zzz), so it is hidden for the spin
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
        SleepyParticle.Visibility = Visibility.Visible;
        if (!_swapped || _appliedMood != _pendingMood) ApplyMood(_pendingMood);
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
        var core = Ink(_isMatcha ? "#F5F0DF" : "#F4F4F8");
        var glow = Ink(_isMatcha ? "#CC4E6B36" : "#CCD4BC8E");
        var inner = Ink(_isMatcha ? "#85B957" : "#F3E2BD");
        var wisp = Ink(_isMatcha ? "#CC4E6B36" : "#CCEBEBF0");
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
        DustA.Fill = DustB.Fill = DustC.Fill = Ink(_isMatcha ? "#554E6B36" : "#55D4BC8E");
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

    private void SetHeadset(string band, string cup)
    {
        HeadbandArc.Stroke = Ink(band);
        EarCupLeft.Background = EarCupRight.Background = Ink(cup);
    }

    // Gaming: a determined face. The eyes are cut slanted at the top (the cartoon way to look focused; Mochi has no eyebrows and no mouth),
    // a cool slate tint and a red gaming headset.
    // Still on purpose: ApplyBaseMood has already frozen the gaze, and these eyes do not blink.
    private void ApplyFocusedFace()
    {
        SetTint(new[] { "#E3E8F2", "#C3CCE0", "#5B6B8C", "#40FF7896" }, new[] { "#9DB8A9", "#7A9A8A", "#3F6656", "#40FF7896" }, animate: false);
        UseEyes(pill: false, flat: false, droop: false, spiral: false);
        LeftDetermEye.Visibility = RightDetermEye.Visibility = Visibility.Visible;
        SetHeadset("#E5484D", "#B3261E");
        HeadphonesLayer.Visibility = Visibility.Visible;
    }

    private void ResetExtras()
    {
        foreach (UIElement element in new UIElement[]
        {
            TearDrop, TearDropRight, AngerMark, StartleLines, WorryDrop, SparkleGroup, CoffeeGroup, ExclaimGroup, ConfettiGroup, SighGroup,
            LeftDetermEye, RightDetermEye, LeftFlatEye, RightFlatEye, LeftDroopEye, RightDroopEye, LeftSpiralEye, RightSpiralEye,
            SteamLeft, SteamRight, HeatLines, IcicleGroup, StarsGroup, BatteryGroup,
            StarEyes, CoinEyes, Sunglasses, BoltOrbit, CoinFloat, UmbrellaGroup, RainGroup, ScarfGroup, SnowGroup, CloudGroup, SunGroup, TimerBadge,
        }) element.Visibility = Visibility.Collapsed;

        BoltOrbitRotate.BeginAnimation(RotateTransform.AngleProperty, null);
        BoltOrbitRotate.Angle = 0;
        foreach (var bolt in new[] { BoltA, BoltB, BoltC }) { bolt.BeginAnimation(OpacityProperty, null); bolt.Opacity = 1; }
        CoinT.BeginAnimation(TranslateTransform.YProperty, null);
        CoinFloat.BeginAnimation(OpacityProperty, null);
        CoinT.Y = 0;
        CoinFloat.Opacity = 1;
        RainT.BeginAnimation(TranslateTransform.YProperty, null);
        RainGroup.BeginAnimation(OpacityProperty, null);
        RainT.Y = 0;
        RainGroup.Opacity = 1;
        SnowT.BeginAnimation(TranslateTransform.YProperty, null);
        SnowGroup.BeginAnimation(OpacityProperty, null);
        SnowT.Y = 0;
        SnowGroup.Opacity = 1;
        CloudT.BeginAnimation(TranslateTransform.XProperty, null);
        CloudT.X = 0;
        SunRaysRotate.BeginAnimation(RotateTransform.AngleProperty, null);
        TimerHandRotate.BeginAnimation(RotateTransform.AngleProperty, null);
        SunRaysRotate.Angle = TimerHandRotate.Angle = 0;

        WorryDropT.BeginAnimation(TranslateTransform.YProperty, null);
        WorryDrop.BeginAnimation(OpacityProperty, null);
        WorryDrop.Opacity = 1;
        WorryDropT.Y = 0;
        SetHeadset("#0A84FF", "#0062D6");   // the headset is blue for music; the gaming faces make it red
        AngerMark.BeginAnimation(OpacityProperty, null);
        AngerMark.Opacity = 1;
        TearRightT.BeginAnimation(TranslateTransform.YProperty, null);
        TearDropRight.BeginAnimation(OpacityProperty, null);
        TearRightT.Y = 0;
        TearDropRight.Opacity = 1;
        CoffeeSteam.BeginAnimation(OpacityProperty, null);
        CoffeeSteam.Opacity = 1;
        ExclaimGroup.BeginAnimation(OpacityProperty, null);
        ExclaimGroup.Opacity = 1;
        SighT.BeginAnimation(TranslateTransform.YProperty, null);
        SighGroup.BeginAnimation(OpacityProperty, null);
        SighGroup.Opacity = 1;
        SighT.Y = 0;
        ConfettiT.BeginAnimation(TranslateTransform.YProperty, null);
        ConfettiGroup.BeginAnimation(OpacityProperty, null);
        ConfettiGroup.Opacity = 1;
        ConfettiT.Y = 0;
        TearT.BeginAnimation(TranslateTransform.YProperty, null);
        TearDrop.BeginAnimation(OpacityProperty, null);
        foreach (var sparkle in new[] { SparkleA, SparkleB, SparkleC }) sparkle.BeginAnimation(OpacityProperty, null);
        TearT.Y = 0;
        TearDrop.Opacity = SparkleA.Opacity = SparkleB.Opacity = SparkleC.Opacity = 1;
        SteamLeftT.BeginAnimation(TranslateTransform.YProperty, null);
        SteamRightT.BeginAnimation(TranslateTransform.YProperty, null);
        SteamLeft.BeginAnimation(OpacityProperty, null);
        SteamRight.BeginAnimation(OpacityProperty, null);
        HeatT.BeginAnimation(TranslateTransform.YProperty, null);
        SweatTransform.BeginAnimation(TranslateTransform.YProperty, null);
        SweatDrop.BeginAnimation(OpacityProperty, null);
        LeftSpiralRotate.BeginAnimation(RotateTransform.AngleProperty, null);
        RightSpiralRotate.BeginAnimation(RotateTransform.AngleProperty, null);
        foreach (var star in new[] { StarA, StarB, StarC }) star.BeginAnimation(OpacityProperty, null);
        BodyBounceTransform.BeginAnimation(TranslateTransform.XProperty, null);
        BodyBounceTransform.X = 0;
        SteamLeftT.Y = SteamRightT.Y = HeatT.Y = SweatTransform.Y = 0;
        SteamLeft.Opacity = SteamRight.Opacity = SweatDrop.Opacity = 1;
        StarA.Opacity = StarB.Opacity = StarC.Opacity = 1;
        SetThemeBaseTint();
    }

    // Body colours: the gradient eases to the new tint; the edge and the cheeks switch at once.
    private void SetTint(string top, string bottom, string edge, string cheek, bool animate = true)
    {
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
        MochiBodyBorder.BorderBrush = Ink(edge);
        LeftCheek.Fill = Ink(cheek);
        RightCheek.Fill = Ink(cheek);
    }

    // The plain starting point of every extra face: pill eyes visible, the other eye shapes and the accessories hidden.
    private void ResetEyesForExtraMood()
    {
        LeftPillEye.Visibility = Visibility.Visible;
        RightPillEye.Visibility = Visibility.Visible;
        LeftHappyEye.Visibility = Visibility.Collapsed;
        RightHappyEye.Visibility = Visibility.Collapsed;
        LeftSleepyEye.Visibility = Visibility.Collapsed;
        RightSleepyEye.Visibility = Visibility.Collapsed;
        SleepyParticle.BeginAnimation(OpacityProperty, null);
        SleepyParticle.Opacity = 0;
        HeadphonesLayer.Visibility = Visibility.Collapsed;
        SweatDrop.Visibility = Visibility.Collapsed;
        BodyBounceTransform.BeginAnimation(TranslateTransform.YProperty, null);
    }

    private void UseEyes(bool pill, bool flat, bool droop, bool spiral)
    {
        LeftPillEye.Visibility = RightPillEye.Visibility = pill ? Visibility.Visible : Visibility.Collapsed;
        LeftFlatEye.Visibility = RightFlatEye.Visibility = flat ? Visibility.Visible : Visibility.Collapsed;
        LeftDroopEye.Visibility = RightDroopEye.Visibility = droop ? Visibility.Visible : Visibility.Collapsed;
        LeftSpiralEye.Visibility = RightSpiralEye.Visibility = spiral ? Visibility.Visible : Visibility.Collapsed;
    }

    private static DoubleAnimation Loop(double from, double to, int milliseconds, bool reverse = true, IEasingFunction? easing = null) => new(from, to, TimeSpan.FromMilliseconds(milliseconds))
    {
        AutoReverse = reverse,
        RepeatBehavior = RepeatBehavior.Forever,
        EasingFunction = easing,
    };

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
                SetTint("#F8C9C2", "#EFA79C", "#C0392B", "#99E24B4A");
                // Sharp slanted eyes, the anger mark above the head and a puff on each side.
                UseEyes(pill: false, flat: false, droop: false, spiral: false);
                LeftDetermEye.Visibility = RightDetermEye.Visibility = AngerMark.Visibility = Visibility.Visible;
                SteamLeft.Visibility = SteamRight.Visibility = Visibility.Visible;
                if (animate)
                {
                    AngerMark.BeginAnimation(OpacityProperty, Loop(1, 0.35, 380));
                    // Puffs rise from the top corners and fade; the body trembles a little.
                    SteamLeftT.BeginAnimation(TranslateTransform.YProperty, Loop(0, -3, 900, reverse: false));
                    SteamRightT.BeginAnimation(TranslateTransform.YProperty, Loop(0, -3, 900, reverse: false));
                    SteamLeft.BeginAnimation(OpacityProperty, Loop(0.95, 0, 900, reverse: false));
                    SteamRight.BeginAnimation(OpacityProperty, Loop(0.95, 0, 900, reverse: false));
                    BodyBounceTransform.BeginAnimation(TranslateTransform.XProperty, Loop(-0.4, 0.4, 110));
                }
                break;

            case MascotMood.Hot:
                SetTint("#F9D2B2", "#F2B089", "#D9622B", "#99E24B4A");
                UseEyes(pill: false, flat: true, droop: false, spiral: false);
                SweatDrop.Visibility = Visibility.Visible;
                HeatLines.Visibility = Visibility.Visible;
                if (animate)
                {
                    SweatTransform.BeginAnimation(TranslateTransform.YProperty, Loop(0, 4, 1000, reverse: false));
                    SweatDrop.BeginAnimation(OpacityProperty, Loop(1, 0.1, 1000, reverse: false));
                    HeatT.BeginAnimation(TranslateTransform.YProperty, Loop(0, -1.2, 700, easing: new SineEase()));
                }
                break;

            case MascotMood.Freezing:
                SetTint(new[] { "#9CCBEA", "#72AEDB", "#3F7FB5", "#997FB5E8" }, new[] { "#9CCBEA", "#72AEDB", "#3F7FB5", "#997FB5E8" });
                UseEyes(pill: true, flat: false, droop: false, spiral: false);
                IcicleGroup.Visibility = Visibility.Visible;
                if (animate) BodyBounceTransform.BeginAnimation(TranslateTransform.XProperty, Loop(-0.7, 0.7, 60));
                break;

            case MascotMood.Dizzy:
                SetTint(new[] { "#B9AEE0", "#9C8FD0", "#6F63B8", "#66B9A6E6" }, new[] { "#B9AEE0", "#9C8FD0", "#6F63B8", "#66B9A6E6" });
                UseEyes(pill: false, flat: false, droop: false, spiral: true);
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
                // Won the match: a gold glow, happy arc eyes and twinkling sparkles.
                SetTint(new[] { "#FCEFC4", "#F4D98A", "#D9A21C", "#99FF7896" }, new[] { "#F2D56A", "#E6BC3A", "#B58A10", "#99FF7896" });
                UseEyes(pill: false, flat: false, droop: false, spiral: false);
                LeftHappyEye.Visibility = RightHappyEye.Visibility = Visibility.Visible;
                SparkleGroup.Visibility = Visibility.Visible;
                if (animate)
                {
                    SparkleA.BeginAnimation(OpacityProperty, Loop(1, 0.25, 450));
                    SparkleB.BeginAnimation(OpacityProperty, Loop(0.25, 1, 450));
                    SparkleC.BeginAnimation(OpacityProperty, Loop(1, 0.25, 600));
                    BodyBounceTransform.BeginAnimation(TranslateTransform.YProperty, Loop(0, -1.5, 300, easing: new SineEase { EasingMode = EasingMode.EaseOut }));
                }
                break;

            case MascotMood.Defeat:
                // Lost the match: a cool grey-blue tint, sad drooping eyes and a tear from each eye; the body sinks a little.
                SetTint(new[] { "#9FB0CC", "#7F93B5", "#566A8E", "#40FF7896" }, new[] { "#9FB0CC", "#7F93B5", "#566A8E", "#40FF7896" });
                UseEyes(pill: false, flat: false, droop: true, spiral: false);
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
                // A new message: wide eyes, a bouncing body and an exclamation mark.
                SetTint(new[] { "#8FB0F2", "#6E94E6", "#3F62C8", "#80FF7896" }, new[] { "#8FB0F2", "#6E94E6", "#3F62C8", "#80FF7896" });
                UseEyes(pill: true, flat: false, droop: false, spiral: false);
                ExclaimGroup.Visibility = Visibility.Visible;
                if (animate)
                {
                    ExclaimGroup.BeginAnimation(OpacityProperty, Loop(1, 0.35, 350));
                    BodyBounceTransform.BeginAnimation(TranslateTransform.YProperty, Loop(0, -2, 260, easing: new SineEase { EasingMode = EasingMode.EaseOut }));
                }
                break;

            case MascotMood.Surprised:
                // Something just arrived: wide eyes and startle lines above the head; one jump.
                SetTint(new[] { "#FFF6E0", "#F5E2B8", "#D9B35C", "#80FF7896" }, new[] { "#F2D98A", "#E5C05E", "#B8902A", "#80FF7896" });
                UseEyes(pill: true, flat: false, droop: false, spiral: false);
                StartleLines.Visibility = Visibility.Visible;
                if (animate) BodyBounceTransform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, -3, TimeSpan.FromMilliseconds(180)) { AutoReverse = true, EasingFunction = new SineEase { EasingMode = EasingMode.EaseOut } });
                break;

            case MascotMood.Cheering:
                // A cheer (a Pomodoro finished): the happy face with confetti falling.
                SetTint(new[] { "#FFE4EC", "#F7B8CC", "#E0709A", "#99FF7896" }, new[] { "#F2A9C4", "#E98AAE", "#C25581", "#99FF7896" });
                UseEyes(pill: false, flat: false, droop: false, spiral: false);
                LeftHappyEye.Visibility = RightHappyEye.Visibility = Visibility.Visible;
                ConfettiGroup.Visibility = Visibility.Visible;
                if (animate)
                {
                    ConfettiT.BeginAnimation(TranslateTransform.YProperty, Loop(0, 4, 900, reverse: false));
                    ConfettiGroup.BeginAnimation(OpacityProperty, Loop(1, 0.2, 900, reverse: false));
                    BodyBounceTransform.BeginAnimation(TranslateTransform.YProperty, Loop(0, -1.6, 320, easing: new SineEase { EasingMode = EasingMode.EaseOut }));
                }
                break;

            case MascotMood.Praying:
                // The prayer time: calm closed eyes and a warm tint. Nothing else is drawn.
                SetTint(new[] { "#FFF3DC", "#F6DFA8", "#C9A24A", "#66FF7896" }, new[] { "#E8D08A", "#D6B866", "#A8873A", "#66FF7896" });
                UseEyes(pill: false, flat: false, droop: false, spiral: false);
                LeftSleepyEye.Visibility = RightSleepyEye.Visibility = Visibility.Visible;
                if (animate) BodyBounceTransform.BeginAnimation(TranslateTransform.YProperty, Loop(0, 1.2, 2400, easing: new SineEase { EasingMode = EasingMode.EaseInOut }));
                break;

            case MascotMood.Bored:
                // Lonely (nobody has touched the PC for a long while): a faded body, eyes looking down and a sigh.
                SetTint(new[] { "#EBEBEE", "#D3D4DA", "#A9ABB4", "#40FF7896" }, new[] { "#B3C79F", "#98AE86", "#647D55", "#40FF7896" });
                UseEyes(pill: true, flat: false, droop: false, spiral: false);
                FaceTransform.Y = 1.6;
                SighGroup.Visibility = Visibility.Visible;
                if (animate)
                {
                    SighT.BeginAnimation(TranslateTransform.YProperty, Loop(0, -3, 2200, reverse: false));
                    SighGroup.BeginAnimation(OpacityProperty, Loop(0.9, 0, 2200, reverse: false));
                    BodyBounceTransform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(900)) { FillBehavior = FillBehavior.HoldEnd });
                }
                break;

            case MascotMood.Waking:
                // Just woke up: closed eyes, a slow stretch and a drifting "z".
                SetTint(new[] { "#F0EAF7", "#D9CDEA", "#8E7BC4", "#80FF7896" }, new[] { "#A7C58A", "#8BAA70", "#5B7E48", "#80FF7896" });
                UseEyes(pill: false, flat: false, droop: false, spiral: false);
                LeftSleepyEye.Visibility = RightSleepyEye.Visibility = Visibility.Visible;
                if (animate)
                {
                    BodyBounceTransform.BeginAnimation(TranslateTransform.YProperty, Loop(0, -1.4, 1500, easing: new SineEase { EasingMode = EasingMode.EaseInOut }));
                    SleepyParticle.BeginAnimation(OpacityProperty, Loop(0, 0.8, 1500));
                    SleepyTranslate.BeginAnimation(TranslateTransform.YProperty, Loop(0, -4, 1500, easing: new SineEase { EasingMode = EasingMode.EaseOut }));
                }
                else SleepyParticle.Opacity = 0.8;
                break;

            case MascotMood.Worried:
                // Gaming, but it is going badly (the frame rate fell or the ping is high): wide eyes, a sweat drop and the red gaming headset.
                SetTint(new[] { "#E3E8F2", "#C3CCE0", "#5B6B8C", "#40FF7896" }, new[] { "#9DB8A9", "#7A9A8A", "#3F6656", "#40FF7896" });
                UseEyes(pill: true, flat: false, droop: false, spiral: false);
                WorryDrop.Visibility = Visibility.Visible;   // the drop flies off above the head, clear of the face and the headset
                SetHeadset("#E5484D", "#B3261E");
                HeadphonesLayer.Visibility = Visibility.Visible;
                if (animate)
                {
                    WorryDropT.BeginAnimation(TranslateTransform.YProperty, Loop(0, 3, 1000, reverse: false));
                    WorryDrop.BeginAnimation(OpacityProperty, Loop(1, 0.1, 1000, reverse: false));
                }
                break;

            case MascotMood.BreakTime:
                // The break reminder: sleepy closed eyes and a slow stretch, with a cup of coffee.
                SetTint(new[] { "#FBEBD3", "#F3D3A6", "#C98A5B", "#80FF7896" }, new[] { "#F0C27A", "#E3A456", "#B07430", "#80FF7896" });
                UseEyes(pill: false, flat: false, droop: false, spiral: false);
                LeftSleepyEye.Visibility = RightSleepyEye.Visibility = Visibility.Visible;
                CoffeeGroup.Visibility = Visibility.Visible;
                if (animate)
                {
                    CoffeeSteam.BeginAnimation(OpacityProperty, Loop(1, 0.2, 700));
                    BodyBounceTransform.BeginAnimation(TranslateTransform.YProperty, Loop(0, -1.2, 1400, easing: new SineEase { EasingMode = EasingMode.EaseInOut }));
                }
                break;

            case MascotMood.Electric:
                // The Stats Battery page: a charged yellow Mochi with star eyes and lightning bolts circling and flickering.
                SetTint(new[] { "#FFF4C2", "#F5D96B", "#C9A227", "#66FFB347" }, new[] { "#F3E7A8", "#DCC45E", "#A88A1E", "#66FFB347" });
                UseEyes(pill: false, flat: false, droop: false, spiral: false);
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
                // FX with the rate up or flat: coin eyes and a coin bobbing above the head.
                UseEyes(pill: false, flat: false, droop: false, spiral: false);
                CoinEyes.Visibility = CoinFloat.Visibility = Visibility.Visible;
                if (animate)
                {
                    CoinT.BeginAnimation(TranslateTransform.YProperty, Loop(0, -1.2, 700, easing: new SineEase { EasingMode = EasingMode.EaseInOut }));
                    BodyBounceTransform.BeginAnimation(TranslateTransform.YProperty, Loop(0, -0.8, 700, easing: new SineEase { EasingMode = EasingMode.EaseInOut }));
                }
                break;

            case MascotMood.MoneyDown:
                // FX with the rate down: the same coin eyes on a cooler body, the coin slipping away and the body sinking a little.
                SetTint(new[] { "#DCE3EE", "#BCC8DA", "#7D8FAE", "#40FF7896" }, new[] { "#A9B8A0", "#8C9D82", "#5E6E55", "#40FF7896" });
                UseEyes(pill: false, flat: false, droop: false, spiral: false);
                CoinEyes.Visibility = CoinFloat.Visibility = Visibility.Visible;
                if (animate)
                {
                    CoinT.BeginAnimation(TranslateTransform.YProperty, Loop(0, 3, 1500, reverse: false));
                    CoinFloat.BeginAnimation(OpacityProperty, Loop(1, 0.1, 1500, reverse: false));
                    BodyBounceTransform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, 0.8, TimeSpan.FromMilliseconds(600)) { FillBehavior = FillBehavior.HoldEnd });
                }
                break;

            case MascotMood.Sunny:
                // Weather, sunny: sunglasses on a warm body and a turning sun.
                SetTint(new[] { "#FFF6E0", "#F5E2B8", "#D9B35C", "#80FF7896" }, new[] { "#F2D98A", "#E5C05E", "#B8902A", "#80FF7896" });
                UseEyes(pill: false, flat: false, droop: false, spiral: false);
                Sunglasses.Visibility = SunGroup.Visibility = Visibility.Visible;
                if (animate) SunRaysRotate.BeginAnimation(RotateTransform.AngleProperty, Loop(0, 360, 6000, reverse: false));
                break;

            case MascotMood.Rainy:
                // Weather, rain: an umbrella over the head and drops falling past it.
                SetTint(new[] { "#E3ECF7", "#C3D3EA", "#7F98BD", "#40FF7896" }, new[] { "#A9C1CC", "#8AA6B4", "#5A7786", "#40FF7896" });
                UseEyes(pill: true, flat: false, droop: false, spiral: false);
                UmbrellaGroup.Visibility = RainGroup.Visibility = Visibility.Visible;
                if (animate)
                {
                    RainT.BeginAnimation(TranslateTransform.YProperty, Loop(0, 5, 900, reverse: false));
                    RainGroup.BeginAnimation(OpacityProperty, Loop(1, 0.1, 900, reverse: false));
                }
                break;

            case MascotMood.Snowy:
                // Weather, snow: a red scarf and snowflakes drifting down.
                UseEyes(pill: true, flat: false, droop: false, spiral: false);
                ScarfGroup.Visibility = SnowGroup.Visibility = Visibility.Visible;
                if (animate)
                {
                    SnowT.BeginAnimation(TranslateTransform.YProperty, Loop(0, 4, 2200, reverse: false));
                    SnowGroup.BeginAnimation(OpacityProperty, Loop(1, 0.2, 2200, reverse: false));
                }
                break;

            case MascotMood.Cloudy:
                // Weather, cloud or fog (or a clear night): a small cloud drifting above.
                UseEyes(pill: true, flat: false, droop: false, spiral: false);
                CloudGroup.Visibility = Visibility.Visible;
                if (animate) CloudT.BeginAnimation(TranslateTransform.XProperty, Loop(-1.5, 1.5, 2600, easing: new SineEase { EasingMode = EasingMode.EaseInOut }));
                break;

            case MascotMood.Studying:
                // A Pomodoro focus session: determined eyes and a tomato timer whose hand goes round once a minute.
                UseEyes(pill: false, flat: false, droop: false, spiral: false);
                LeftDetermEye.Visibility = RightDetermEye.Visibility = Visibility.Visible;
                TimerBadge.Visibility = Visibility.Visible;
                if (animate) TimerHandRotate.BeginAnimation(RotateTransform.AngleProperty, Loop(0, 360, 60000, reverse: false));
                break;

            case MascotMood.Drained:
                SetTint(new[] { "#E4E4E8", "#C9C9CF", "#A0A0A8", "#40FF7896" }, new[] { "#9CB08C", "#82976F", "#58694B", "#40FF7896" });
                UseEyes(pill: false, flat: false, droop: true, spiral: false);
                BatteryGroup.Visibility = Visibility.Visible;
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



    public void PetMochi()

    {

        // Report the click first: the view model counts clicks and may turn Mochi dizzy right now.
        AnyMochiPetted?.Invoke();
        if (Mood == MascotMood.Dizzy) { WobbleDizzy(); return; }
        // Interactive Petting reaction: Coucou love sound + Happy squint eye arcs + elastic squish spring!

        SoundService.PlayPetSound();

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

                LeftPillEye.Visibility = Visibility.Visible;

                RightPillEye.Visibility = Visibility.Visible;

                LeftHappyEye.Visibility = Visibility.Collapsed;

                RightHappyEye.Visibility = Visibility.Collapsed;
                LeftSleepyEye.Visibility = Visibility.Collapsed;
                RightSleepyEye.Visibility = Visibility.Collapsed;
                SleepyParticle.BeginAnimation(OpacityProperty, null);
                SleepyParticle.Opacity = 0;

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


    /// <summary>The theme the app is using; set by ThemeService so a Mochi created later starts with the right body.</summary>
    public static bool CurrentIsMatcha { get; set; }

    private bool _isMatcha = CurrentIsMatcha;

    /// <summary>Picks the body tint for the theme: the faces that are neutral in colour have their own Matcha tint, so they do not turn grey on the green theme.</summary>
    private void SetTint(string[] normal, string[] matcha, bool animate = true)
    {
        var tint = _isMatcha ? matcha : normal;
        SetTint(tint[0], tint[1], tint[2], tint[3], animate);
    }

    // The resting body of the theme (every mood starts from it).
    private void SetThemeBaseTint() =>
        SetTint(new[] { "#EDEDEF", "#C4C5CA", "#B8B9C0", "#73FF7896" }, new[] { "#85B957", "#54863B", "#365D2A", "#73FF7896" }, animate: false);

    public void ApplyTheme(bool isMatcha)
    {
        if (MochiBodyBorder == null || BodyGradStopTop == null || BodyGradStopBottom == null) return;
        _isMatcha = isMatcha;
        if (_spinning) return;   // the turn applies the face (in the new theme) when it comes round
        ApplyMood(Mood);   // repaint the face being worn, not only the resting body
    }
}
