using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace MoveAndHydrate
{
    internal sealed class PetFrame
    {
        public const int Size = 112;
        public readonly byte[] Pixels;
        public readonly uint Milliseconds;
        public readonly bool HasVisiblePixels;
        public readonly int VerticalOffset;

        public PetFrame(byte[] pixels, uint milliseconds, bool hasVisiblePixels, int verticalOffset = 0)
        {
            Pixels = pixels;
            Milliseconds = milliseconds;
            HasVisiblePixels = hasVisiblePixels;
            VerticalOffset = verticalOffset;
        }
    }

    internal sealed class PetArt
    {
        private const int CellWidth = 192;
        private const int CellHeight = 208;
        private static readonly int[] IdleTiming = { 280, 110, 110, 140, 140, 320 };
        private static readonly int[] JumpTiming = { 140, 140, 140, 140, 280 };
        private static readonly int[] WaitingTiming = { 150, 150, 150, 150, 150, 260 };

        public readonly PetFrame[] Idle, Jump, Waiting;

        public PetArt(PetFrame[] idle, PetFrame[] jump = null, PetFrame[] waiting = null)
        {
            Idle = idle;
            Jump = jump;
            Waiting = waiting;
        }

        public static PetArt FromImage(IntPtr rgba, int width, int height)
        {
            if (width == CellWidth * 8 && (height == CellHeight * 9 || height == CellHeight * 11))
            {
                PetFrame[] idle = ReadRow(rgba, width, 0, IdleTiming);
                if (idle.Length == 0)
                    throw new InvalidDataException("动作图集的第一行需要至少一张可见的待机帧。");
                return new PetArt(idle, ReadRow(rgba, width, 4, JumpTiming), ReadRow(rgba, width, 6, WaitingTiming));
            }

            PetFrame image = Scale(rgba, width, 0, 0, width, height, 0);
            if (!image.HasVisiblePixels)
                throw new InvalidDataException("图片是完全透明的，桌面上看不到宠物。");
            return new PetArt(new[] { image });
        }

        private static PetFrame[] ReadRow(IntPtr rgba, int stride, int row, int[] timings)
        {
            var frames = new List<PetFrame>(timings.Length);
            for (int i = 0; i < timings.Length; i++)
            {
                PetFrame frame = Scale(rgba, stride, i * CellWidth, row * CellHeight,
                    CellWidth, CellHeight, (uint)timings[i]);
                if (frame.HasVisiblePixels)
                    frames.Add(frame);
            }
            return frames.ToArray();
        }

        public static unsafe PetFrame Scale(IntPtr rgba, int stride, int x, int y, int width, int height, uint milliseconds)
        {
            const int size = PetFrame.Size;
            int longest = Math.Max(width, height);
            int targetWidth = Math.Max(1, size * width / longest);
            int targetHeight = Math.Max(1, size * height / longest);
            int left = (size - targetWidth) / 2;
            int top = (size - targetHeight) / 2;
            byte[] pixels = new byte[size * size * 4];
            byte* source = (byte*)rgba;
            bool visible = false;

            // UpdateLayeredWindow expects premultiplied BGRA.
            for (int column = 0; column < targetWidth; column++)
            {
                int sourceX = x + (2 * column + 1) * width / (2 * targetWidth);
                for (int row = 0; row < targetHeight; row++)
                {
                    int sourceY = y + (2 * row + 1) * height / (2 * targetHeight);
                    byte* pixel = source + (sourceY * stride + sourceX) * 4;
                    int alpha = pixel[3];
                    if (alpha == 0)
                        continue;
                    visible = true;
                    int destination = ((top + row) * size + left + column) * 4;
                    pixels[destination] = (byte)((pixel[2] * alpha + 127) / 255);
                    pixels[destination + 1] = (byte)((pixel[1] * alpha + 127) / 255);
                    pixels[destination + 2] = (byte)((pixel[0] * alpha + 127) / 255);
                    pixels[destination + 3] = (byte)alpha;
                }
            }
            return new PetFrame(pixels, milliseconds, visible);
        }
    }
}
