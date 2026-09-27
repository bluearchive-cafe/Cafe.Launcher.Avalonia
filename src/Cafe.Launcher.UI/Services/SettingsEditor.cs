using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Threading;
using Cafe.Launcher.UI.Models;
using Cafe.Launcher.Core;
using Cafe.Launcher.Core.Services;
using Cafe.Launcher.Core.Models;

namespace Cafe.Launcher.UI.Services;

/// <summary>
/// The settings state: the draft the user edits (<see cref="Current"/>) and the last saved
/// snapshot it is compared against. In-memory and I/O-free on purpose — every production write
/// to the saved settings goes through <see cref="ISavedSettingsWriter"/>, which persists and then
/// applies the persisted value back here so the draft never disagrees with disk.
/// </summary>
internal sealed class SettingsEditor : INotifyPropertyChanged, ISettingsDraftOwner
{
    private LauncherSettings current;
    private LauncherSettings snapshot;
    private bool isDirty;

    /// <summary>
    /// Creates the editor over the default settings for a fresh draft. <paramref name="buildIdentity"/>
    /// only decides the default update channel (pre-release builds default to Beta); the composition
    /// root injects it, and a draft is replaced by the saved snapshot as soon as settings load.
    /// </summary>
    public SettingsEditor(LauncherBuildIdentity? buildIdentity = null)
    {
        var defaults = LauncherSettings.CreateDefaults(buildIdentity);
        current = defaults;
        snapshot = defaults.DeepClone();
        AttachCurrentListeners();
    }

    public LauncherSettings Current => current;

    /// <summary>
    /// Whether <see cref="Current"/> differs from the last saved snapshot — the state identity
    /// defined by <see cref="LauncherSettings.HasSameSettingsState"/>. Recomputed whenever a field
    /// on <see cref="Current"/> changes; the settings page's save button tracks nothing else.
    /// </summary>
    public bool IsDirty => isDirty;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Fires for per-field changes on the <see cref="Current"/> settings object.
    /// Distinct from <see cref="INotifyPropertyChanged.PropertyChanged"/>, which fires
    /// for editor-level state changes (<see cref="Current"/> reference replacement,
    /// <see cref="IsDirty"/> transitions).
    /// </summary>
    public event PropertyChangedEventHandler? CurrentPropertyChanged;

    public LauncherSettings GetSnapshot() => current.DeepClone();

    public LauncherSettings GetSavedSnapshot() => snapshot.DeepClone();

    public void ApplySnapshot(LauncherSettings settings)
    {
        DetachCurrentListeners();
        current = settings.DeepClone();
        AttachCurrentListeners();
        snapshot = settings.DeepClone();
        isDirty = false;
        OnPropertyChanged(nameof(Current));
        OnPropertyChanged(nameof(IsDirty));
    }

    public void Commit(Action<LauncherSettings> apply)
    {
        apply(current);
    }

    LauncherSettings ISettingsDraftOwner.GetDraftSnapshot() => GetSnapshot();

    LauncherSettings ISettingsDraftOwner.GetSavedSnapshot() => GetSavedSnapshot();

    /// <summary>
    /// <see cref="ISettingsDraftOwner"/>：把落盘的归一化值收口成新的草稿与快照。
    /// 收口必须落在 UI 线程——<see cref="PropertyChanged"/> 直接驱动绑定与命令可用性（按钮在处理
    /// CanExecuteChanged 时会读 Button.Command），而落盘的续体在线程池线程上，就地收口会在绑定层
    /// 抛出 VerifyAccess。等待调度完成而非 Post：ADR-024 要求写入方返回时编辑器已经拿掉落盘值。
    /// </summary>
    async Task ISettingsDraftOwner.ApplyPersistedAsync(
        LauncherSettings persisted,
        CancellationToken cancellationToken)
    {
        // 无 Avalonia 应用（纯单元测试）时没有可调度的 UI 线程，就地收口。
        if (Application.Current is null || Dispatcher.UIThread.CheckAccess())
        {
            ApplySnapshot(persisted);
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(() => ApplySnapshot(persisted));
    }

    public void Discard()
    {
        if (!isDirty)
        {
            return;
        }

        DetachCurrentListeners();
        current = snapshot.DeepClone();
        AttachCurrentListeners();
        isDirty = false;
        OnPropertyChanged(nameof(Current));
        OnPropertyChanged(nameof(IsDirty));
    }

    // GameRuntime is a nested ObservableObject: edits like Current.GameRuntime.RunnerPath
    // never fire on LauncherSettings itself, so the editor must listen on the child too
    // or those changes would neither mark the session dirty nor notify CurrentPropertyChanged.
    private void AttachCurrentListeners()
    {
        current.PropertyChanged += OnCurrentPropertyChanged;
        current.GameRuntime.PropertyChanged += OnCurrentPropertyChanged;
    }

    private void DetachCurrentListeners()
    {
        current.PropertyChanged -= OnCurrentPropertyChanged;
        current.GameRuntime.PropertyChanged -= OnCurrentPropertyChanged;
    }

    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private void OnCurrentPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        CurrentPropertyChanged?.Invoke(this, e);
        OnPropertyChanged(nameof(Current));
        var newIsDirty = !current.HasSameSettingsState(snapshot);
        if (isDirty != newIsDirty)
        {
            isDirty = newIsDirty;
            OnPropertyChanged(nameof(IsDirty));
        }
    }
}
