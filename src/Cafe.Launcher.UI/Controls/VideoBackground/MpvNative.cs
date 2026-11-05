using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace Cafe.Launcher.UI.Controls.VideoBackground;

/// <summary>libmpv C ABI 与参数内存所有者；运行库由显式绝对路径加载，不改变进程搜索路径。</summary>
internal sealed class MpvNative : IDisposable
{
    private readonly nint library;
    private readonly List<nint> dependencies = [];
    private readonly List<nint> allocations = [];
    private static int activeLibraryCount;
    private bool disposed;
    private nint screenshotArguments;

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
    internal delegate int CommandResult(nint handle, nint arguments, out Node result);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void ReleaseNode(ref Node node);
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

    // mpv_node's union occupies eight bytes on the supported 64-bit runtimes.
    [StructLayout(LayoutKind.Explicit)]
    internal struct Node
    {
        [FieldOffset(0)] internal nint Pointer;
        [FieldOffset(0)] internal long Integer;
        [FieldOffset(8)] internal int Format;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NodeList
    {
        internal int Count;
        internal nint Values;
        internal nint Keys;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ByteArray
    {
        internal nint Data;
        internal nuint Size;
    }

    /// <summary>A copied screenshot with tightly packed BGRX rows; no native allocation escapes its owner.</summary>
    internal sealed record VideoFrame(int Width, int Height, byte[] Pixels);

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
    private readonly CommandResult sendCommandResult;
    private readonly ReleaseNode freeNode;
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
        try
        {
            // Prefer fixed system locations. Portable builds carry fallbacks for
            // missing libraries; bare names would also search untrusted paths.
            if (OperatingSystem.IsLinux() && Path.IsPathFullyQualified(path) &&
                File.Exists(Path.Combine(Path.GetDirectoryName(path)!, "libva.so.2")))
            {
                var directory = Path.GetDirectoryName(path)!;
                foreach (var name in new[] { "libasound.so.2", "libva.so.2", "libva-drm.so.2" })
                {
                    var loaded = false;
                    foreach (var candidate in VideoBackgroundRuntime.GetLinuxSystemLibraryPaths(RuntimeInformation.ProcessArchitecture, name))
                    {
                        if (!File.Exists(candidate) || !NativeLibrary.TryLoad(candidate, out var dependency)) continue;
                        dependencies.Add(dependency);
                        loaded = true;
                        break;
                    }
                    if (loaded) continue;
                    var fallback = Path.Combine(directory, name);
                    if (File.Exists(fallback) && NativeLibrary.TryLoad(fallback, out var bundled))
                        dependencies.Add(bundled);
                }
            }
            library = NativeLibrary.Load(path);
            Interlocked.Increment(ref activeLibraryCount);
        }
        catch
        {
            ReleaseDependencies();
            throw;
        }
        try
        {
            NewPlayer = Export<Create>("mpv_create");
            InitializePlayer = Export<Initialize>("mpv_initialize");
            SetOption = Export<SetString>("mpv_set_option_string");
            SetProperty = Export<SetString>("mpv_set_property_string");
            SendCommand = Export<Command>("mpv_command");
            sendCommandResult = Export<CommandResult>("mpv_command_ret");
            freeNode = Export<ReleaseNode>("mpv_free_node_contents");
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
            ReleaseDependencies();
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
        // libmpv interprets URI-looking relative names as protocols before
        // FFmpeg's nested-protocol restrictions apply. Always pass a file path.
        var absolutePath = Path.GetFullPath(path);
        var arguments = Marshal.AllocHGlobal(IntPtr.Size * 3);
        var command = Marshal.StringToCoTaskMemUTF8("loadfile");
        var file = Marshal.StringToCoTaskMemUTF8(absolutePath);
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

    /// <summary>Copies the paused video's frame via screenshot-raw; unavailable frames return null.</summary>
    internal VideoFrame? CaptureVideoFrame(nint player)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (screenshotArguments == 0)
        {
            screenshotArguments = Marshal.AllocHGlobal(IntPtr.Size * 3);
            allocations.Add(screenshotArguments);
            Marshal.WriteIntPtr(screenshotArguments, String("screenshot-raw"));
            Marshal.WriteIntPtr(screenshotArguments, IntPtr.Size, String("video"));
            // bgr0 is the default on both older and current mpv; 0.37 does not
            // accept the optional format argument introduced in later versions.
            Marshal.WriteIntPtr(screenshotArguments, IntPtr.Size * 2, 0);
        }

        if (sendCommandResult(player, screenshotArguments, out var result) < 0)
            return null;

        try
        {
            // screenshot-raw returns a node map; its pixels remain valid only until free_node_contents.
            // https://mpv.io/manual/master/#command-screenshot-raw
            var list = Marshal.PtrToStructure<NodeList>(result.Pointer);
            var fields = new Dictionary<string, Node>(StringComparer.Ordinal);
            for (var index = 0; index < list.Count; index++)
            {
                var key = Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(list.Keys, index * IntPtr.Size))!;
                fields.Add(key, Marshal.PtrToStructure<Node>(list.Values + index * Marshal.SizeOf<Node>()));
            }

            var width = checked((int)fields["w"].Integer);
            var height = checked((int)fields["h"].Integer);
            var stride = checked((int)fields["stride"].Integer);
            var bytes = Marshal.PtrToStructure<ByteArray>(fields["data"].Pointer);
            var rowBytes = checked(width * 4);
            var pixels = new byte[checked(rowBytes * height)];
            // Native screenshots can have padding or negative stride. Copy rows before releasing the node.
            for (var row = 0; row < height; row++)
                Marshal.Copy(bytes.Data + row * stride, pixels, row * rowBytes, rowBytes);
            return new VideoFrame(width, height, pixels);
        }
        finally
        {
            freeNode(ref result);
        }
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
        ReleaseDependencies();
    }

    private void ReleaseDependencies()
    {
        for (var index = dependencies.Count - 1; index >= 0; index--)
            NativeLibrary.Free(dependencies[index]);
        dependencies.Clear();
    }
}
