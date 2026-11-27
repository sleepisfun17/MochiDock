using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
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

    private readonly QueuedJsonWriter _writer = new();
    public event Action<string>? PersistenceError;
    public void LoadTasks()
    {
        var result = AtomicJsonStore.Load(TasksPath, () => new List<TodoTaskItem>());
        Tasks.Clear();
        foreach (var item in result.Value) Tasks.Add(item);
    }
    public void SaveTasks()
    {
        _writer.Enqueue(TasksPath, Tasks, msg => PersistenceError?.Invoke(msg));
        TasksChanged?.Invoke();
    }
    public Task FlushAsync() => _writer.FlushAsync();

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
    public TodoTaskItem AddTaskWithNaturalLanguage(string input, int? explicitDueMinutes = null, DateTime? selectedDate = null)
    {
        if (string.IsNullOrWhiteSpace(input)) return new TodoTaskItem();

        var parsed = MochiDock.Core.AgendaLogic.ParseTaskInput(input, DateTime.Now, explicitDueMinutes, selectedDate);
        var task = new TodoTaskItem
        {
            Title = parsed.Title,
            DueTime = parsed.DueTime,
            AllDay = parsed.AllDay,
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

    public bool UpdateTask(TodoTaskItem task, string input, int? explicitDueMinutes = null, DateTime? selectedDate = null)
    {
        if (string.IsNullOrWhiteSpace(input)) return false;

        var parsed = MochiDock.Core.AgendaLogic.ParseTaskEdit(
            input,
            DateTime.Now,
            explicitDueMinutes,
            (selectedDate ?? DateTime.Today).Date,
            task.DueTime,
            task.AllDay);
        if (string.IsNullOrWhiteSpace(parsed.Title)) return false;

        task.Title = parsed.Title;
        task.DueTime = parsed.DueTime;
        task.AllDay = parsed.AllDay;
        SaveTasks();
        return true;
    }

    public void SnoozeTask(TodoTaskItem task, int minutes = 10)
    {
        task.DueTime = DateTime.Now.AddMinutes(minutes);
        task.AllDay = false;
        task.IsCompleted = false;
        SaveTasks();
    }

    public void RescheduleTask(TodoTaskItem task, MochiDock.Core.TaskSnoozePreset preset)
    {
        var (due, allDay) = MochiDock.Core.AgendaLogic.ComputeSnoozeTarget(task.DueTime, task.AllDay, preset, DateTime.Now);
        task.DueTime = due;
        task.AllDay = allDay;
        if (preset != MochiDock.Core.TaskSnoozePreset.ClearDue) task.IsCompleted = false;
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
