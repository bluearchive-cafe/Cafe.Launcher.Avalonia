using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace Cafe.Launcher.UI.Controls.VideoBackground;

/// <summary>libmpv C ABI 与参数内存所有者；运行库由显式绝对路径加载，不改变进程搜索路径。</summary>
internal sealed class MpvNative : IDisposable
{
    private readonly nint library;
    private readonly List<nint> allocations = [];
    private static int activeLibraryCount;
    private bool disposed;

    internal int AllocationCount => allocations.Count;
    internal static int ActiveLibraryCount => Volatile.Read(ref activeLibraryCount);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate nint Create();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int Initialize(nint handle);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int SetString(nint handle, [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int Command(nint handle, nint arguments);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate nint GetString(nint handle, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void Release(nint handle);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int CreateRender(out nint context, nint handle, nint parameters);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int Render(nint context, nint parameters);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void UpdateCallback(nint context);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void SetUpdate(nint context, nint callback, nint callbackContext);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate nint GetProc(nint context, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate nint Wait(nint handle, double timeout);

    [StructLayout(LayoutKind.Sequential)]
    internal struct PlayerEvent
    {
        internal int Id;
        internal int Error;
        internal ulong ReplyUserData;
        internal nint Data;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Parameter(int type, nint data)
    {
        internal int Type = type;
        internal nint Data = data;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct GlInit(nint callback)
    {
        internal nint Callback = callback;
        internal nint Context = 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Framebuffer(int framebuffer, int width, int height)
    {
        internal int FramebufferId = framebuffer;
        internal int Width = width;
        internal int Height = height;
        internal int InternalFormat = 0;
    }

    internal readonly Create NewPlayer;
    internal readonly Initialize InitializePlayer;
    internal readonly SetString SetOption;
    internal readonly SetString SetProperty;
    internal readonly Command SendCommand;
    internal readonly GetString GetProperty;
    internal readonly Release Free;
    internal readonly Release DestroyPlayer;
    internal readonly CreateRender NewRender;
    internal readonly Render Draw;
    internal readonly Release DestroyRender;
    internal readonly SetUpdate SetUpdateCallback;
    internal readonly Wait WaitEvent;

    internal MpvNative(string path)
    {
        library = NativeLibrary.Load(path);
        Interlocked.Increment(ref activeLibraryCount);
        try
        {
            NewPlayer = Export<Create>("mpv_create");
            InitializePlayer = Export<Initialize>("mpv_initialize");
            SetOption = Export<SetString>("mpv_set_option_string");
            SetProperty = Export<SetString>("mpv_set_property_string");
            SendCommand = Export<Command>("mpv_command");
            GetProperty = Export<GetString>("mpv_get_property_string");
            Free = Export<Release>("mpv_free");
            DestroyPlayer = Export<Release>("mpv_terminate_destroy");
            NewRender = Export<CreateRender>("mpv_render_context_create");
            Draw = Export<Render>("mpv_render_context_render");
            DestroyRender = Export<Release>("mpv_render_context_free");
            SetUpdateCallback = Export<SetUpdate>("mpv_render_context_set_update_callback");
            WaitEvent = Export<Wait>("mpv_wait_event");
        }
        catch
        {
            NativeLibrary.Free(library);
            Interlocked.Decrement(ref activeLibraryCount);
            throw;
        }
    }

    private T Export<T>(string name) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));

    internal nint Allocate<T>(T value) where T : struct
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<T>());
        allocations.Add(pointer);
        Marshal.StructureToPtr(value, pointer, false);
        return pointer;
    }

    internal nint String(string text)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var bytes = System.Text.Encoding.UTF8.GetBytes(text + '\0');
        var pointer = Marshal.AllocHGlobal(bytes.Length);
        allocations.Add(pointer);
        Marshal.Copy(bytes, 0, pointer, bytes.Length);
        return pointer;
    }

    internal nint Parameters(params Parameter[] values)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var size = Marshal.SizeOf<Parameter>();
        var pointer = Marshal.AllocHGlobal(size * (values.Length + 1));
        allocations.Add(pointer);
        for (var i = 0; i <= values.Length; i++)
        {
            Marshal.StructureToPtr(i == values.Length ? default : values[i], pointer + i * size, false);
        }
        return pointer;
    }

    internal void Load(nint player, string path)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var arguments = Marshal.AllocHGlobal(IntPtr.Size * 3);
        var command = Marshal.StringToCoTaskMemUTF8("loadfile");
        var file = Marshal.StringToCoTaskMemUTF8(path);
        try
        {
            Marshal.WriteIntPtr(arguments, command);
            Marshal.WriteIntPtr(arguments, IntPtr.Size, file);
            Marshal.WriteIntPtr(arguments, IntPtr.Size * 2, 0);
            Check(SendCommand(player, arguments), "loadfile");
        }
        finally
        {
            Marshal.FreeHGlobal(arguments);
            Marshal.FreeCoTaskMem(command);
            Marshal.FreeCoTaskMem(file);
        }
    }

    internal string Read(nint player, string name)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var pointer = GetProperty(player, name);
        if (pointer == 0) return "unavailable";
        try { return Marshal.PtrToStringUTF8(pointer) ?? "unavailable"; }
        finally { Free(pointer); }
    }

    internal static void Check(int result, string operation)
    {
        if (result < 0) throw new InvalidOperationException($"libmpv {operation}: {result}");
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        foreach (var pointer in allocations) Marshal.FreeHGlobal(pointer);
        allocations.Clear();
        NativeLibrary.Free(library);
        Interlocked.Decrement(ref activeLibraryCount);
    }
}
