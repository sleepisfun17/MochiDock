using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Windows.Threading;
using MochiDock.Models;

namespace MochiDock.Services;

public class TasksService
{
    private static readonly string AppDataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MochiDock");
    private static readonly string TasksPath = Path.Combine(AppDataDir, "tasks.json");

    private readonly DispatcherTimer _countdownTimer;

    public ObservableCollection<TodoTaskItem> Tasks { get; } = new();

    public event Action? TasksChanged;

    public TasksService()
    {
        LoadTasks();
        Current = this;

        _countdownTimer = new DispatcherTimer(
            TimeSpan.FromSeconds(10),
            DispatcherPriority.Background,
            (s, e) => RefreshCountdowns(),
            System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher);
        _countdownTimer.Start();
    }

    private void RefreshCountdowns()
    {
        foreach (var task in Tasks)
        {
            task.RefreshTimeState();
        }
    }

    private readonly object _saveGate = new();
    private Task _pendingSave = Task.CompletedTask;
    public event Action<string>? PersistenceError;
    public void LoadTasks()
    {
        var result = AtomicJsonStore.Load(TasksPath, () => new List<TodoTaskItem>());
        Tasks.Clear();
        foreach (var item in result.Value) Tasks.Add(item);
    }
    public void SaveTasks()
    {
        string snapshot = JsonSerializer.Serialize(Tasks, new JsonSerializerOptions { WriteIndented = true });
        lock (_saveGate)
            _pendingSave = _pendingSave.ContinueWith(_ =>
            {
                try { AtomicJsonStore.SaveJson(TasksPath, snapshot); }
                catch (Exception ex) { PersistenceError?.Invoke(ex.Message); }
            }, TaskScheduler.Default);
        TasksChanged?.Invoke();
    }
    public Task FlushAsync() { lock (_saveGate) return _pendingSave; }

    /// <summary>The app's task list for settings export and import. Read and changed on the UI thread only.</summary>
    public static TasksService? Current { get; private set; }

    /// <summary>The task list as JSON, the same shape as tasks.json, for the settings export.</summary>
    public JsonArray ExportTasks() => JsonSerializer.SerializeToNode(Tasks.ToList()) as JsonArray ?? new JsonArray();

    /// <summary>Reads the tasks of an export file. Throws <see cref="JsonException"/> when one cannot be read; a task without an id gets a new one.</summary>
    public static List<TodoTaskItem> ParseTasks(JsonArray tasks)
    {
        var parsed = tasks.Deserialize<List<TodoTaskItem?>>() ?? new();
        var result = new List<TodoTaskItem>();
        foreach (var task in parsed)
        {
            if (task == null) continue;
            if (string.IsNullOrWhiteSpace(task.Id)) task.Id = Guid.NewGuid().ToString("N");
            result.Add(task);
        }
        return result;
    }

