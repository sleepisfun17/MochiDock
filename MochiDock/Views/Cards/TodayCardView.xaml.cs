using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Media;
using System.Windows.Threading;
using MochiDock.Core;
using MochiDock.Services;
using MochiDock.ViewModels;

namespace MochiDock.Views.Cards;

/// <summary>One line of the day's agenda: a calendar event or a task due that day.</summary>
public sealed class AgendaRow
{
    public string TimeText { get; init; } = "";
    public string Title { get; init; } = "";
    public string Detail { get; init; } = "";
    public string? RowToolTip { get; init; }
    /// <summary>The calendar event behind this row, or null for a task.</summary>
    public AgendaEvent? Event { get; init; }
    /// <summary>The task behind this row, or null for a calendar event.</summary>
    public Models.TodoTaskItem? Task { get; init; }
    public bool IsDone => Task?.IsCompleted == true;
    public Visibility TaskVisibility => Task == null ? Visibility.Collapsed : Visibility.Visible;
    public Visibility SnoozeVisibility => Task != null && !IsDone ? Visibility.Visible : Visibility.Collapsed;
    public TextDecorationCollection? TitleDecorations => IsDone ? TextDecorations.Strikethrough : null;
    public string? JoinUrl { get; init; }
    public System.Windows.Media.Brush RowBackground { get; init; } = System.Windows.Media.Brushes.Transparent;
    public System.Windows.Media.Brush TitleBrush { get; init; } = System.Windows.Media.Brushes.Gray;
    public System.Windows.Media.Brush DetailBrush { get; init; } = System.Windows.Media.Brushes.Gray;
    public System.Windows.Media.Brush? CalendarDotBrush { get; init; }
    public FontWeight TitleWeight { get; init; } = FontWeights.SemiBold;
    public Visibility CalendarDotVisibility => CalendarDotBrush == null ? Visibility.Collapsed : Visibility.Visible;
    public Visibility DetailVisibility => string.IsNullOrEmpty(Detail) ? Visibility.Collapsed : Visibility.Visible;
    public Visibility JoinVisibility => string.IsNullOrEmpty(JoinUrl) ? Visibility.Collapsed : Visibility.Visible;
}

