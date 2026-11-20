using System.Runtime.InteropServices;
using MochiDock.Core;

namespace MochiDock.Tests;

public class StartupRegistrationTests
{
    [Fact]
    public void MissingTaskHandlesBothDotNetExceptionTypes()
    {
        Assert.False(StartupRegistration.ReadTaskEnabled(() => throw new FileNotFoundException()));
        Assert.False(StartupRegistration.ReadTaskEnabled(() => throw new COMException("Missing", unchecked((int)0x80070002))));
        Assert.True(StartupRegistration.ReadTaskEnabled(() => true));
        Assert.False(StartupRegistration.ReadTaskEnabled(() => false));
    }

    [Fact]
    public void OtherLookupFailuresAreNotHidden()
    {
        Assert.Throws<UnauthorizedAccessException>(() => StartupRegistration.ReadTaskEnabled(() => throw new UnauthorizedAccessException()));
        Assert.Throws<COMException>(() => StartupRegistration.ReadTaskEnabled(() => throw new COMException("Denied", unchecked((int)0x80070005))));
        Assert.Throws<IOException>(() => StartupRegistration.ReadTaskEnabled(() => throw new IOException("Disconnected")));
    }

    [Fact]
    public async Task MissingTaskCanBeEnabledAndVerifiedThenDisabled()
    {
        bool exists = false, enabled = false;
        var registration = new StartupRegistration(
            () => Task.FromResult(StartupRegistration.ReadTaskEnabled(() => exists ? enabled : throw new FileNotFoundException())),
            value => { exists = value; enabled = value; return Task.CompletedTask; });
        Assert.False(await registration.ReadAsync());
        Assert.True(await registration.ToggleAsync());
        Assert.False(await registration.ToggleAsync());
    }

    [Fact]
    public async Task FailedChangesDoNotReturnSuccessAndGateRecovers()
    {
        bool enabled = false, fail = true;
        var registration = new StartupRegistration(() => Task.FromResult(enabled), value =>
        {
            if (fail) throw new FileNotFoundException("Startup helper is missing.");
            enabled = value; return Task.CompletedTask;
        });
        await Assert.ThrowsAsync<FileNotFoundException>(() => registration.ToggleAsync());
        Assert.False(await registration.ReadAsync());
        fail = false;
        Assert.True(await registration.ToggleAsync());
    }

    [Fact]
    public async Task FailedVerificationCannotProduceASavedPreference()
    {
        bool savedPreference = false;
        var registration = new StartupRegistration(() => Task.FromResult(false), _ => Task.CompletedTask);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => savedPreference = await registration.ToggleAsync());
        Assert.False(savedPreference);
    }

    [Fact]
    public async Task RapidTogglesSerializeTheEntireTransaction()
    {
        bool enabled = false;
        int active = 0, maxActive = 0;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var registration = new StartupRegistration(() => Task.FromResult(enabled), async value =>
        {
            maxActive = Math.Max(maxActive, ++active);
            started.TrySetResult();
            await release.Task;
            enabled = value;
            active--;
        });
        var first = registration.ToggleAsync();
        await started.Task;
        var second = registration.ToggleAsync();
        Assert.False(second.IsCompleted);
        release.SetResult();
        Assert.True(await first);
        Assert.False(await second);
        Assert.Equal(1, maxActive);
    }
}
