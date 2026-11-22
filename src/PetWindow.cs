using System;
using System.IO;
using System.Runtime.InteropServices;

namespace MoveAndHydrate
{
    internal sealed class PetWindow : IDisposable
    {
        private const string ClassName = "MoveAndHydrate.DesktopPet";
        private const int Size = PetFrame.Size;
        private static readonly UIntPtr Timer = new UIntPtr(1);

        private readonly IntPtr instance;
        private readonly Action<Native.Point> showMenu;
        private readonly Action<Native.Point> savePosition;
        private readonly Native.WindowProc windowProc;
        private IntPtr window, dc, bitmap, pixels, previousBitmap;
        private Native.Point position, dragOffset;
        private bool hasPosition, registered, dragging, dragMoved, paused, reducedMotion, awaitingWater, reacting;
        private PetArt art;
        private PetFrame[] sequence;
        private int frameIndex;

        public PetWindow(IntPtr instance, Native.Point? savedPosition, Action<Native.Point> showMenu,
            Action<Native.Point> savePosition)
        {
            this.instance = instance;
            this.showMenu = showMenu;
            this.savePosition = savePosition;
            hasPosition = savedPosition.HasValue;
            position = savedPosition.GetValueOrDefault();
            windowProc = ProcessMessage;
        }

        public void Show(PetArt next)
        {
            bool created = window == IntPtr.Zero;
            if (created)
                CreateWindow();
            PetArt previous = art;
            art = next;
            reducedMotion = !Native.ClientAnimationsEnabled();
            reacting = false;
            try
            {
                if (!Start(Baseline()))
                    throw new IOException("无法显示桌面宠物窗口。");
                if (!paused)
                    Native.ShowWindow(window, 4); // SW_SHOWNOACTIVATE
            }
            catch
            {
                if (created)
                    Dispose();
                else
                {
                    art = previous;
                    Start(Baseline());
                }
                throw;
            }
        }

        public void SetAwaitingWater(bool awaiting)
        {
            if (awaitingWater == awaiting)
                return;
            awaitingWater = awaiting;
            if (art != null && !reacting)
                Start(Baseline());
        }

        public void Celebrate()
        {
            if (art == null || paused || reducedMotion)
                return;
            PetFrame[] jump = art.Jump;
            if (jump == null || jump.Length == 0)
            {
                PetFrame pose = art.Idle[FirstVisible(art.Idle)];
                jump = new[]
                {
                    new PetFrame(pose.Pixels, 85, true, -5),
                    new PetFrame(pose.Pixels, 145, true, -12),
                    new PetFrame(pose.Pixels, 85, true, -5)
                };
            }
            reacting = true;
            Start(jump);
        }

        public void SetPaused(bool pause)
        {
            if (paused == pause)
                return;
            paused = pause;
            if (window == IntPtr.Zero)
                return;
            if (pause)
            {
                Native.KillTimer(window, Timer);
                Native.ShowWindow(window, 0);
            }
            else
            {
                reacting = false;
                Start(Baseline());
                Native.ShowWindow(window, 4);
            }
        }

        private void CreateWindow()
        {
            if (!registered)
            {
                var windowClass = new Native.WindowClass
                {
                    ClassName = ClassName,
                    Instance = instance,
                    Proc = windowProc,
                    Cursor = Native.LoadCursor(IntPtr.Zero, new IntPtr(32649)) // IDC_HAND
                };
                registered = Native.RegisterClass(ref windowClass) != 0;
                if (!registered)
                    throw new IOException("无法注册桌面宠物窗口。");
            }

            if (!hasPosition)
            {
                Native.Point cursor;
                Native.GetCursorPos(out cursor);
                Native.Rectangle work = Native.WorkArea(cursor);
                position = new Native.Point { X = work.Right - Size - 28, Y = work.Bottom - Size - 120 };
                hasPosition = true;
            }
            ClampPosition();
            window = Native.CreateWindowEx(0x08080088, ClassName, "桌面宠物", 0x80000000, // Layered, no activate, tool, topmost
                position.X, position.Y, Size, Size, IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);
            if (window == IntPtr.Zero)
                throw new IOException("无法创建桌面宠物窗口。");

            dc = Native.CreateCompatibleDC(IntPtr.Zero);
            var header = new Native.BitmapHeader { Size = 40, Width = Size, Height = -Size, Planes = 1, BitsPerPixel = 32 };
            bitmap = Native.CreateDIBSection(dc, ref header, 0, out pixels, IntPtr.Zero, 0);
            if (dc == IntPtr.Zero || bitmap == IntPtr.Zero || pixels == IntPtr.Zero)
            {
                Dispose();
                throw new IOException("无法创建宠物图像缓冲区。");
            }
            previousBitmap = Native.SelectObject(dc, bitmap);
            if (previousBitmap == IntPtr.Zero || previousBitmap == new IntPtr(-1))
            {
                Dispose();
                throw new IOException("无法准备宠物图像缓冲区。");
            }
        }

        private PetFrame[] Baseline()
        {
            return awaitingWater && art.Waiting != null && art.Waiting.Length > 0 ? art.Waiting : art.Idle;
        }

