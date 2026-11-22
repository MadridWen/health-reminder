using System;
using System.IO;
using System.Runtime.InteropServices;

namespace MoveAndHydrate
{
    internal sealed class DialogTemplate : IDisposable
    {
        public const uint TabStop = 0x00010000;
        private readonly MemoryStream buffer = new MemoryStream();
        private readonly BinaryWriter writer;
        private ushort count;

        public DialogTemplate(string title, short width, short height, bool taskbar)
        {
            writer = new BinaryWriter(buffer);
            writer.Write((ushort)1);
            writer.Write((ushort)0xffff);
            writer.Write(0u); // Help ID
            writer.Write(taskbar ? 0x00040000u : 0u); // WS_EX_APPWINDOW
            writer.Write(0x80c808c0u); // WS_POPUP | WS_CAPTION | WS_SYSMENU | DS_CENTER | DS_MODALFRAME | DS_SETFONT
            writer.Write((ushort)0); // Control count is filled in before displaying.
            WriteBounds(0, 0, width, height);
            writer.Write((ushort)0); // No menu
            writer.Write((ushort)0); // Default dialog class
            WriteString(title);
            writer.Write((ushort)9);
            writer.Write((ushort)400);
            writer.Write((byte)0); // Italic
            writer.Write((byte)1); // DEFAULT_CHARSET
            WriteString("Microsoft YaHei UI");
        }

        public void Text(int id, short x, short y, short width, short height, string text)
        {
            Add(0x82, id, 0, x, y, width, height, text);
        }

        public void Button(int id, uint style, short x, short y, short width, short height, string text)
        {
            Add(0x80, id, TabStop | style, x, y, width, height, text);
        }

        public void Edit(int id, uint style, short x, short y, short width, short height)
        {
            Add(0x81, id, TabStop | style, x, y, width, height, "", 0x200); // WS_EX_CLIENTEDGE
        }

        private void Add(ushort controlClass, int id, uint style, short x, short y, short width,
            short height, string text, uint extendedStyle = 0)
        {
            while (buffer.Position % 4 != 0)
                writer.Write((byte)0);
            writer.Write(0u);
            writer.Write(extendedStyle);
            writer.Write(style | 0x50000000u); // WS_VISIBLE | WS_CHILD
            WriteBounds(x, y, width, height);
            writer.Write(id);
            writer.Write((ushort)0xffff);
            writer.Write(controlClass);
            WriteString(text);
            writer.Write((ushort)0);
            count++;
        }

        public IntPtr Run(IntPtr instance, IntPtr owner, Native.WindowProc dialogProc)
        {
            buffer.Position = 16; // DLGTEMPLATEEX.cDlgItems
            writer.Write(count);
            byte[] template = buffer.ToArray();
            IntPtr pointer = Marshal.AllocHGlobal(template.Length);
            try
            {
                Marshal.Copy(template, 0, pointer, template.Length);
                return Native.DialogBoxIndirectParam(instance, pointer, owner, dialogProc, IntPtr.Zero);
            }
            finally
            {
                GC.KeepAlive(dialogProc);
                Marshal.FreeHGlobal(pointer);
            }
        }

        private void WriteBounds(short x, short y, short width, short height)
        {
            writer.Write(x);
            writer.Write(y);
            writer.Write(width);
            writer.Write(height);
        }

        private void WriteString(string text)
        {
            foreach (char character in text)
                writer.Write((ushort)character);
            writer.Write((ushort)0);
        }

        public void Dispose()
        {
            writer.Dispose();
        }
    }
}
