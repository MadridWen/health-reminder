using System;
using System.IO;
using System.Runtime.InteropServices;

namespace MoveAndHydrate
{
    internal static partial class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct Rectangle
        {
            public int Left, Top, Right, Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MonitorInfo
        {
            public uint Size;
            public Rectangle Monitor, Work;
            public uint Flags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SystemInfo
        {
            public ushort Architecture, Reserved;
            public uint PageSize;
            public IntPtr MinimumAddress, MaximumAddress, ProcessorMask;
            public uint ProcessorCount, ProcessorType, AllocationGranularity;
            public ushort ProcessorLevel, ProcessorRevision;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Size
        {
            public int Width, Height;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct BitmapHeader
        {
            public uint Size;
            public int Width, Height;
            public ushort Planes, BitsPerPixel;
            public uint Compression, ImageSize;
            public int XPelsPerMeter, YPelsPerMeter;
            public uint ColorsUsed, ColorsImportant;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct Blend
        {
            public byte Operation, Flags, Opacity, AlphaFormat;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct OpenFileName
        {
            public int Size;
            public IntPtr Owner, Instance;
            public string Filter, CustomFilter;
            public int MaxCustomFilter, FilterIndex;
            public IntPtr File;
            public int MaxFile;
            public IntPtr FileTitle;
            public int MaxFileTitle;
            public string InitialDirectory, Title;
            public int Flags;
            public ushort FileOffset, FileExtension;
            public string DefaultExtension;
            public IntPtr CustomData, Hook, Template, Reserved;
            public uint ReservedValue, ExtendedFlags;
        }

        [DllImport("kernel32.dll")]
        private static extern void GetSystemInfo(out SystemInfo info);

        internal static string ProcessArchitecture()
        {
            SystemInfo info;
            GetSystemInfo(out info);
            switch (info.Architecture)
            {
                case 0: return "x86";
                case 9: return "x64";
                case 12: return "arm64";
                default: throw new IOException("当前处理器没有可用的 WebP 解码库。");
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern IntPtr LoadLibraryEx(string path, IntPtr file, uint flags);

        [DllImport("kernel32.dll", ExactSpelling = true)]
        internal static extern IntPtr GetProcAddress(IntPtr module, string name);

        [DllImport("kernel32.dll")]
        internal static extern bool FreeLibrary(IntPtr module);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromPoint(Point point, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

        internal static Rectangle WorkArea(Point point)
        {
            var info = new MonitorInfo { Size = (uint)Marshal.SizeOf(typeof(MonitorInfo)) };
            if (GetMonitorInfo(MonitorFromPoint(point, 2), ref info)) // MONITOR_DEFAULTTONEAREST
                return info.Work;
            return new Rectangle { Right = GetSystemMetrics(0), Bottom = GetSystemMetrics(1) };
        }

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int index);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool SystemParametersInfo(uint action, uint value, out int result, uint flags);

        internal static bool ClientAnimationsEnabled()
        {
            int enabled;
            return !SystemParametersInfo(0x1042, 0, out enabled, 0) || enabled != 0; // SPI_GETCLIENTAREAANIMATION
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern IntPtr LoadCursor(IntPtr instance, IntPtr resource);

        [DllImport("user32.dll")]
        internal static extern bool ShowWindow(IntPtr window, int command);

        [DllImport("user32.dll")]
        internal static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);

        [DllImport("user32.dll")]
        internal static extern IntPtr SetCapture(IntPtr window);

        [DllImport("user32.dll")]
        internal static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        internal static extern bool UpdateLayeredWindow(IntPtr window, IntPtr screen, ref Point destination, ref Size size,
            IntPtr source, ref Point origin, uint colorKey, ref Blend blend, uint flags);

        [DllImport("gdi32.dll")]
        internal static extern IntPtr CreateCompatibleDC(IntPtr dc);

        [DllImport("gdi32.dll")]
        internal static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapHeader header, uint usage,
            out IntPtr bits, IntPtr section, uint offset);

        [DllImport("gdi32.dll")]
        internal static extern IntPtr SelectObject(IntPtr dc, IntPtr drawingObject);

        [DllImport("gdi32.dll")]
        internal static extern bool DeleteObject(IntPtr drawingObject);

        [DllImport("gdi32.dll")]
        internal static extern bool DeleteDC(IntPtr dc);

        [DllImport("comdlg32.dll", CharSet = CharSet.Unicode)]
        private static extern bool GetOpenFileName(ref OpenFileName parameters);

        [DllImport("comdlg32.dll")]
        private static extern uint CommDlgExtendedError();

        internal static string ChooseWebP(IntPtr owner)
        {
            const int Capacity = 32768;
            IntPtr filename = Marshal.AllocHGlobal(Capacity * sizeof(char));
            try
            {
                Marshal.WriteInt16(filename, 0);
                var picker = new OpenFileName
                {
                    Size = Marshal.SizeOf(typeof(OpenFileName)),
                    Owner = owner,
                    Filter = "WebP 图片 (*.webp)\0*.webp\0\0",
                    FilterIndex = 1,
                    File = filename,
                    MaxFile = Capacity,
                    Title = "选择宠物 WebP 图片",
                    DefaultExtension = "webp",
                    Flags = 0x00081808 // OFN_EXPLORER | OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR
                };
                if (GetOpenFileName(ref picker))
                    return Marshal.PtrToStringUni(filename);
                if (CommDlgExtendedError() != 0)
                    throw new IOException("无法打开文件选择窗口。");
                return null;
            }
            finally
            {
                Marshal.FreeHGlobal(filename);
            }
        }

        [DllImport("user32.dll")]
        private static extern bool OpenClipboard(IntPtr owner);
        [DllImport("user32.dll")]
        private static extern bool EmptyClipboard();
        [DllImport("user32.dll")]
        private static extern IntPtr SetClipboardData(uint format, IntPtr memory);
        [DllImport("user32.dll")]
        private static extern bool CloseClipboard();
        [DllImport("kernel32.dll")]
        private static extern IntPtr GlobalAlloc(uint flags, UIntPtr count);
        [DllImport("kernel32.dll")]
        private static extern IntPtr GlobalLock(IntPtr memory);
        [DllImport("kernel32.dll")]
        private static extern bool GlobalUnlock(IntPtr memory);
        [DllImport("kernel32.dll")]
        private static extern IntPtr GlobalFree(IntPtr memory);

        internal static bool CopyText(IntPtr owner, string text)
        {
            if (!OpenClipboard(owner))
                return false;
            IntPtr memory = IntPtr.Zero;
            try
            {
                char[] characters = (text + "\0").ToCharArray();
                memory = GlobalAlloc(2, new UIntPtr((uint)(characters.Length * 2))); // GMEM_MOVEABLE
                if (memory == IntPtr.Zero)
                    return false;
                IntPtr destination = GlobalLock(memory);
                if (destination == IntPtr.Zero)
                    return false;
                Marshal.Copy(characters, 0, destination, characters.Length);
                GlobalUnlock(memory);
                if (!EmptyClipboard() || SetClipboardData(13, memory) == IntPtr.Zero) // CF_UNICODETEXT
                    return false;
                memory = IntPtr.Zero; // Clipboard now owns the allocation.
                return true;
            }
            finally
            {
                if (memory != IntPtr.Zero)
                    GlobalFree(memory);
                CloseClipboard();
            }
        }
    }
}
