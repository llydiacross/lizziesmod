<#
.SYNOPSIS
Sends one command to the locally running 7 Days To Die client console.

.DESCRIPTION
Activates the native client window, opens its F1 console unless it is already
open, types the supplied command through SendInput, and submits it. The script
refuses to send input unless the foreground window belongs to 7DaysToDie.
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [ValidateNotNullOrEmpty()]
    [string]$Command,
    [switch]$ConsoleAlreadyOpen,
    [ValidateRange(0, 1000)]
    [int]$ConsoleOpenDelayMilliseconds = 150
)

$ErrorActionPreference = 'Stop'
$Command = $Command.Trim()

if ([string]::IsNullOrWhiteSpace($Command)) {
    throw 'Command must contain non-whitespace text.'
}

if ($Command.IndexOf([char]0) -ge 0) {
    throw 'Command cannot contain a null character.'
}

if ($null -eq ('LizziesModConsoleInputV3' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

public static class LizziesModConsoleInputV3
{
    private const uint InputKeyboard = 1;
    private const uint KeyEventKeyUp = 0x0002;
    private const uint KeyEventUnicode = 0x0004;

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Union;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public KeybdInput Keyboard;

        [FieldOffset(0)]
        public MouseInput Mouse;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeybdInput
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
        public int DeltaX;
        public int DeltaY;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint numberOfInputs, Input[] inputs, int inputSize);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool BringWindowToTop(IntPtr window);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool AttachThreadInput(uint attachThreadId, uint attachToThreadId, bool attach);

    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr window);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    public static uint GetForegroundProcessId()
    {
        uint processId;
        GetWindowThreadProcessId(GetForegroundWindow(), out processId);
        return processId;
    }

    public static bool TryActivate(IntPtr window)
    {
        ShowWindow(window, 9);
        BringWindowToTop(window);
        if (SetForegroundWindow(window)) return true;

        IntPtr foregroundWindow = GetForegroundWindow();
        uint foregroundProcessId;
        uint foregroundThreadId = GetWindowThreadProcessId(foregroundWindow, out foregroundProcessId);
        uint targetProcessId;
        uint targetThreadId = GetWindowThreadProcessId(window, out targetProcessId);
        uint currentThreadId = GetCurrentThreadId();
        bool attachedToForeground = false;
        bool attachedToTarget = false;

        try
        {
            if (foregroundThreadId != 0 && foregroundThreadId != currentThreadId)
            {
                attachedToForeground = AttachThreadInput(currentThreadId, foregroundThreadId, true);
            }

            if (targetThreadId != 0 && targetThreadId != currentThreadId && targetThreadId != foregroundThreadId)
            {
                attachedToTarget = AttachThreadInput(currentThreadId, targetThreadId, true);
            }

            BringWindowToTop(window);
            SetForegroundWindow(window);
            SetFocus(window);
            return GetForegroundWindow() == window;
        }
        finally
        {
            if (attachedToTarget) AttachThreadInput(currentThreadId, targetThreadId, false);
            if (attachedToForeground) AttachThreadInput(currentThreadId, foregroundThreadId, false);
        }
    }

    public static void SendVirtualKey(ushort virtualKey)
    {
        Send(new[]
        {
            CreateKeyboardInput(virtualKey, 0, 0),
            CreateKeyboardInput(virtualKey, 0, KeyEventKeyUp)
        });
    }

    public static void SendText(string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        var inputs = new Input[text.Length * 2];
        int inputIndex = 0;
        foreach (char character in text)
        {
            inputs[inputIndex++] = CreateKeyboardInput(0, (ushort)character, KeyEventUnicode);
            inputs[inputIndex++] = CreateKeyboardInput(0, (ushort)character, KeyEventUnicode | KeyEventKeyUp);
        }

        Send(inputs);
    }

    private static Input CreateKeyboardInput(ushort virtualKey, ushort scanCode, uint flags)
    {
        return new Input
        {
            Type = InputKeyboard,
            Union = new InputUnion
            {
                Keyboard = new KeybdInput
                {
                    VirtualKey = virtualKey,
                    ScanCode = scanCode,
                    Flags = flags,
                    Time = 0,
                    ExtraInfo = IntPtr.Zero
                }
            }
        };
    }

    private static void Send(Input[] inputs)
    {
        int inputSize = Marshal.SizeOf(typeof(Input));
        if (inputSize != 40)
        {
            throw new InvalidOperationException("The native INPUT structure must be 40 bytes on this 64-bit client.");
        }

        uint sent = SendInput((uint)inputs.Length, inputs, inputSize);
        if (sent != inputs.Length)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows did not deliver all console input events.");
        }
    }
}
'@
}

$client = Get-Process -Name '7DaysToDie' -ErrorAction SilentlyContinue |
    Where-Object { $_.MainWindowHandle -ne [IntPtr]::Zero } |
    Select-Object -First 1

if ($null -eq $client) {
    throw 'No visible 7 Days To Die client window was found. Launch the client before sending a console command.'
}

$target = "7DaysToDie (PID $($client.Id))"
if (-not $PSCmdlet.ShouldProcess($target, "Send console command '$Command'")) {
    return
}

$activationShell = New-Object -ComObject WScript.Shell
$activationShell.AppActivate($client.Id) | Out-Null
[LizziesModConsoleInputV3]::TryActivate($client.MainWindowHandle) | Out-Null

if ([LizziesModConsoleInputV3]::GetForegroundProcessId() -ne [uint32]$client.Id) {
    throw "Could not activate $target. No console input was sent."
}

if (-not $ConsoleAlreadyOpen) {
    [LizziesModConsoleInputV3]::SendVirtualKey(0x70)
    if ($ConsoleOpenDelayMilliseconds -gt 0) {
        Start-Sleep -Milliseconds $ConsoleOpenDelayMilliseconds
    }
}

[LizziesModConsoleInputV3]::SendText($Command)
[LizziesModConsoleInputV3]::SendVirtualKey(0x0D)
Write-Host "Submitted console command to ${target}: $Command"