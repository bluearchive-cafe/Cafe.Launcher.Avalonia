using System.ComponentModel;
using System.Windows.Input;
using Cafe.Launcher.Avalonia.Features.Shell;
using Cafe.Launcher.Avalonia.ViewModels;

namespace Cafe.Launcher.Avalonia.Tests;

public sealed class ModalRegistrarTests
{
    [Fact]
    public void Register_WhenVisibilityPropertyTurnsTrue_OpensKind()
    {
        var host = new ModalHostViewModel();
        using var registrar = new ModalRegistrar(host);
        var source = new FakeSource();
        var escape = new FakeCommand();
        registrar.Register(new ModalRegistration(
            ModalKind.Settings, source, nameof(FakeSource.Visible), () => source.Visible, new FakeContent(), escape));

        source.Visible = true;
        source.Raise(nameof(FakeSource.Visible));

        Assert.Equal(ModalKind.Settings, host.Top?.Kind);
    }

    [Fact]
    public void Register_WhenVisibilityPropertyTurnsFalse_ClosesKind()
    {
        var host = new ModalHostViewModel();
        using var registrar = new ModalRegistrar(host);
        var source = new FakeSource();
        registrar.Register(new ModalRegistration(
            ModalKind.Settings, source, nameof(FakeSource.Visible), () => source.Visible, new FakeContent(), new FakeCommand()));

        source.Visible = true;
        source.Raise(nameof(FakeSource.Visible));
        source.Visible = false;
        source.Raise(nameof(FakeSource.Visible));

        Assert.Null(host.Top);
    }

    [Fact]
    public void Register_WhenUnrelatedPropertyChanges_DoesNotYankLowerModalToTop()
    {
        var host = new ModalHostViewModel();
        using var registrar = new ModalRegistrar(host);
        var lowerSource = new FakeSource();
        var upperSource = new FakeSource();
        registrar.Register(new ModalRegistration(
            ModalKind.Settings, lowerSource, nameof(FakeSource.Visible), () => lowerSource.Visible, new FakeContent(), new FakeCommand()));
        registrar.Register(new ModalRegistration(
            ModalKind.Debug, upperSource, nameof(FakeSource.Visible), () => upperSource.Visible, new FakeContent(), new FakeCommand()));

        lowerSource.Visible = true;
        lowerSource.Raise(nameof(FakeSource.Visible));
        upperSource.Visible = true;
        upperSource.Raise(nameof(FakeSource.Visible));
        Assert.Equal(ModalKind.Debug, host.Top?.Kind);

        // 下层来源的不相关属性变更不得触发重新入栈，把 Settings 拉到 Debug 之上。
        lowerSource.Raise(nameof(FakeSource.Other));

        Assert.Equal(ModalKind.Debug, host.Top?.Kind);
    }

    [Fact]
    public void TryDispatchEscape_KnownKind_ExecutesRegisteredCommand()
    {
        var host = new ModalHostViewModel();
        using var registrar = new ModalRegistrar(host);
        var source = new FakeSource();
        var escape = new FakeCommand();
        registrar.Register(new ModalRegistration(
            ModalKind.Settings, source, nameof(FakeSource.Visible), () => source.Visible, new FakeContent(), escape));

        Assert.True(registrar.TryDispatchEscape(ModalKind.Settings));
        Assert.Equal(1, escape.ExecuteCount);
    }

    [Fact]
    public void TryDispatchEscape_UnknownKind_ReturnsFalse()
    {
        using var registrar = new ModalRegistrar(new ModalHostViewModel());

        Assert.False(registrar.TryDispatchEscape(ModalKind.Notice));
    }

    [Fact]
    public void Dispose_AfterDispose_StopsReactingToVisibilityChanges()
    {
        var host = new ModalHostViewModel();
        var registrar = new ModalRegistrar(host);
        var source = new FakeSource();
        registrar.Register(new ModalRegistration(
            ModalKind.Settings, source, nameof(FakeSource.Visible), () => source.Visible, new FakeContent(), new FakeCommand()));
        registrar.Dispose();

        source.Visible = true;
        source.Raise(nameof(FakeSource.Visible));

        Assert.Null(host.Top);
    }

    private sealed class FakeContent : IModalContentViewModel
    {
    }

    private sealed class FakeSource : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        public bool Visible { get; set; }

        public bool Other { get; set; }

        public void Raise(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private sealed class FakeCommand : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public int ExecuteCount { get; private set; }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => ExecuteCount++;
    }
}
