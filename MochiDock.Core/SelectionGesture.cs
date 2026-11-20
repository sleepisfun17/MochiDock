namespace MochiDock.Core;

// A release from a different view (such as the opening capsule) is not a click.
public sealed class SelectionGesture
{
    private object? pressed;
    public void Press(object target) => pressed = target;
    public void Cancel() => pressed = null;
    public bool Release(object target, bool inside)
    {
        bool accepted = inside && ReferenceEquals(pressed, target);
        Cancel();
        return accepted;
    }
}
