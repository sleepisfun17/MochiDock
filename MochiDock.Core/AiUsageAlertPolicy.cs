namespace MochiDock.Core;

/// <summary>The tightest quota window of one provider, as a share that is still left (0..100).</summary>
public sealed record AiUsageAlertReading(string ProviderId, string ProviderName, string WindowLabel, double LeftPercent);

/// <summary>
/// Decides when a provider is worth a one-time "running low" notice. A provider alerts once when it falls under
/// <see cref="AlertBelowPercent"/> and is armed again only after it recovers to <see cref="RecoverAtPercent"/> (its window reset),
/// so a provider sitting at 10% is not announced on every refresh. Pure logic: no clock, I/O or WPF.
/// </summary>
public sealed class AiUsageAlertPolicy
{
    public const double AlertBelowPercent = 15;
    public const double RecoverAtPercent = 25;

    private readonly Dictionary<string, bool> alerted = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Pass one fresh reading per provider. Providers missing from a call keep their state.</summary>
    public IReadOnlyList<AiUsageAlertReading> Evaluate(IEnumerable<AiUsageAlertReading> readings)
    {
        var due = new List<AiUsageAlertReading>();
        foreach (var reading in readings)
        {
            if (double.IsNaN(reading.LeftPercent)) continue;
            alerted.TryGetValue(reading.ProviderId, out var already);
            if (reading.LeftPercent < AlertBelowPercent)
            {
                if (!already) { due.Add(reading); alerted[reading.ProviderId] = true; }
            }
            else if (reading.LeftPercent >= RecoverAtPercent)
            {
                alerted[reading.ProviderId] = false;
            }
        }
        return due;
    }

    public void Reset() => alerted.Clear();
}
