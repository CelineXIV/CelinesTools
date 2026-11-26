using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace C4TChat.Services;

internal static class TaskbarFlasher
{
    private const uint FlashwAll = 0x3;
    private const uint FlashwTimerNoFg = 0xC;

    [StructLayout(LayoutKind.Sequential)]
    private struct FlashWInfo
    {
        public uint CbSize;
        public IntPtr Hwnd;
        public uint DwFlags;
        public uint UCount;
        public uint DwTimeout;
    }

    [DllImport("user32.dll")]
    private static extern bool FlashWindowEx(ref FlashWInfo info);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    private static IntPtr gameWindow;

    public static bool IsGameInForeground()
    {
        try
        {
            if (gameWindow == IntPtr.Zero)
            {
                gameWindow = Process.GetCurrentProcess().MainWindowHandle;
            }

            return gameWindow != IntPtr.Zero && GetForegroundWindow() == gameWindow;
        }
        catch (Exception)
        {
            return true;
        }
    }

    public static void FlashIfInBackground()
    {
        try
        {
            if (gameWindow == IntPtr.Zero)
            {
                gameWindow = Process.GetCurrentProcess().MainWindowHandle;
            }

            if (gameWindow == IntPtr.Zero || GetForegroundWindow() == gameWindow)
            {
                return;
            }

            var info = new FlashWInfo
            {
                CbSize = (uint)Marshal.SizeOf<FlashWInfo>(),
                Hwnd = gameWindow,
                DwFlags = FlashwAll | FlashwTimerNoFg,
                UCount = uint.MaxValue,
                DwTimeout = 0,
            };
            FlashWindowEx(ref info);
        }
        catch (Exception)
        {
        }
    }
}
