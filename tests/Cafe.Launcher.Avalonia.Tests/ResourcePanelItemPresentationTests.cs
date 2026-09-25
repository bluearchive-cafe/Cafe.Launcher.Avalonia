using System.Collections.Generic;
using Cafe.Launcher.Avalonia.Models;
using Xunit;

namespace Cafe.Launcher.Avalonia.Tests;

// ADR-041：状态 chip 的类映射布尔——视图层新消费的只读表象接缝。
public sealed class ResourcePanelItemPresentationTests
{
    [Fact]
    public void StatusFlags_MirrorTheStatusEnum_ExactlyOneAtATime()
    {
        foreach (var status in new[]
                 {
                     ResourcePanelItemStatus.Loading,
                     ResourcePanelItemStatus.Ready,
                     ResourcePanelItemStatus.Waiting,
                     ResourcePanelItemStatus.Failed
                 })
        {
            var item = new ResourcePanelItem(ResourcePanelResourceCodes.Text) { Status = status };

            Assert.Equal(status == ResourcePanelItemStatus.Loading, item.IsStatusLoading);
            Assert.Equal(status == ResourcePanelItemStatus.Ready, item.IsStatusReady);
            Assert.Equal(status == ResourcePanelItemStatus.Waiting, item.IsStatusWaiting);
            Assert.Equal(status == ResourcePanelItemStatus.Failed, item.IsStatusFailed);
        }
    }

    [Fact]
    public void StatusFlags_RaiseChangeNotifications_WhenStatusChanges()
    {
        var item = new ResourcePanelItem(ResourcePanelResourceCodes.Text);
        var reported = new List<string?>();
        item.PropertyChanged += (_, eventArgs) => reported.Add(eventArgs.PropertyName);

        item.Status = ResourcePanelItemStatus.Ready;

        Assert.Contains(nameof(ResourcePanelItem.IsOperable), reported);
        Assert.Contains(nameof(ResourcePanelItem.IsStatusLoading), reported);
        Assert.Contains(nameof(ResourcePanelItem.IsStatusReady), reported);
        Assert.Contains(nameof(ResourcePanelItem.IsStatusWaiting), reported);
        Assert.Contains(nameof(ResourcePanelItem.IsStatusFailed), reported);
    }

    [Fact]
    public void IsVersionAligned_WhenVersionsArePlaceholders_IsFalse()
    {
        // ADR-041 裁决 1 的依据："--" 占位不会被判成「版本一致」，未加载行走两列分支。
        var item = new ResourcePanelItem(ResourcePanelResourceCodes.Text)
        {
            OfficialVersion = "--",
            LocalizedVersion = "--"
        };

        Assert.False(item.IsVersionAligned);
    }
}

