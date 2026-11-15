using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Cafe.Launcher.UI.Features.GameOperations;

/// <summary>Writes a Shell Link through the native COM ABI, which also works under Native AOT.</summary>
[SupportedOSPlatform("windows")]
internal static class WindowsShellLink
{
    private const uint ApartmentThreaded = 2;
    private const uint InProcessServer = 1;
    private const int ChangedApartmentMode = unchecked((int)0x80010106);

    // Windows SDK IShellLinkW and IPersistFile vtables, including inherited slots.
    private const int SetWorkingDirectorySlot = 9;
    private const int SetIconLocationSlot = 17;
    private const int SetPathSlot = 20;
    private const int SaveSlot = 6;

    private static readonly Guid ShellLinkClass = new("00021401-0000-0000-C000-000000000046");
    private static readonly Guid ShellLinkInterface = new("000214F9-0000-0000-C000-000000000046");
    private static readonly Guid PersistFileInterface = new("0000010B-0000-0000-C000-000000000046");

    public static void Save(string targetPath, string workingDirectory, string iconPath, string shortcutPath)
    {
        var initialization = CoInitializeEx(IntPtr.Zero, ApartmentThreaded);
        if (initialization != ChangedApartmentMode)
        {
            Marshal.ThrowExceptionForHR(initialization);
        }

        var shellLink = IntPtr.Zero;
        var persistFile = IntPtr.Zero;
        try
        {
            Marshal.ThrowExceptionForHR(CoCreateInstance(
                in ShellLinkClass, IntPtr.Zero, InProcessServer, in ShellLinkInterface, out shellLink));
            Marshal.ThrowExceptionForHR(GetMethod<SetString>(shellLink, SetPathSlot)(shellLink, targetPath));
            Marshal.ThrowExceptionForHR(GetMethod<SetString>(shellLink, SetWorkingDirectorySlot)(shellLink, workingDirectory));

            // Pin the icon to the game's main executable, not the loader wrapper.
            Marshal.ThrowExceptionForHR(GetMethod<SetStringAndInt>(shellLink, SetIconLocationSlot)(shellLink, iconPath, 0));
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(shellLink, in PersistFileInterface, out persistFile));
            // IPersistFile::Save takes a Win32 BOOL for fRemember.
            Marshal.ThrowExceptionForHR(GetMethod<SetStringAndInt>(persistFile, SaveSlot)(persistFile, shortcutPath, 1));
        }
        finally
        {
            if (persistFile != IntPtr.Zero)
            {
                _ = Marshal.Release(persistFile);
            }

            if (shellLink != IntPtr.Zero)
            {
                _ = Marshal.Release(shellLink);
            }

            // S_OK and S_FALSE both need balancing. RPC_E_CHANGED_MODE leaves the caller's apartment intact.
            if (initialization >= 0)
            {
                CoUninitialize();
            }
        }
    }

    private static T GetMethod<T>(IntPtr instance, int slot) where T : Delegate
    {
        var vtable = Marshal.ReadIntPtr(instance);
        return Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(vtable, slot * IntPtr.Size));
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int SetString(IntPtr instance, [MarshalAs(UnmanagedType.LPWStr)] string value);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int SetStringAndInt(IntPtr instance, [MarshalAs(UnmanagedType.LPWStr)] string value, int argument);

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern int CoInitializeEx(IntPtr reserved, uint coInit);

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern void CoUninitialize();

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern int CoCreateInstance(
        in Guid classId, IntPtr outer, uint context, in Guid interfaceId, out IntPtr instance);
}
