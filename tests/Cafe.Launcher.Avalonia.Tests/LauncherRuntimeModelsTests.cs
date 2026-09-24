using System.Collections.Generic;
using Cafe.Launcher.Avalonia.Models;

namespace Cafe.Launcher.Avalonia.Tests;

// 迁移到 ObservableObject 后的契约守卫：横幅导航点曾因静默丢通知而不更新，
// 这里对每类模型至少断言一个属性的 PropertyChanged 名称（D17）。
public sealed class LauncherRuntimeModelsTests
{
    [Fact]
    public void SelectableOption_WhenPropertyChanges_RaisesPropertyChangedWithPropertyName()
    {
        var names = CollectNames(new SettingOption(), option => option.Code = "jp");

        Assert.Equal(["Code"], names);
    }

    [Fact]
    public void SelectableOption_WhenSameValueAssigned_RaisesNoNotification()
    {
        var names = CollectNames(new LanguageOption { Code = "zh-Hans" }, option => option.Code = "zh-Hans");

        Assert.Empty(names);
    }

    [Fact]
    public void RemoteContentItem_WhenPropertyChanges_RaisesPropertyChangedWithPropertyName()
    {
        var titleNames = CollectNames(new RemoteContentItem(), item => item.Title = "标题");
        var bannerNames = CollectNames(new RemoteContentItem(), item => item.BannerBitmap = null);
        var urlNames = CollectNames(new RemoteContentItem(), item => item.Url = "https://example.com");

        Assert.Equal(["Title"], titleNames);
        Assert.Empty(bannerNames);
        Assert.Equal(["Url"], urlNames);
    }

    [Fact]
    public void RemoteContentItem_WhenImageLoadFails_RaisesBothFlagNotifications()
    {
        var names = CollectNames(new RemoteContentItem(), item => item.MarkImageLoadFailed());

        Assert.Equal(["IsImageLoading", "IsImageLoadFailed"], names);
    }

    [Fact]
    public void NewsCategory_WhenLabelChanges_RaisesPropertyChangedWithPropertyName()
    {
        var names = CollectNames(new NewsCategory(), category => category.Label = "公告");

        Assert.Equal(["Label"], names);
    }

    private static List<string> CollectNames<T>(T source, Action<T> change)
        where T : System.ComponentModel.INotifyPropertyChanged
    {
        var names = new List<string>();
        source.PropertyChanged += (_, e) => names.Add(e.PropertyName!);
        change(source);
        return names;
    }
}
