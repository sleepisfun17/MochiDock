using MochiDock.Core;

public class AgendaTaskGroupTests
{
    static readonly DateTime Today = new(2026, 11, 14);

    [Fact]
    public void TaskDueOnTheSelectedDayBelongsToThatDayWhetherDoneOrNot()
    {
        Assert.Equal(AgendaTaskGroup.OnDay, AgendaLogic.TaskGroup(Today.AddHours(15), false, Today, Today));
        Assert.Equal(AgendaTaskGroup.OnDay, AgendaLogic.TaskGroup(Today.AddHours(15), true, Today, Today));
        var other = Today.AddDays(3);
        Assert.Equal(AgendaTaskGroup.OnDay, AgendaLogic.TaskGroup(other.AddHours(9), false, other, Today));
    }

    [Fact]
    public void UndatedAndOverdueOpenTasksFloatOnTodayOnly()
    {
        Assert.Equal(AgendaTaskGroup.Floating, AgendaLogic.TaskGroup(null, false, Today, Today));
        Assert.Equal(AgendaTaskGroup.Floating, AgendaLogic.TaskGroup(Today.AddDays(-2).AddHours(10), false, Today, Today));
        Assert.Equal(AgendaTaskGroup.None, AgendaLogic.TaskGroup(null, false, Today.AddDays(1), Today));
        Assert.Equal(AgendaTaskGroup.None, AgendaLogic.TaskGroup(Today.AddDays(-2), false, Today.AddDays(-1), Today));
    }

    [Fact]
    public void CompletedTasksOnOtherDaysAndFutureTasksAreNotListed()
    {
        Assert.Equal(AgendaTaskGroup.None, AgendaLogic.TaskGroup(null, true, Today, Today));
        Assert.Equal(AgendaTaskGroup.None, AgendaLogic.TaskGroup(Today.AddDays(-2), true, Today, Today));
        Assert.Equal(AgendaTaskGroup.None, AgendaLogic.TaskGroup(Today.AddDays(2), false, Today, Today));
    }
}
