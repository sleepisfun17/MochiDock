using MochiDock.Core;
namespace MochiDock.Tests;
public class AiUsageAlertPolicyTests
{
    private static AiUsageAlertReading R(string id, double left, string window = "5-hour") => new(id, id, window, left);

    [Fact] public void AlertsOnceWhenFallingUnderTheLimit()
    {
        var policy = new AiUsageAlertPolicy();
        Assert.Empty(policy.Evaluate(new[] { R("claude", 40) }));
        Assert.Single(policy.Evaluate(new[] { R("claude", 12) }));
        Assert.Empty(policy.Evaluate(new[] { R("claude", 9) }));
    }

    [Fact] public void StaysQuietBetweenTheLimitAndRecovery()
    {
        var policy = new AiUsageAlertPolicy();
        policy.Evaluate(new[] { R("codex", 10) });
        Assert.Empty(policy.Evaluate(new[] { R("codex", 20) }));   // not recovered yet
        Assert.Empty(policy.Evaluate(new[] { R("codex", 14) }));   // still the same episode
    }

    [Fact] public void RearmsAfterRecovery()
    {
        var policy = new AiUsageAlertPolicy();
        policy.Evaluate(new[] { R("codex", 10) });
        policy.Evaluate(new[] { R("codex", 80) });                  // window reset
        Assert.Single(policy.Evaluate(new[] { R("codex", 5) }));
    }

    [Fact] public void ProvidersAreIndependentAndMissingOnesKeepState()
    {
        var policy = new AiUsageAlertPolicy();
        Assert.Equal(2, policy.Evaluate(new[] { R("claude", 5), R("codex", 8) }).Count);
        Assert.Empty(policy.Evaluate(new[] { R("claude", 4) }));    // codex absent this round
        Assert.Empty(policy.Evaluate(new[] { R("codex", 3) }));     // and still not re-announced
    }

    [Fact] public void ExactlyAtTheLimitIsNotLow()
    {
        Assert.Empty(new AiUsageAlertPolicy().Evaluate(new[] { R("claude", 15) }));
    }
}
