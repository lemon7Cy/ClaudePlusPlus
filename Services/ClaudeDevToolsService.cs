using System.Runtime.InteropServices;

namespace ClaudePlusPlus.Services;

internal static class ClaudeDevToolsService
{
    public static async Task OpenOrFocusAsync(string executablePath)
    {
        var ownedProcesses = ClaudeProcessService.FindOwnedProcesses(executablePath);
        try
        {
            var mainProcess = ownedProcesses.FirstOrDefault(process => process.MainWindowHandle != IntPtr.Zero)
                              ?? throw new InvalidOperationException("未找到 Claude 主窗口，请先启动 Claude。");

            ShowWindow(mainProcess.MainWindowHandle, 9);
            if (!SetForegroundWindow(mainProcess.MainWindowHandle))
            {
                throw new InvalidOperationException("无法将 Claude 窗口切到前台。");
            }

            await Task.Delay(500);
            SendCtrlAltI();
        }
        finally
        {
            foreach (var process in ownedProcesses)
            {
                process.Dispose();
            }
        }
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr windowHandle);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr windowHandle, int command);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint inputCount, Input[] inputs, int inputSize);

    private static void SendCtrlAltI()
    {
        const ushort controlKey = 0x11;
        const ushort altKey = 0x12;
        const ushort iKey = 0x49;
        const uint keyboardInput = 1;
        const uint keyUp = 0x0002;

        var inputs = new[]
        {
            CreateKeyboardInput(controlKey, 0),
            CreateKeyboardInput(altKey, 0),
            CreateKeyboardInput(iKey, 0),
            CreateKeyboardInput(iKey, keyUp),
            CreateKeyboardInput(altKey, keyUp),
            CreateKeyboardInput(controlKey, keyUp)
        };

        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
        if (sent != inputs.Length)
        {
            throw new InvalidOperationException(
                $"Windows 未能完整发送 Ctrl+Alt+I（Win32={Marshal.GetLastWin32Error()}）。");
        }

        return;

        static Input CreateKeyboardInput(ushort virtualKey, uint flags) => new()
        {
            Type = keyboardInput,
            Data = new InputUnion
            {
                Keyboard = new KeyboardInput
                {
                    VirtualKey = virtualKey,
                    Flags = flags
                }
            }
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public KeyboardInput Keyboard;

        [FieldOffset(0)]
        public MouseInput Mouse;

        [FieldOffset(0)]
        public HardwareInput Hardware;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HardwareInput
    {
        public uint Message;
        public ushort ParameterLow;
        public ushort ParameterHigh;
    }
}