        private static int FirstVisible(PetFrame[] frames)
        {
            for (int i = 0; i < frames.Length; i++)
                if (frames[i].HasVisiblePixels)
                    return i;
            return 0;
        }

        private bool Start(PetFrame[] next)
        {
            Native.KillTimer(window, Timer);
            sequence = next;
            frameIndex = reacting ? 0 : FirstVisible(next);
            return DrawCurrentFrame();
        }

        private bool DrawCurrentFrame()
        {
            PetFrame frame = sequence[frameIndex];
            Marshal.Copy(frame.Pixels, 0, pixels, frame.Pixels.Length);
            var target = new Native.Point { X = position.X, Y = position.Y + frame.VerticalOffset };
            var size = new Native.Size { Width = Size, Height = Size };
            var source = new Native.Point();
            var blend = new Native.Blend { Opacity = 255, AlphaFormat = 1 };
            bool rendered = Native.UpdateLayeredWindow(window, IntPtr.Zero, ref target, ref size,
                dc, ref source, 0, ref blend, 2);
            if (rendered && !paused && !reducedMotion && (sequence.Length > 1 || reacting))
                Native.SetTimer(window, Timer, frame.Milliseconds, IntPtr.Zero);
            return rendered;
        }

        private void Advance()
        {
            Native.KillTimer(window, Timer);
            if (sequence == null || paused || reducedMotion)
                return;
            frameIndex++;
            if (frameIndex == sequence.Length)
            {
                if (reacting)
                {
                    reacting = false;
                    Start(Baseline());
                    return;
                }
                frameIndex = 0;
            }
            DrawCurrentFrame();
        }

        private void ClampPosition()
        {
            Native.Rectangle work = Native.WorkArea(new Native.Point { X = position.X + Size / 2, Y = position.Y + Size / 2 });
            position.X = Math.Max(work.Left, Math.Min(work.Right - Size, position.X));
            position.Y = Math.Max(work.Top, Math.Min(work.Bottom - Size, position.Y));
        }

        private void MoveTo(Native.Point point)
        {
            position = point;
            ClampPosition();
            Native.SetWindowPos(window, IntPtr.Zero, position.X, position.Y + sequence[frameIndex].VerticalOffset,
                0, 0, 0x15); // SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE
        }

        private void EndDrag()
        {
            if (!dragging)
                return;
            dragging = false;
            if (dragMoved)
                savePosition(position);
        }

        private IntPtr ProcessMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
        {
            Native.Point cursor;
            switch (message)
            {
                case 0x0113: // WM_TIMER
                    if (wParam.ToInt64() == (long)Timer.ToUInt64())
                        Advance();
                    return IntPtr.Zero;
                case 0x0201: // WM_LBUTTONDOWN
                    if (Native.GetCursorPos(out cursor))
                    {
                        dragOffset = new Native.Point { X = cursor.X - position.X, Y = cursor.Y - position.Y };
                        dragging = true;
                        dragMoved = false;
                        Native.SetCapture(hwnd);
                    }
                    return IntPtr.Zero;
                case 0x0200: // WM_MOUSEMOVE
                    if (dragging && Native.GetCursorPos(out cursor))
                    {
                        var destination = new Native.Point { X = cursor.X - dragOffset.X, Y = cursor.Y - dragOffset.Y };
                        dragMoved |= destination.X != position.X || destination.Y != position.Y;
                        MoveTo(destination);
                    }
                    return IntPtr.Zero;
                case 0x0202: // WM_LBUTTONUP
                    EndDrag();
                    Native.ReleaseCapture();
                    return IntPtr.Zero;
                case 0x0215: // WM_CAPTURECHANGED
                    EndDrag();
                    return IntPtr.Zero;
                case 0x0205: // WM_RBUTTONUP
                    if (!dragging && Native.GetCursorPos(out cursor))
                        showMenu(cursor);
                    return IntPtr.Zero;
                case 0x007e: // WM_DISPLAYCHANGE
                case 0x001a: // WM_SETTINGCHANGE
                    if (art != null)
                    {
                        reducedMotion = !Native.ClientAnimationsEnabled();
                        ClampPosition();
                        reacting = false;
                        Start(Baseline());
                    }
                    return IntPtr.Zero;
                default:
                    return Native.DefWindowProc(hwnd, message, wParam, lParam);
            }
        }

        public void Dispose()
        {
            if (window != IntPtr.Zero)
            {
                Native.KillTimer(window, Timer);
                Native.DestroyWindow(window);
                window = IntPtr.Zero;
            }
            if (dc != IntPtr.Zero && previousBitmap != IntPtr.Zero && previousBitmap != new IntPtr(-1))
                Native.SelectObject(dc, previousBitmap);
            if (bitmap != IntPtr.Zero)
                Native.DeleteObject(bitmap);
            if (dc != IntPtr.Zero)
                Native.DeleteDC(dc);
            dc = bitmap = pixels = previousBitmap = IntPtr.Zero;
            art = null;
            sequence = null;
        }
    }
}