    /// <summary>
    /// Merges imported tasks by id: same id is updated, new ones are added at the top, and local tasks missing from the file stay.
    /// Call on the UI thread.
    /// </summary>
    public void ImportTasks(IEnumerable<TodoTaskItem> imported)
    {
        var merged = MochiDock.Core.SettingsTransfer.MergeById(Tasks, imported, t => t.Id);
        Tasks.Clear();
        foreach (var task in merged) Tasks.Add(task);
        SaveTasks();
    }
    public TodoTaskItem AddTaskWithNaturalLanguage(string input, int? explicitDueMinutes = null)
    {
        if (string.IsNullOrWhiteSpace(input)) return new TodoTaskItem();

        string cleanTitle = input.Trim();
        DateTime? due = null;

        if (explicitDueMinutes.HasValue && explicitDueMinutes.Value > 0)
        {
            due = DateTime.Now.AddMinutes(explicitDueMinutes.Value);
        }
        else
        {
            // Pattern 1: Relative duration: "in 5m", "5m", "15 min", "2h", "1h 30m"
            var inMatch = Regex.Match(cleanTitle, @"\b(?:in|dalam)?\s*(\d+)\s*(m|min|mins|menit|h|hr|hours|jam)\b", RegexOptions.IgnoreCase);
            if (inMatch.Success)
            {
                int val = int.Parse(inMatch.Groups[1].Value);
                string unit = inMatch.Groups[2].Value.ToLowerInvariant();
                due = (unit.StartsWith("h") || unit == "jam") ? DateTime.Now.AddHours(val) : DateTime.Now.AddMinutes(val);
                cleanTitle = cleanTitle.Remove(inMatch.Index, inMatch.Length).Trim();
            }
            else
            {
                // Pattern 2: 12-hour format e.g. "at 2pm", "2:30 pm", "5pm"
                var ampmMatch = Regex.Match(cleanTitle, @"\b(?:at|jam|pukul)?\s*(\d{1,2})(?::(\d{2}))?\s*(am|pm)\b", RegexOptions.IgnoreCase);
                if (ampmMatch.Success)
                {
                    int h = int.Parse(ampmMatch.Groups[1].Value);
                    int m = ampmMatch.Groups[2].Success ? int.Parse(ampmMatch.Groups[2].Value) : 0;
                    string meridiem = ampmMatch.Groups[3].Value.ToLowerInvariant();
                    if (meridiem == "pm" && h < 12) h += 12;
                    if (meridiem == "am" && h == 12) h = 0;

                    var target = DateTime.Today.AddHours(h).AddMinutes(m);
                    if (target < DateTime.Now) target = target.AddDays(1);
                    due = target;
                    cleanTitle = cleanTitle.Remove(ampmMatch.Index, ampmMatch.Length).Trim();
                }
                else
                {
                    // Pattern 3: 24-hour format e.g. "at 14:30", "14:30", "jam 19"
                    var atMatch = Regex.Match(cleanTitle, @"\b(?:at|jam|pukul)?\s*(\d{1,2}):(\d{2})\b", RegexOptions.IgnoreCase);
                    if (atMatch.Success)
                    {
                        int h = int.Parse(atMatch.Groups[1].Value);
                        int m = int.Parse(atMatch.Groups[2].Value);
                        if (h >= 0 && h <= 23 && m >= 0 && m <= 59)
                        {
                            var target = DateTime.Today.AddHours(h).AddMinutes(m);
                            if (target < DateTime.Now) target = target.AddDays(1);
                            due = target;
                            cleanTitle = cleanTitle.Remove(atMatch.Index, atMatch.Length).Trim();
                        }
                    }
                    else
                    {
                        // Pattern 4: a keyword and bare digits, e.g. "at 0900", "at 9", "jam 19". The keyword is required, so numbers in a title ("buy 2") stay untouched.
                        var wordMatch = Regex.Match(cleanTitle, @"\b(?:at|jam|pukul)\s+(\d{1,4})\b", RegexOptions.IgnoreCase);
                        if (wordMatch.Success && MochiDock.Core.TimeInput.TryParse(wordMatch.Groups[1].Value, out var wordTime))
                        {
                            var target = DateTime.Today.Add(wordTime);
                            if (target < DateTime.Now) target = target.AddDays(1);
                            due = target;
                            cleanTitle = cleanTitle.Remove(wordMatch.Index, wordMatch.Length).Trim();
                        }
                    }
                }
            }
        }

        var task = new TodoTaskItem
        {
            Title = string.IsNullOrWhiteSpace(cleanTitle) ? input.Trim() : cleanTitle,
            DueTime = due,
            IsCompleted = false
        };

        Tasks.Insert(0, task);
        SaveTasks();
        return task;
    }

    public void ToggleTaskCompleted(TodoTaskItem task)
    {
        task.IsCompleted = !task.IsCompleted;
        SaveTasks();
    }

    public void SnoozeTask(TodoTaskItem task, int minutes = 10)
    {
        task.DueTime = DateTime.Now.AddMinutes(minutes);
        task.IsCompleted = false;
        SaveTasks();
    }

    public void DeleteTask(TodoTaskItem task)
    {
        Tasks.Remove(task);
        SaveTasks();
    }

    public TodoTaskItem? GetNextUpcomingTask()
    {
        TodoTaskItem? next = null;
        DateTime minDue = DateTime.MaxValue;

        foreach (var t in Tasks)
        {
            if (!t.IsCompleted && t.DueTime.HasValue && t.DueTime.Value >= DateTime.Now.AddMinutes(-5))
            {
                if (t.DueTime.Value < minDue)
                {
                    minDue = t.DueTime.Value;
                    next = t;
                }
            }
        }

        return next;
    }
}
