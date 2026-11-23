using System;
using System.Collections.Generic;
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
    private readonly HashSet<INotifyPropertyChanged> nestedSources = new(ReferenceEqualityComparer.Instance);

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

    /// <summary>只比较当前编辑范围的变化；导航与窗口几何不属于用户设置草稿。</summary>
    public bool IsScopeDirty(string? gameId)
    {
        var merged = snapshot.DeepClone();
        merged.ApplyDraftChanges(current, snapshot, gameId);
        return !merged.HasSameSettingsState(snapshot);
    }

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

    async Task ISettingsDraftOwner.ApplyPersistedUpdateAsync(
        LauncherSettings persisted,
        CancellationToken cancellationToken)
    {
        if (Application.Current is null || Dispatcher.UIThread.CheckAccess())
        {
            ApplyUpdate(persisted);
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(() => ApplyUpdate(persisted));
    }

    /// <summary>Refreshes saved values during a live edit session without discarding its draft.</summary>
    public void ApplySavedUpdate(LauncherSettings persisted) => ApplyUpdate(persisted);

    private void ApplyUpdate(LauncherSettings persisted)
    {
        var merged = persisted.DeepClone();
        merged.ApplyDraftChanges(current, snapshot, gameId: null);
        foreach (var gameId in current.Games.Keys)
        {
            merged.ApplyDraftChanges(current, snapshot, gameId);
        }

        DetachCurrentListeners();
        current = merged;
        snapshot = persisted.DeepClone();
        AttachCurrentListeners();
        isDirty = !current.HasSameSettingsState(snapshot);
        OnPropertyChanged(nameof(Current));
        OnPropertyChanged(nameof(IsDirty));
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
        if (current.Games is null)
        {
            return;
        }

        foreach (var game in current.Games.Values)
        {
            if (game is null)
            {
                continue;
            }

            AttachNested(game);
            if (game.GameRuntime is not null)
            {
                AttachNested(game.GameRuntime);
            }
        }
    }

    private void AttachNested(INotifyPropertyChanged source)
    {
        if (nestedSources.Add(source))
        {
            source.PropertyChanged += OnCurrentPropertyChanged;
        }
    }

    private void DetachCurrentListeners()
    {
        current.PropertyChanged -= OnCurrentPropertyChanged;
        foreach (var source in nestedSources)
        {
            source.PropertyChanged -= OnCurrentPropertyChanged;
        }

        nestedSources.Clear();
    }

    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private void OnCurrentPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // The fixed BA aliases already raised the same change on their game object.
        // Observe the owning object once while preserving root notifications for other consumers.
        if (ReferenceEquals(sender, current)
            && e.PropertyName is nameof(LauncherSettings.GamePath)
                or nameof(LauncherSettings.PatchUrlGroup)
                or nameof(LauncherSettings.LaunchCheckMode)
                or nameof(LauncherSettings.GameRuntime)
                or nameof(LauncherSettings.AfterLaunchBehavior)
                or nameof(LauncherSettings.ResourcePanelUid)
                or nameof(LauncherSettings.ResourcePanelUidSource))
        {
            return;
        }

        if ((ReferenceEquals(sender, current) && e.PropertyName == nameof(LauncherSettings.Games))
            || (sender is GameSettings && e.PropertyName == nameof(GameSettings.GameRuntime)))
        {
            DetachCurrentListeners();
            AttachCurrentListeners();
        }

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
