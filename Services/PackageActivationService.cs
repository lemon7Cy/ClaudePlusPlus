using System.Runtime.InteropServices;

namespace ClaudePlusPlus.Services;

internal static class PackageActivationService
{
    public static uint Activate(string appUserModelId, string arguments)
    {
        var manager = (IApplicationActivationManager)(object)new ApplicationActivationManager();
        var result = manager.ActivateApplication(appUserModelId, arguments, 0, out var processId);
        Marshal.ThrowExceptionForHR(result);
        return processId;
    }

    [ComImport]
    [Guid("2E941141-7F97-4756-BA1D-9DECDE894A3D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IApplicationActivationManager
    {
        [PreserveSig]
        int ActivateApplication(
            [MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
            [MarshalAs(UnmanagedType.LPWStr)] string arguments,
            uint options,
            out uint processId);

        [PreserveSig]
        int ActivateForFile(IntPtr appUserModelId, IntPtr itemArray, IntPtr verb, out uint processId);

        [PreserveSig]
        int ActivateForProtocol(IntPtr appUserModelId, IntPtr itemArray, out uint processId);
    }

    [ComImport]
    [Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")]
    private sealed class ApplicationActivationManager;
}
