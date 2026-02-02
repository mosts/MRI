using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using MRI;

namespace MRI.praesidia
{
    class AutoChat
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct WindowInfo
        {
            public IntPtr hwnd;
            public string title;
        }

        static List<WindowInfo> matching_windows_global = new List<WindowInfo>();

        private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxLength);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        [DllImport("user32.dll")]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [StructLayout(LayoutKind.Sequential)]
        struct INPUT
        {
            public uint type;
            public InputUnion ki;
        }

        [StructLayout(LayoutKind.Explicit)]
        struct InputUnion
        {
            [FieldOffset(0)]
            public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public UIntPtr dwExtraInfo;
        }

        const uint INPUT_KEYBOARD = 1;
        const uint KEYEVENTF_KEYUP = 0x0002;
        const byte VK_RETURN = 0x0D;
        const byte VK_SLASH = 0xBF;

        static bool CaseInsensitiveFind(string str, string substr)
        {
            return str?.IndexOf(substr ?? "", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static bool ShouldIgnoreWindow(string title, List<string> ignore_list)
        {
            foreach (var name in ignore_list)
            {
                if (CaseInsensitiveFind(title, name))
                    return true;
            }
            return false;
        }

        static bool EnumWindowsProcImpl(IntPtr hwnd, IntPtr lParam)
        {
            GetWindowThreadProcessId(hwnd, out uint window_pid);
            uint current_pid = (uint)Process.GetCurrentProcess().Id;

            if (window_pid == current_pid)
                return true;

            int length = GetWindowTextLength(hwnd);
            if (length == 0)
                return true;

            var title_builder = new StringBuilder(length + 1);
            GetWindowText(hwnd, title_builder, title_builder.Capacity);
            string title = title_builder.ToString();

            var data = (EnumData?)GCHandle.FromIntPtr(lParam).Target;

            if (data == null || !CaseInsensitiveFind(title, data.PartialName))
                return true;

            if (ShouldIgnoreWindow(title, data.IgnoreList))
                return true;

            matching_windows_global.Add(new WindowInfo { hwnd = hwnd, title = title });

            return true;
        }

        class EnumData
        {
            public string PartialName = "";
            public List<string> IgnoreList = new List<string>();
        }

        static void PatternWindowFinderAll(string partial_name, List<string> ignore_list)
        {
            matching_windows_global.Clear();

            var data = new EnumData
            {
                PartialName = partial_name,
                IgnoreList = ignore_list
            };

            var handle = GCHandle.Alloc(data);
            try
            {
                EnumWindows(EnumWindowsProcImpl, GCHandle.ToIntPtr(handle));
            }
            finally
            {
                handle.Free();
            }
        }

        public static string GetExeNameFromHwnd(IntPtr hwnd)
        {
            try
            {
                GetWindowThreadProcessId(hwnd, out uint processId);
                var process = Process.GetProcessById((int)processId);
                return System.IO.Path.GetFileName(process.MainModule?.FileName) ?? "[Unknown]";
            }
            catch (Exception ex)
            {
                return $"[Error: {ex.Message}]";
            }
        }

        public static async Task TypeText(string text)
        {
            foreach (char c in text)
            {
                // Use VK code for the character
                short vk = VkKeyScan(c);
                byte vkCode = (byte)(vk & 0xFF);
                bool needsShift = (vk & 0x100) != 0;

                if (needsShift)
                {
                    keybd_event(0x10, 0, 0, UIntPtr.Zero); // Shift down
                    await Task.Delay(10);
                }

                keybd_event(vkCode, 0, 0, UIntPtr.Zero); // Key down
                await Task.Delay(30);
                keybd_event(vkCode, 0, KEYEVENTF_KEYUP, UIntPtr.Zero); // Key up
                await Task.Delay(30);

                if (needsShift)
                {
                    keybd_event(0x10, 0, KEYEVENTF_KEYUP, UIntPtr.Zero); // Shift up
                    await Task.Delay(10);
                }
            }
        }

        [DllImport("user32.dll")]
        static extern short VkKeyScan(char ch);

        static async Task SendChatToWindow(IntPtr hwnd, string message)
        {
            SetForegroundWindow(hwnd);
            await Task.Delay(100);

            // Press / to open chat
            keybd_event(VK_SLASH, 0, 0, UIntPtr.Zero);
            await Task.Delay(50);
            keybd_event(VK_SLASH, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            await Task.Delay(200);

            // Type the message
            await TypeText(message);
            await Task.Delay(100);

            // Press Enter to send
            keybd_event(VK_RETURN, 0, 0, UIntPtr.Zero);
            await Task.Delay(50);
            keybd_event(VK_RETURN, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            await Task.Delay(50);
        }

        public static async Task SendChatToAllRoblox(string message)
        {
            string current_name = Process.GetCurrentProcess().ProcessName;
            var ignore = new List<string> { current_name };
            var current = GetForegroundWindow();

            PatternWindowFinderAll("Roblox", ignore);

            foreach (var win in matching_windows_global)
            {
                if (GetExeNameFromHwnd(win.hwnd) != "RobloxPlayerBeta.exe") { continue; }
                await SendChatToWindow(win.hwnd, message);
                await Task.Delay(500); // Delay between windows
            }

            // Restore original window
            if (current != IntPtr.Zero)
            {
                await Task.Delay(100);
                SetForegroundWindow(current);
            }
        }

        public static async Task AutoChatLoop(MainWindow window, string message)
        {
            while (window.AutoRepeatCheckBox?.IsChecked == true)
            {
                await SendChatToAllRoblox(message);
                await Task.Delay(10000); // Wait 10 seconds
            }
        }
    }
}
