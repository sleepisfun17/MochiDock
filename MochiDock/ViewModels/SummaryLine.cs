using System.Collections.Generic;
using System.Linq;
using MochiDock.Core;

namespace MochiDock.ViewModels;

/// <summary>
/// One row of a game summary: a short label ("Speed", "Network") and either a few stats (big number plus a small caption) or,
/// for rows that are a sentence (the match, the trend, what you missed), plain text.
/// </summary>
public sealed record SummaryLine(string Label, string Text, IReadOnlyList<GameSessionFormat.SummaryStat>? Stats = null)
{
    public bool HasStats => Stats is { Count: > 0 };
    public bool IsPlain => !HasStats;
}
