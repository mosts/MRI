using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using MRI;

namespace MRI.praesidia
{
    class AntiAFK
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
        private static extern bool IsWindowVisible(IntPtr hWnd);

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
        const uint KEYEVENTF_SCANCODE = 0x0008;

        const byte VK_SPACE = 0x20;
        const byte VK_W = 0x57;
        const byte VK_A = 0x41;
        const byte VK_S = 0x53;
        const byte VK_D = 0x44;

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

            if (matching_windows_global.Count == 0)
            {
                Debug.WriteLine("No matching windows found!");
            }
        }

        public static string GetExeNameFromHwnd(IntPtr hwnd)
        {
            try
            {
                // Get the process ID from the window handle
                GetWindowThreadProcessId(hwnd, out uint processId);

                // Get the process by ID
                var process = Process.GetProcessById((int)processId);

                // Return the executable file name
                return System.IO.Path.GetFileName(process.MainModule?.FileName) ?? "[Unknown]";
            }
            catch (Exception ex)
            {
                return $"[Error: {ex.Message}]";
            }
        }

        public async static Task PressButton(byte vk)
        {
            keybd_event(vk, 0x39, 0, UIntPtr.Zero);
            await Task.Delay(30);
            keybd_event(vk, 0x39, KEYEVENTF_KEYUP, UIntPtr.Zero);

            INPUT input = new INPUT
            {
                type = INPUT_KEYBOARD,
                ki = new InputUnion
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = 0,
                        wScan = 0x39,
                        dwFlags = KEYEVENTF_SCANCODE,
                        time = 0,
                        dwExtraInfo = UIntPtr.Zero
                    }
                }
            };

            SendInput(1, new[] { input }, Marshal.SizeOf(typeof(INPUT)));
            await Task.Delay(30);

            input.ki.ki.dwFlags = KEYEVENTF_SCANCODE | KEYEVENTF_KEYUP;
            for (int i = 0; i < 5; i++)
            {
                SendInput(1, new[] { input }, Marshal.SizeOf(typeof(INPUT)));
            }
        }
        static async Task AntiAfkCall(IntPtr hwnd)
        {
            SetForegroundWindow(hwnd);
            await Task.Delay(100);
            
            // Jump a few times
            await PressButton(VK_SPACE);
            await Task.Delay(200);
            await PressButton(VK_SPACE);
            await Task.Delay(200);
            
            // Move forward
            keybd_event(VK_W, 0, 0, UIntPtr.Zero);
            await Task.Delay(500);
            keybd_event(VK_W, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            await Task.Delay(100);
            
            // Move backward
            keybd_event(VK_S, 0, 0, UIntPtr.Zero);
            await Task.Delay(500);
            keybd_event(VK_S, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            await Task.Delay(100);
            
            // Move left
            keybd_event(VK_A, 0, 0, UIntPtr.Zero);
            await Task.Delay(300);
            keybd_event(VK_A, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            await Task.Delay(100);
            
            // Move right
            keybd_event(VK_D, 0, 0, UIntPtr.Zero);
            await Task.Delay(300);
            keybd_event(VK_D, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            await Task.Delay(100);
            
            // Final jump
            await PressButton(VK_SPACE);
        }
        public static async Task AntiAFKLoop(MainWindow window)
        {

            if (window.AntiAFKCheckBox.IsChecked == false) { return; }
            await Task.Delay(1000);
            string current_name = Process.GetCurrentProcess().ProcessName;
            Debug.WriteLine(current_name);

            var ignore = new List<string> { current_name };
            var current = GetForegroundWindow();

            PatternWindowFinderAll("Roblox", ignore);
            foreach (var win in matching_windows_global)
            {
                if (window.AntiAFKCheckBox.IsChecked == false) { break; }
                if (GetExeNameFromHwnd(win.hwnd) != "RobloxPlayerBeta.exe") { continue; }
                await AntiAfkCall(win.hwnd);
            }

            if (current != IntPtr.Zero)
            {
                await Task.Delay(10);
                SetForegroundWindow(current);
            }

            while (true)
            {
                // every 10 minutes
                await Task.Delay(1000 * 60 * 10);
                if (window.AntiAFKCheckBox.IsChecked == false) { break; }
                current = GetForegroundWindow();

                PatternWindowFinderAll("Roblox", ignore);
                foreach (var win in matching_windows_global)
                {
                    if (window.AntiAFKCheckBox.IsChecked == false) { break; }
                    if (GetExeNameFromHwnd(win.hwnd) != "RobloxPlayerBeta.exe") { continue; }
                    await AntiAfkCall(win.hwnd);
                }

                if (current != IntPtr.Zero)
                {
                    await Task.Delay(10);
                    SetForegroundWindow(current);
                }
            }
        }
    }
}
