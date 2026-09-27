using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Cafe.Launcher.Core.Models;

/// <summary>
/// 设置模型的通知基类。Core 的持久化模型只需要「值变了就报一声」——设置页的脏标记与绑定
/// 都依赖它——而不需要 MVVM 工具包：<c>CommunityToolkit.Mvvm</c> 属于表现层，Core 一旦引用它，
/// 「无表现依赖的后端」这条边界就只剩名义。
/// </summary>
/// <remarks>
/// 语义与 <c>ObservableObject.SetProperty</c> 一致（<see cref="EqualityComparer{T}.Default"/>
/// 判等，相同则不发通知），因此 <see cref="LauncherSettings"/>／<see cref="GameRuntimeSettings"/>
/// 的 setter 保持原有写法。表现层的可观察草稿与 ViewModel 继续使用 CommunityToolkit.Mvvm。
/// </remarks>
public abstract class SettingsModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(storage, value))
        {
            return false;
        }

        storage = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
