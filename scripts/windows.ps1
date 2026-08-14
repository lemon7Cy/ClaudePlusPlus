param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Launch', 'Stop', 'OpenDevTools')]
    [string]$Action,

    [Parameter(Mandatory = $true)]
    [string]$AppUserModelId,

    [Parameter(Mandatory = $true)]
    [string]$ExecutablePath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Get-OwnedClaudeProcesses {
    $expectedPath = [IO.Path]::GetFullPath($ExecutablePath)
    @(Get-Process Claude -ErrorAction SilentlyContinue | Where-Object {
        try {
            [IO.Path]::GetFullPath($_.Path) -ieq $expectedPath
        }
        catch {
            $false
        }
    })
}

function Invoke-ClaudeActivation {
    $source = @'
using System;
using System.Runtime.InteropServices;

[ComImport, Guid("2E941141-7F97-4756-BA1D-9DECDE894A3D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IApplicationActivationManager
{
    [PreserveSig]
    int ActivateApplication(
        [MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
        [MarshalAs(UnmanagedType.LPWStr)] string arguments,
        uint options,
        out uint processId);

    [PreserveSig] int ActivateForFile(IntPtr appUserModelId, IntPtr itemArray, IntPtr verb, out uint processId);
    [PreserveSig] int ActivateForProtocol(IntPtr appUserModelId, IntPtr itemArray, out uint processId);
}

[ComImport, Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")]
class ApplicationActivationManager { }

public static class ClaudePackageActivator
{
    public static uint Activate(string appUserModelId)
    {
        var manager = (IApplicationActivationManager)(object)new ApplicationActivationManager();
        uint processId;
        int result = manager.ActivateApplication(appUserModelId, "", 0, out processId);
        Marshal.ThrowExceptionForHR(result);
        return processId;
    }
}
'@
    Add-Type -TypeDefinition $source
    [ClaudePackageActivator]::Activate($AppUserModelId)
}

function Open-ClaudeDevTools {
    $mainProcess = Get-OwnedClaudeProcesses |
        Where-Object { $_.MainWindowHandle -ne 0 } |
        Select-Object -First 1

    if (-not $mainProcess) {
        throw 'Claude main window was not found.'
    }

    $source = @'
using System;
using System.Runtime.InteropServices;

public static class ClaudeNativeInput
{
    [StructLayout(LayoutKind.Sequential)]
    struct INPUT
    {
        public uint type;
        public InputUnion data;
    }

    [StructLayout(LayoutKind.Explicit)]
    struct InputUnion
    {
        [FieldOffset(0)] public KEYBDINPUT keyboard;
        [FieldOffset(0)] public MOUSEINPUT mouse;
        [FieldOffset(0)] public HARDWAREINPUT hardware;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct KEYBDINPUT
    {
        public ushort virtualKey;
        public ushort scanCode;
        public uint flags;
        public uint time;
        public IntPtr extraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr extraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct HARDWAREINPUT
    {
        public uint message;
        public ushort parameterLow;
        public ushort parameterHigh;
    }

    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr windowHandle);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr windowHandle, int command);
    [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint count, INPUT[] inputs, int size);

    static INPUT Key(ushort virtualKey, uint flags)
    {
        return new INPUT
        {
            type = 1,
            data = new InputUnion
            {
                keyboard = new KEYBDINPUT { virtualKey = virtualKey, flags = flags }
            }
        };
    }

    public static void OpenDevTools(IntPtr windowHandle)
    {
        ShowWindow(windowHandle, 9);
        if (!SetForegroundWindow(windowHandle))
            throw new InvalidOperationException("Could not focus Claude window.");

        System.Threading.Thread.Sleep(450);

        const uint keyUp = 0x0002;
        var inputs = new[]
        {
            Key(0x11, 0), Key(0x12, 0), Key(0x49, 0),
            Key(0x49, keyUp), Key(0x12, keyUp), Key(0x11, keyUp)
        };
        uint sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
        if (sent != inputs.Length)
            throw new InvalidOperationException(
                "Could not send Ctrl+Alt+I. Win32=" + Marshal.GetLastWin32Error());
    }
}
'@
    Add-Type -TypeDefinition $source
    [ClaudeNativeInput]::OpenDevTools($mainProcess.MainWindowHandle)
}

switch ($Action) {
    'Launch' {
        $processId = Invoke-ClaudeActivation
        Write-Output "Activated PID=$processId"
    }
    'Stop' {
        $processes = Get-OwnedClaudeProcesses
        foreach ($process in $processes) {
            Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        }
        Write-Output "Stopped=$($processes.Count)"
    }
    'OpenDevTools' {
        Open-ClaudeDevTools
        Write-Output 'DevTools shortcut sent.'
    }
}
