using System.Collections.Generic;
using MochiDock.Models;
using Xunit;

namespace MochiDock.Tests;

public class ClipboardHistoryItemTests
{
    [Fact]
    public void IsPinned_RaisesPropertyChanged_OnlyWhenValueChanges()
    {
        var item = new ClipboardHistoryItem { Text = "hello" };
        var changed = new List<string?>();
        item.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        item.IsPinned = false;
        Assert.Empty(changed);

        item.IsPinned = true;
        Assert.Equal(new[] { nameof(ClipboardHistoryItem.IsPinned) }, changed);

        item.IsPinned = false;
        Assert.Equal(new[] { nameof(ClipboardHistoryItem.IsPinned), nameof(ClipboardHistoryItem.IsPinned) }, changed);
    }
}
