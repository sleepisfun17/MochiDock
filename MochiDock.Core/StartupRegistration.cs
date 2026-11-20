namespace MochiDock.Core;

// Serializes the complete read/change/verify operation, including requests from different menus.
public sealed class StartupRegistration(Func<Task<bool>> read, Func<bool, Task> write)
{
    private readonly SemaphoreSlim gate = new(1, 1);

    public static bool ReadTaskEnabled(Func<bool> lookup)
    {
        try { return lookup(); }
        catch (Exception ex) when (ex.HResult == unchecked((int)0x80070002)) { return false; }
    }

    public async Task<bool> ReadAsync()
    {
        await gate.WaitAsync();
        try { return await read(); }
        finally { gate.Release(); }
    }

    public Task<bool> SetAsync(bool enabled) => ChangeAsync(enabled);
    public Task<bool> ToggleAsync() => ChangeAsync(null);

    private async Task<bool> ChangeAsync(bool? requested)
    {
        await gate.WaitAsync();
        try
        {
            bool target = requested ?? !await read();
            await write(target);
            bool actual = await read();
            if (actual != target) throw new InvalidOperationException("Windows startup verification failed. The saved preference was not changed.");
            return actual;
        }
        finally { gate.Release(); }
    }
}
