namespace Cafe.Launcher.Avalonia.Services;

internal sealed class ShutdownDeferral
{
    private bool isDeferred;
    private bool isCommitted;

    public bool ShouldCancelRequest => isDeferred && !isCommitted;

    public void Defer() => isDeferred = true;

    public void Commit() => isCommitted = true;
}
