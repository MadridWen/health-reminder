using System;
using System.Runtime.InteropServices;
using System.Text;

namespace MoveAndHydrate
{
    internal static partial class Native
    {
        internal delegate IntPtr WindowProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct WindowClass
        {
            public uint Style;
            public WindowProc Proc;
            public int ClassExtra, WindowExtra;
            public IntPtr Instance, Icon, Cursor, Background;
            public string MenuName, ClassName;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Point
        {
            public int X, Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Message
        {
            public IntPtr Window;
            public uint Id;
            public IntPtr WParam, LParam;
            public uint Time;
            public Point Position;
            public uint Private;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct LastInput
        {
            public uint Size, Time;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct NotifyIcon
        {
            public uint Size;
            public IntPtr Window;
            public uint Id, Flags, Callback;
            public IntPtr Icon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string Tip;
            public uint State, StateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string Text;
            public uint Version;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string Title;
            public uint InfoFlags;
            public Guid Guid;
            public IntPtr BalloonIcon;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        internal static extern IntPtr GetModuleHandle(string name);

        [DllImport("kernel32.dll")]
        internal static extern ulong GetTickCount64();

        [DllImport("kernel32.dll")]
        internal static extern bool QueryUnbiasedInterruptTime(out ulong time);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern ushort RegisterClass(ref WindowClass windowClass);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern IntPtr CreateWindowEx(uint exStyle, string className, string title, uint style,
            int x, int y, int width, int height, IntPtr owner, IntPtr menu, IntPtr instance, IntPtr parameter);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern IntPtr FindWindow(string className, string title);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern uint RegisterWindowMessage(string message);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern IntPtr DefWindowProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern int GetMessage(out Message message, IntPtr window, uint first, uint last);

        [DllImport("user32.dll")]
        internal static extern bool TranslateMessage(ref Message message);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern IntPtr DispatchMessage(ref Message message);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        internal static extern void PostQuitMessage(int exitCode);

        [DllImport("user32.dll")]
        internal static extern bool DestroyWindow(IntPtr window);

        [DllImport("user32.dll")]
        internal static extern UIntPtr SetTimer(IntPtr window, UIntPtr id, uint interval, IntPtr callback);

        [DllImport("user32.dll")]
        internal static extern bool KillTimer(IntPtr window, UIntPtr id);

        [DllImport("user32.dll")]
        internal static extern bool GetLastInputInfo(ref LastInput lastInput);

        [DllImport("wtsapi32.dll")]
        internal static extern bool WTSRegisterSessionNotification(IntPtr window, uint flags);

        [DllImport("wtsapi32.dll")]
        internal static extern bool WTSUnRegisterSessionNotification(IntPtr window);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        internal static extern bool Shell_NotifyIcon(uint message, ref NotifyIcon data);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        internal static extern uint ExtractIconEx(string file, int index, out IntPtr large, out IntPtr small, uint count);

        [DllImport("user32.dll")]
        internal static extern bool DestroyIcon(IntPtr icon);

        [DllImport("user32.dll")]
        internal static extern IntPtr CreatePopupMenu();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern bool AppendMenu(IntPtr menu, uint flags, UIntPtr id, string label);

        [DllImport("user32.dll")]
        internal static extern bool DestroyMenu(IntPtr menu);

        [DllImport("user32.dll")]
        internal static extern uint TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr window, IntPtr parameters);

        [DllImport("user32.dll")]
        internal static extern bool SetForegroundWindow(IntPtr window);

        [DllImport("user32.dll")]
        internal static extern bool GetCursorPos(out Point point);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern IntPtr DialogBoxIndirectParam(IntPtr instance, IntPtr template, IntPtr owner,
            WindowProc dialogProc, IntPtr parameter);

        [DllImport("user32.dll")]
        internal static extern bool EndDialog(IntPtr dialog, IntPtr result);

        [DllImport("user32.dll")]
        internal static extern bool SetDlgItemInt(IntPtr dialog, int id, uint value, bool signed);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern bool SetDlgItemText(IntPtr dialog, int id, string value);

        [DllImport("user32.dll")]
        internal static extern bool CheckDlgButton(IntPtr dialog, int id, uint check);

        [DllImport("user32.dll")]
        internal static extern uint IsDlgButtonChecked(IntPtr dialog, int id);

        [DllImport("user32.dll")]
        internal static extern bool EnableWindow(IntPtr window, bool enabled);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern uint GetDlgItemText(IntPtr dialog, int id, StringBuilder text, int capacity);

        [DllImport("user32.dll")]
        internal static extern IntPtr GetDlgItem(IntPtr dialog, int id);

        [DllImport("user32.dll")]
        internal static extern IntPtr SetFocus(IntPtr window);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern int MessageBox(IntPtr owner, string text, string caption, uint type);
    }
}