public partial class TodayCardView : System.Windows.Controls.UserControl
{
    private CalendarFeedService? _feed;
    private DateTime _month = DateTime.Today;
    private DateTime _selected = DateTime.Today;
    private readonly HashSet<INotifyPropertyChanged> _subscriptions = new();
    private MainViewModel? _owner;
    private Models.TodoTaskItem? _editingTask;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMinutes(1) };

    public TodayCardView()
    {
        InitializeComponent();
        Loaded += Start;
        Unloaded += Stop;
        MochiDock.Services.ThemeService.RedrawOnThemeChange(this, Render);   // calendar cells copy theme brushes when built
        Loaded += (_, _) => { MochiDock.Core.DateDisplay.Changed -= DateFormatChanged; MochiDock.Core.DateDisplay.Changed += DateFormatChanged; };
        Unloaded += (_, _) => MochiDock.Core.DateDisplay.Changed -= DateFormatChanged;
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) Start(this, new RoutedEventArgs());
            else Stop(this, new RoutedEventArgs());
        };
        _timer.Tick += Tick;
    }

    private void Start(object s, RoutedEventArgs e)
    {
        Stop(s, e);
        if (!IsVisible) return;
        _owner = DataContext as MainViewModel;
        if (_owner != null)
        {
            _owner.Tasks.CollectionChanged += Changed;
            _owner.PropertyChanged += OwnerPropertyChanged;
            _feed = _owner.CalendarFeed;
            if (_feed != null)
            {
                _feed.Changed += FeedChanged;
                _feed.SetVisible(true);   // the feed is only polled while this card is on screen
            }
        }
        _timer.Start();
        Render();
    }

    private void Stop(object s, RoutedEventArgs e)
    {
        _timer.Stop();
        if (_feed != null)
        {
            _feed.Changed -= FeedChanged;
            _feed.SetVisible(false);
            _feed = null;
        }
        if (_owner != null)
        {
            _owner.Tasks.CollectionChanged -= Changed;
            _owner.PropertyChanged -= OwnerPropertyChanged;
        }
        foreach (var t in _subscriptions) t.PropertyChanged -= TaskChanged;
        _subscriptions.Clear();
        CancelEdit(rerender: false);
        _owner = null;
    }

    private void Tick(object? s, EventArgs e) => Render();
    private void DateFormatChanged() => Dispatcher.BeginInvoke(() => { if (IsVisible) Render(); });
    private void FeedChanged() => Dispatcher.BeginInvoke(() => { if (IsVisible) Render(); });

    private void JoinMeeting_Click(object sender, RoutedEventArgs e)
    {
        // The link comes from an untrusted feed: re-check it against the conferencing allow-list right before opening it.
        if (sender is System.Windows.Controls.Button { Tag: string url } && AgendaLogic.IsJoinUrlAllowed(url))
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
        }
    }

    private void CalendarStatus_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_feed == null || !_feed.IsConnected || _feed.State == CalendarFeedState.Invalid)
            App.OpenSettings(MochiDock.Core.SettingsSections.Calendar);
    }
    private void Changed(object? s, NotifyCollectionChangedEventArgs e) => Render();
    private void TaskChanged(object? s, PropertyChangedEventArgs e) => Render();

    private void Previous(object s, RoutedEventArgs e)
    {
        _month = _month.AddMonths(-1);
        Render();
    }

    private void Next(object s, RoutedEventArgs e)
    {
        _month = _month.AddMonths(1);
        Render();
    }

    private void JumpToToday(object s, RoutedEventArgs e)
    {
        _month = DateTime.Today;
        _selected = DateTime.Today;
        Render();
    }

    private void OwnerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.ShowJapanHolidays) or nameof(MainViewModel.ShowIndonesiaHolidays) or nameof(MainViewModel.WeekStartsOnMonday))
            Dispatcher.BeginInvoke(() => { if (IsVisible) Render(); });
    }

    // ---- Tasks (merged from the former Tasks tab) -------------------------------------------------------------------

    private void AddTask_Click(object sender, RoutedEventArgs e) => SubmitTask();

    private void TaskInputBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter) { SubmitTask(); e.Handled = true; }
        else if (e.Key == System.Windows.Input.Key.Escape && _editingTask != null) { CancelEdit(); e.Handled = true; }
    }

    // Natural language such as "Review in 30m" is parsed by the view model.
    private void SubmitTask()
    {
        string text = TaskInputBox.Text.Trim();
        if (text.Length == 0 || _owner == null) return;
        if (_editingTask != null)
        {
            _owner.EditTask(_editingTask, text, null, _selected);
            CancelEdit();
            return;
        }
        _owner.AddTask(text, null, _selected);
        TaskInputBox.Text = "";
    }

    private void TaskCheck_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: AgendaRow { Task: { } task } }) _owner?.ToggleTask(task);
    }

    private void EditTask_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: AgendaRow { Task: { } task } }) BeginEditTask(task);
    }

    private void CancelEdit_Click(object sender, RoutedEventArgs e) => CancelEdit();

    private void SnoozeTask_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: AgendaRow { Task: { } task } } element || _owner == null) return;
        var itemStyle = (Style)FindResource("ModernTrayMenuItemStyle");
        var sepStyle = (Style)FindResource("ModernTraySeparatorStyle");
        var menu = new ContextMenu
        {
            Style = (Style)FindResource("ModernTrayMenuStyle"),
            PlacementTarget = element,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
        };
        PopulateSnoozeMenu(menu.Items, itemStyle, sepStyle, task);
        menu.IsOpen = true;
    }

    private void DeleteTask_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: AgendaRow { Task: { } task } })
        {
            if (ReferenceEquals(task, _editingTask)) CancelEdit(rerender: false);
            _owner?.DeleteTask(task);
        }
    }

    private void AgendaRow_LeftClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2 || sender is not FrameworkElement { DataContext: AgendaRow { Task: { } task } } row) return;
        for (DependencyObject? cur = e.OriginalSource as DependencyObject; cur != null && !ReferenceEquals(cur, row); cur = VisualTreeHelper.GetParent(cur))
            if (cur is System.Windows.Controls.Primitives.ButtonBase) return;
        BeginEditTask(task);
        e.Handled = true;
    }

    private void BeginEditTask(Models.TodoTaskItem task)
    {
        _editingTask = task;
        TaskInputBox.Text = AgendaLogic.FormatTaskForEdit(task.Title, task.DueTime, task.AllDay);
        TaskSubmitButton.Content = "Save";
        EditBanner.Visibility = Visibility.Visible;
        UpdateEditBanner();
        Render();
        TaskInputBox.Focus();
        TaskInputBox.SelectAll();
    }

    private void UpdateEditBanner()
    {
        if (_editingTask == null) return;
        string target = MochiDock.Core.DateDisplay.Short(_selected);
        EditBannerText.Text = _editingTask.DueTime is { } due && due.Date != _selected.Date
            ? $"Editing · moving to {target}"
            : $"Editing · {target} (click a day to move)";
    }

    private void CancelEdit(bool rerender = true)
    {
        if (_editingTask == null && EditBanner.Visibility != Visibility.Visible) return;
        _editingTask = null;
        TaskSubmitButton.Content = "+ Add";
        EditBanner.Visibility = Visibility.Collapsed;
        TaskInputBox.Text = "";
        if (rerender) Render();
    }

    private void Render()
    {
        if (_editingTask != null && (_owner == null || !_owner.Tasks.Contains(_editingTask)))
            CancelEdit(rerender: false);
        UpdateEditBanner();
        if (_owner != null)
        {
            var current = _owner.Tasks.Cast<INotifyPropertyChanged>().ToHashSet();
            foreach (var t in _subscriptions.Where(t => !current.Contains(t)).ToArray())
            {
                t.PropertyChanged -= TaskChanged;
                _subscriptions.Remove(t);
            }
            foreach (var t in current)
            {
                if (_subscriptions.Add(t)) t.PropertyChanged += TaskChanged;
            }
        }

        bool enableJp = _owner?.ShowJapanHolidays ?? true;
        bool enableId = _owner?.ShowIndonesiaHolidays ?? true;

        // Month header and selected day in the chosen date format (Settings > Appearance & Dock).
        MonthLabel.Text = MochiDock.Core.DateDisplay.Month(_month);

        // Selected Date Header: yyyy/MM/dd ddd (e.g. 2026/11/05 木)
        SelectedLabel.Text = MochiDock.Core.DateDisplay.Full(_selected);

        // Fixed-Height Holiday Status
        var selectedHolidays = HolidayEngine.GetHolidays(_selected, enableJp, enableId);
        if (selectedHolidays.Count > 0)
        {
            HolidayBanner.Opacity = 1.0;
            HolidayBannerText.Text = string.Join(" · ", selectedHolidays.Select(h =>
                (h.Country == "jp" ? "🇯🇵 " : "🇮🇩 ") + h.NameLocal + (string.IsNullOrEmpty(h.NameEn) ? "" : $" ({h.NameEn})")));
        }
        else
        {
            // Keep banner space reserved with invisible/subtle opacity to guarantee no layout displacement
            HolidayBanner.Opacity = 0.0;
            HolidayBannerText.Text = string.Empty;
        }

        DaysPanel.Children.Clear();
        var allEvents = _feed?.Events ?? Array.Empty<AgendaEvent>();
        var firstDay = _owner?.WeekStartsOnMonday == true ? DayOfWeek.Monday : DayOfWeek.Sunday;
        var monthDays = LocalCalendar.Days(_month, firstDay).ToList();
        var dayColors = monthDays.Count > 0 ? AgendaLogic.DayEventColors(allEvents, monthDays[0], monthDays[^1].AddDays(1)) : new Dictionary<DateTime, List<string>>();

        // 1. Weekday Headers, starting on the configured first day
        var weekdayLabels = new[] { "Su", "Mo", "Tu", "We", "Th", "Fr", "Sa" }; // indexed by DayOfWeek
        for (int i = 0; i < weekdayLabels.Length; i++)
        {
            var weekday = (DayOfWeek)(((int)firstDay + i) % 7);
            var text = new TextBlock
            {
                Text = weekdayLabels[(int)weekday],
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 4)
            };

            if (weekday == DayOfWeek.Saturday)
            {
                text.SetResourceReference(TextBlock.ForegroundProperty, "IslandSaturdayBrush");
            }
            else if (weekday == DayOfWeek.Sunday)
            {
                text.SetResourceReference(TextBlock.ForegroundProperty, "IslandNegativeBrush");
            }
            else
            {
                text.SetResourceReference(TextBlock.ForegroundProperty, "IslandSecondaryInkBrush");
            }

            DaysPanel.Children.Add(text);
        }

        // 2. 42 calendar days from the configured first day
        foreach (var day in monthDays)
        {
            dayColors.TryGetValue(day.Date, out var eventDotColors);
            bool hasEvent = eventDotColors is { Count: > 0 };
            var holidays = HolidayEngine.GetHolidays(day, enableJp, enableId);
            bool isHoliday = holidays.Count > 0;
            bool isSunday = day.DayOfWeek == DayOfWeek.Sunday;
            bool isSaturday = day.DayOfWeek == DayOfWeek.Saturday;
            bool isCurrentMonth = day.Month == _month.Month;
            bool isToday = day.Date == DateTime.Today;
            bool isSelected = day.Date == _selected.Date;

            // Day Container Grid
            // Fills the UniformGrid slot (no fixed size) so the highlight can never exceed its cell.
            var dayGrid = new Grid
            {
                Margin = new Thickness(1.5),
                SnapsToDevicePixels = true,
                UseLayoutRounding = true
            };

            // Selected & Today Selection Border
            var highlightBorder = new Border
            {
                CornerRadius = new CornerRadius(7),
                BorderThickness = new Thickness(isSelected || isToday ? 1.5 : 0),
                Background = isSelected
                    ? (System.Windows.Media.Brush)FindResource("IslandChipActiveBrush")
                    : (isToday ? (System.Windows.Media.Brush)FindResource("IslandChipBackgroundBrush") : System.Windows.Media.Brushes.Transparent)
            };

            if (isSelected)
            {
                highlightBorder.SetResourceReference(Border.BorderBrushProperty, "IslandAccentBrush");
            }
            else if (isToday)
            {
                highlightBorder.SetResourceReference(Border.BorderBrushProperty, "IslandPositiveBrush");
            }
            dayGrid.Children.Add(highlightBorder);

            // Day Number Text
            var dayText = new TextBlock
            {
                Text = day.Day.ToString(),
                FontSize = 12,
                FontWeight = (isToday || isHoliday) ? FontWeights.Bold : FontWeights.SemiBold,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                // Same lift on every cell so numbers share one baseline; dots sit below it in the freed space.
                Margin = new Thickness(0, 0, 0, 5),
                Opacity = isCurrentMonth ? 1.0 : 0.35
            };

            // Color: Red for Sunday & Holidays, Blue for Saturday, else InkBrush
            if (isSunday || isHoliday)
            {
                dayText.SetResourceReference(TextBlock.ForegroundProperty, "IslandNegativeBrush");
            }
            else if (isSaturday)
            {
                dayText.SetResourceReference(TextBlock.ForegroundProperty, "IslandSaturdayBrush");
            }
            else
            {
                dayText.SetResourceReference(TextBlock.ForegroundProperty, "IslandInkBrush");
            }
            dayGrid.Children.Add(dayText);

            if ((isHoliday && isCurrentMonth) || hasEvent)
            {
                var dotsPanel = new StackPanel
                {
                    Orientation = System.Windows.Controls.Orientation.Horizontal,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Margin = new Thickness(0, 0, 0, 3)
                };

                if (isHoliday && isCurrentMonth)
                {
                    var dot = new System.Windows.Shapes.Ellipse
                    {
                        Width = 4,
                        Height = 4,
                        Margin = new Thickness(1.5, 0, 1.5, 0)
                    };
                    dot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "IslandNegativeBrush");
                    dotsPanel.Children.Add(dot);
                }

                if (eventDotColors != null)
                {
                    foreach (string colorKey in eventDotColors)
                    {
                        var eventDot = new System.Windows.Shapes.Ellipse
                        {
                            Width = 4,
                            Height = 4,
                            Margin = new Thickness(1.5, 0, 1.5, 0),
                            Opacity = isCurrentMonth ? 1.0 : 0.5
                        };
                        eventDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, CalendarFeedPolicy.BrushKey(colorKey));
                        dotsPanel.Children.Add(eventDot);
                    }
                }

                dayGrid.Children.Add(dotsPanel);
            }

            // Clickable Transparent Button Overlay
            var btn = new System.Windows.Controls.Button
            {
                Style = (Style)FindResource("DayCellButtonStyle"),
                Content = dayGrid
            };

            // Holiday tooltip
            if (isHoliday)
            {
                btn.ToolTip = string.Join("\n", holidays.Select(h =>
                    (h.Country == "jp" ? "🇯🇵 " : "🇮🇩 ") + h.NameLocal + (string.IsNullOrEmpty(h.NameEn) ? "" : $" ({h.NameEn})")));
            }

            var capturedDay = day;
            btn.Click += (_, _) =>
            {
                _selected = capturedDay;
                Render();
            };

            DaysPanel.Children.Add(btn);
        }

        RenderAgenda(allEvents);
    }

    // The selected day's agenda: calendar events (all-day first) merged with tasks due that day, ordered by time.
    private void RenderAgenda(IReadOnlyList<AgendaEvent> allEvents)
    {
        var events = AgendaLogic.ForDay(allEvents, _selected);
        var focus = _selected.Date == DateTime.Today ? AgendaLogic.Focus(events, DateTime.Now) : null;
        System.Windows.Media.Brush Brush(string key) => (System.Windows.Media.Brush)FindResource(key);
        bool showCalendarLabels = (_feed?.Feeds.Count ?? 0) > 1;
        var rules = _owner?.GetCalendarActivityRules();

        var timed = new List<(DateTime Sort, int Group, AgendaRow Row)>();
        foreach (var e in events)
        {
            bool isFocus = focus != null && ReferenceEquals(focus.Event, e);
            string detail = CalendarFeedPolicy.FormatDetail(e.CalendarName, showCalendarLabels, isFocus ? focus!.Text : null, e.Place);
            string tip = e.Title;
            if (!e.AllDay)
            {
                var activity = AgendaLogic.ClassifyActivity(e, rules);
                tip = $"{e.Title}\nMascot badge: {ActivityLabel(activity)} · Right-click to change";
            }
            var row = new AgendaRow
            {
                TimeText = AgendaLogic.TimeText(e),
                Title = e.Title,
                Detail = detail,
                RowToolTip = tip,
                Event = e,
                JoinUrl = e.JoinUrl,
                CalendarDotBrush = showCalendarLabels ? Brush(CalendarFeedPolicy.BrushKey(e.CalendarColor)) : null,
                TitleBrush = isFocus ? Brush("IslandAccentBrush") : Brush("IslandInkBrush"),
                DetailBrush = isFocus ? Brush("IslandAccentBrush") : Brush("IslandMutedInkBrush"),
                TitleWeight = isFocus ? (FontWeight)FindResource("IslandHeadingWeight") : (FontWeight)FindResource("IslandSmallLabelWeight")
            };
            timed.Add((e.AllDay ? e.Start.Date : e.Start, e.AllDay ? 0 : 1, row));
        }
        foreach (var t in _owner?.Tasks ?? Enumerable.Empty<Models.TodoTaskItem>())
        {
            var group = AgendaLogic.TaskGroup(t.DueTime, t.IsCompleted, _selected, DateTime.Today);
            if (group == AgendaTaskGroup.None) continue;
            string countdown = t.CountdownText;
            string timeText = t.DueTime is { } due
                ? (t.AllDay
                    ? (group == AgendaTaskGroup.OnDay ? "All day" : MochiDock.Core.DateDisplay.Short(due))
                    : (group == AgendaTaskGroup.OnDay ? due.ToString("HH:mm") : MochiDock.Core.DateDisplay.Short(due) + due.ToString(" HH:mm")))
                : "—";
            bool floatingOverdue = group == AgendaTaskGroup.Floating && t.DueTime != null && (!t.AllDay || t.DueTime.Value.Date < DateTime.Today);
            var row = new AgendaRow
            {
                TimeText = timeText,
                Title = t.Title,
                Detail = string.IsNullOrEmpty(countdown) ? (floatingOverdue ? "Task · overdue" : "Task") : "Task · " + countdown,
                RowToolTip = $"{t.Title}\nDouble-click to edit · Right-click for snooze & actions",
                Task = t,
                RowBackground = ReferenceEquals(t, _editingTask) ? Brush("IslandChipActiveBrush") : System.Windows.Media.Brushes.Transparent,
                TitleBrush = Brush(t.IsCompleted ? "IslandMutedInkBrush" : "IslandInkBrush"),
                DetailBrush = Brush((t.IsDueSoon || t.IsOverdue) && !t.IsCompleted ? "IslandRedBrush" : "IslandMutedInkBrush"),
                TitleWeight = (FontWeight)FindResource("IslandSmallLabelWeight")
            };
            // All-day tasks on the selected day sit at the top with all-day events; open tasks without a time come last.
            int rowGroup = group == AgendaTaskGroup.Floating ? 2 : (t.AllDay ? 0 : 1);
            timed.Add((t.DueTime ?? DateTime.MaxValue, rowGroup, row));
        }

        var rows = timed.OrderBy(x => x.Group).ThenBy(x => x.Sort).Select(x => x.Row).ToList();
        TaskList.ItemsSource = rows;
        AgendaEmptyText.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        if (_feed == null || !_feed.IsConnected)
        {
            CalendarStatusText.Text = "Connect Google Calendar in Settings";
            CalendarStatusText.Cursor = System.Windows.Input.Cursors.Hand;
            CalendarStatusText.SetResourceReference(TextBlock.ForegroundProperty, "IslandMutedInkBrush");
        }
        else
        {
            CalendarStatusText.Text = _feed.StatusText;
            CalendarStatusText.Cursor = _feed.State == CalendarFeedState.Invalid ? System.Windows.Input.Cursors.Hand : System.Windows.Input.Cursors.Arrow;
            CalendarStatusText.SetResourceReference(TextBlock.ForegroundProperty, _feed.State == CalendarFeedState.Invalid ? "IslandNegativeBrush" : "IslandMutedInkBrush");
        }
    }

    private static string ActivityLabel(CalendarActivityKind kind) => kind switch
    {
        CalendarActivityKind.Working => "Working",
        CalendarActivityKind.Meeting => "Meeting",
        CalendarActivityKind.Eating => "Eating / Lunch",
        CalendarActivityKind.Workout => "Gym / Workout",
        _ => "None",
    };

    private void AgendaRow_RightClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element || _owner == null) return;

        if (element.DataContext is AgendaRow { Task: { } task })
        {
            e.Handled = true;
            OpenTaskContextMenu(element, task);
            return;
        }

        if (element.DataContext is not AgendaRow { Event: { AllDay: false } ev })
            return;

        e.Handled = true;
        var itemStyle = (Style)FindResource("ModernTrayMenuItemStyle");
        var sepStyle = (Style)FindResource("ModernTraySeparatorStyle");
        var menu = new ContextMenu
        {
            Style = (Style)FindResource("ModernTrayMenuStyle"),
            PlacementTarget = element,
            Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint,
        };

        string titleText = (ev.Title ?? "").Trim();
        string shortTitle = titleText.Length > 26 ? titleText[..25] + "…" : titleText;
        menu.Items.Add(new MenuItem { Header = $"Badge for \"{shortTitle}\"", IsEnabled = false, Style = itemStyle });

        var autoKind = AgendaLogic.ClassifyActivity(ev);
        var currentTitleRule = _owner.GetCalendarTitleActivityOverride(ev.Title);
        PopulateActivityMenu(menu.Items, itemStyle, $"Auto ({ActivityLabel(autoKind)})", currentTitleRule, kind =>
        {
            _owner.SetCalendarTitleActivity(ev.Title, kind);
            Render();
        });

        if (!string.IsNullOrWhiteSpace(ev.CalendarName))
        {
            menu.Items.Add(new Separator { Style = sepStyle });
            var calSubmenu = new MenuItem { Header = $"Default for \"{ev.CalendarName}\" calendar", Style = itemStyle };
            var currentCalRule = _owner.GetCalendarFeedActivityOverride(ev.CalendarName);
            PopulateActivityMenu(calSubmenu.Items, itemStyle, "Auto", currentCalRule, kind =>
            {
                _owner.SetCalendarFeedActivity(ev.CalendarName, kind);
                Render();
            });
            menu.Items.Add(calSubmenu);
        }

        menu.IsOpen = true;
    }

    private static void PopulateActivityMenu(ItemCollection items, Style itemStyle, string autoLabel, CalendarActivityKind? current, Action<CalendarActivityKind?> onSelect)
    {
        var choices = new (string Label, CalendarActivityKind? Kind)[]
        {
            (autoLabel, null),
            ("Working", CalendarActivityKind.Working),
            ("Meeting", CalendarActivityKind.Meeting),
            ("Eating / Lunch", CalendarActivityKind.Eating),
            ("Gym / Workout", CalendarActivityKind.Workout),
            ("None (no badge)", CalendarActivityKind.None),
        };
        foreach (var (label, kind) in choices)
        {
            var item = new MenuItem { Header = label, IsChecked = current == kind, Style = itemStyle };
            var selected = kind;
            item.Click += (_, _) => onSelect(selected);
            items.Add(item);
        }
    }

    private void OpenTaskContextMenu(FrameworkElement element, Models.TodoTaskItem task)
    {
        var itemStyle = (Style)FindResource("ModernTrayMenuItemStyle");
        var sepStyle = (Style)FindResource("ModernTraySeparatorStyle");
        var menu = new ContextMenu
        {
            Style = (Style)FindResource("ModernTrayMenuStyle"),
            PlacementTarget = element,
            Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint,
        };

        var editItem = new MenuItem { Header = "Edit task", Style = itemStyle };
        editItem.Click += (_, _) => BeginEditTask(task);
        menu.Items.Add(editItem);

        if (!task.IsCompleted)
        {
            var snoozeSub = new MenuItem { Header = "Snooze", Style = itemStyle };
            PopulateSnoozeMenu(snoozeSub.Items, itemStyle, sepStyle, task);
            menu.Items.Add(snoozeSub);
        }

        var toggleItem = new MenuItem { Header = task.IsCompleted ? "Mark undone" : "Mark done", Style = itemStyle };
        toggleItem.Click += (_, _) => _owner?.ToggleTask(task);
        menu.Items.Add(toggleItem);

        if (task.IsCompleted && task.DueTime != null)
        {
            var clearDueItem = new MenuItem { Header = "Remove due time", Style = itemStyle };
            clearDueItem.Click += (_, _) => ApplySnooze(task, TaskSnoozePreset.ClearDue);
            menu.Items.Add(clearDueItem);
        }

        menu.Items.Add(new Separator { Style = sepStyle });
        var deleteItem = new MenuItem { Header = "Delete task", Style = itemStyle };
        deleteItem.Click += (_, _) =>
        {
            if (ReferenceEquals(task, _editingTask)) CancelEdit(rerender: false);
            _owner?.DeleteTask(task);
        };
        menu.Items.Add(deleteItem);

        menu.IsOpen = true;
    }

    private void PopulateSnoozeMenu(ItemCollection items, Style itemStyle, Style sepStyle, Models.TodoTaskItem task)
    {
        DateTime now = DateTime.Now;
        var tomorrow = AgendaLogic.ComputeSnoozeTarget(task.DueTime, task.AllDay, TaskSnoozePreset.Tomorrow, now);
        var nextWeek = AgendaLogic.ComputeSnoozeTarget(task.DueTime, task.AllDay, TaskSnoozePreset.NextWeek, now);
        string tomorrowHint = tomorrow.AllDay ? "All day" : tomorrow.DueTime!.Value.ToString("HH:mm");
        string nextWeekHint = nextWeek.AllDay
            ? MochiDock.Core.DateDisplay.Short(nextWeek.DueTime!.Value)
            : MochiDock.Core.DateDisplay.Short(nextWeek.DueTime!.Value) + nextWeek.DueTime.Value.ToString(" HH:mm");

        var choices = new (string Label, TaskSnoozePreset Preset)[]
        {
            ("+10 min", TaskSnoozePreset.Minutes10),
            ("+30 min", TaskSnoozePreset.Minutes30),
            ("+1 hour", TaskSnoozePreset.Hour1),
            ("+3 hours", TaskSnoozePreset.Hours3),
            ($"Tomorrow ({tomorrowHint})", TaskSnoozePreset.Tomorrow),
            ($"Next week ({nextWeekHint})", TaskSnoozePreset.NextWeek),
        };
        foreach (var (label, preset) in choices)
        {
            var item = new MenuItem { Header = label, Style = itemStyle };
            var chosen = preset;
            item.Click += (_, _) => ApplySnooze(task, chosen);
            items.Add(item);
        }

        if (task.DueTime != null)
        {
            items.Add(new Separator { Style = sepStyle });
            var clearItem = new MenuItem { Header = "Remove due time", Style = itemStyle };
            clearItem.Click += (_, _) => ApplySnooze(task, TaskSnoozePreset.ClearDue);
            items.Add(clearItem);
        }
    }

    private void ApplySnooze(Models.TodoTaskItem task, TaskSnoozePreset preset)
    {
        if (ReferenceEquals(task, _editingTask)) CancelEdit(rerender: false);
        _owner?.SnoozeTask(task, preset);
    }
}
