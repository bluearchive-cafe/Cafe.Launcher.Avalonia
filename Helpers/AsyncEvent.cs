using System;
using System.Threading.Tasks;

namespace Cafe.Launcher.Avalonia.Helpers;

/// <summary>Invokes asynchronous multicast delegates without dropping tasks.</summary>
internal static class AsyncEvent
{
    /// <summary>Invokes every handler sequentially in subscription order.</summary>
    public static async Task InvokeSequentiallyAsync(Func<Task>? handlers)
    {
        if (handlers is null)
        {
            return;
        }

        foreach (Func<Task> handler in handlers.GetInvocationList())
        {
            await handler();
        }
    }

    /// <summary>Invokes every handler sequentially with the supplied argument.</summary>
    public static async Task InvokeSequentiallyAsync<T>(Func<T, Task>? handlers, T argument)
    {
        if (handlers is null)
        {
            return;
        }

        foreach (Func<T, Task> handler in handlers.GetInvocationList())
        {
            await handler(argument);
        }
    }
}
