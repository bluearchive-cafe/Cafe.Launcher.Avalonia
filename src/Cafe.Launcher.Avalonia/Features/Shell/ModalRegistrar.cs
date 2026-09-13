using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows.Input;
using Cafe.Launcher.Avalonia.ViewModels;

namespace Cafe.Launcher.Avalonia.Features.Shell;

/// <summary>
/// One modal surface's registration: the kind, where its visibility flag lives,
/// what the stack shows for it, and which command Escape dispatches to. One
/// record replaces the watcher method, escape-switch arm, and mapping arm a
/// modal used to need across <c>ShellLifecycle</c>.
/// </summary>
internal sealed record ModalRegistration(
    ModalKind Kind,
    INotifyPropertyChanged Source,
    string VisibilityPropertyName,
    Func<bool> IsVisible,
    IModalContentViewModel Content,
    ICommand EscapeCommand);

/// <summary>
/// Drives the modal host stack from the registrations: a visibility property
/// flipping opens or closes its kind, and Escape dispatches to the top entry's
/// registered command. Only the registered visibility property itself triggers
/// a sync — unrelated property changes on the same source must never re-open a
/// lower entry and yank it above a modal stacked on top of it.
/// </summary>
internal sealed class ModalRegistrar : IDisposable
{
    private readonly ModalHostViewModel modalHost;
    private readonly List<ModalRegistration> registrations = [];

    public ModalRegistrar(ModalHostViewModel modalHost)
    {
        this.modalHost = modalHost;
    }

    public void Register(ModalRegistration registration)
    {
        registrations.Add(registration);
        registration.Source.PropertyChanged += OnSourcePropertyChanged;
    }

    /// <summary>Executes the escape command registered for <paramref name="kind"/>; false when unregistered.</summary>
    public bool TryDispatchEscape(ModalKind kind)
    {
        var registration = registrations.Find(item => item.Kind == kind);
        if (registration is null)
        {
            return false;
        }

        registration.EscapeCommand.Execute(null);
        return true;
    }

    private void OnSourcePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // 同一来源（如 DialogsViewModel）可承载多个模态的可见性旗标，
        // 必须遍历全部匹配的注册，而不是只取第一个 source 相同的。
        foreach (var registration in registrations)
        {
            if (ReferenceEquals(registration.Source, sender)
                && e.PropertyName == registration.VisibilityPropertyName)
            {
                Sync(registration);
            }
        }
    }

    private void Sync(ModalRegistration registration)
    {
        if (registration.IsVisible())
        {
            modalHost.Open(registration.Kind, registration.Content);
        }
        else
        {
            modalHost.Close(registration.Kind);
        }
    }

    public void Dispose()
    {
        foreach (var registration in registrations)
        {
            registration.Source.PropertyChanged -= OnSourcePropertyChanged;
        }

        registrations.Clear();
    }
}
