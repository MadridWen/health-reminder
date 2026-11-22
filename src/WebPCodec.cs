using System;
using System.IO;
using System.Runtime.InteropServices;

namespace MoveAndHydrate
{
    internal sealed class WebPCodec : IDisposable
    {
        public const int MaxFileBytes = 20 * 1024 * 1024;
        private const int MaxPixels = 4194304;
        private const int MaxFrames = 120;

        private readonly string codecFolder;
        private IntPtr decoderLibrary, animationLibrary;

        [StructLayout(LayoutKind.Sequential, Size = 40)]
        private struct Features
        {
            public int Width, Height, HasAlpha, HasAnimation, Format;
        }

        [StructLayout(LayoutKind.Sequential, Size = 36)]
        private struct AnimationInfo
        {
            public uint Width, Height, LoopCount, Background, FrameCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WebPData
        {
            public IntPtr Bytes;
            public UIntPtr Size;
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int GetFeatures(IntPtr data, UIntPtr length, out Features features, int version);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr DecodeInto(IntPtr data, UIntPtr length, IntPtr pixels, UIntPtr capacity, int stride);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr CreateAnimation(ref WebPData data, IntPtr options, int version);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int GetAnimationInfo(IntPtr animation, out AnimationInfo info);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int GetFrame(IntPtr animation, out IntPtr pixels, out int timestamp);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void DestroyAnimation(IntPtr animation);

        public WebPCodec(string appFolder)
        {
            string architecture = Native.ProcessArchitecture();
            codecFolder = Path.Combine(appFolder, "codec", "webp-1.6.0", architecture);
            foreach (string library in new[] { "libsharpyuv.dll", "libwebp.dll", "libwebpdemux.dll" })
                InstallLibrary(architecture, library);
            decoderLibrary = LoadLibrary("libwebp.dll");
        }

        public static byte[] ReadImage(string path)
        {
            using (var file = File.OpenRead(path))
                return ReadImage(file);
        }

        internal static byte[] ReadImage(Stream stream)
        {
            long remaining = stream.Length - stream.Position;
            if (remaining < 12 || remaining > MaxFileBytes)
                throw new InvalidDataException("请选择不超过 20 MiB 的 WebP 图片。");

            byte[] contents = new byte[(int)remaining];
            int offset = 0;
            while (offset < contents.Length)
            {
                int count = stream.Read(contents, offset, contents.Length - offset);
                if (count == 0)
                    throw new EndOfStreamException("读取 WebP 图片时遇到不完整的文件。");
                offset += count;
            }
            return contents;
        }

        public PetArt Decode(byte[] image)
        {
            if (image.Length < 12 || image[0] != 'R' || image[1] != 'I' || image[2] != 'F' || image[3] != 'F' ||
                image[8] != 'W' || image[9] != 'E' || image[10] != 'B' || image[11] != 'P')
                throw new InvalidDataException("文件内容不是 WebP 图片。");

            GCHandle pinned = GCHandle.Alloc(image, GCHandleType.Pinned);
            try
            {
                var data = new WebPData { Bytes = pinned.AddrOfPinnedObject(), Size = new UIntPtr((uint)image.Length) };
                var readFeatures = Function<GetFeatures>(decoderLibrary, "WebPGetFeaturesInternal");
                Features features;
                if (readFeatures(data.Bytes, data.Size, out features, 0x0210) != 0)
                    throw new InvalidDataException("无法读取 WebP 图片，文件可能损坏。");
                if (features.Width <= 0 || features.Height <= 0 || (long)features.Width * features.Height > MaxPixels)
                    throw new InvalidDataException("图片最多可包含 419 万像素。");

                if (features.HasAnimation != 0)
                    return DecodeAnimation(ref data, features.Width, features.Height);
                return DecodeImage(data, features.Width, features.Height);
            }
            finally
            {
                pinned.Free();
            }
        }

        private PetArt DecodeImage(WebPData data, int width, int height)
        {
            int length = width * height * 4;
            IntPtr pixels = Marshal.AllocHGlobal(length);
            try
            {
                var decode = Function<DecodeInto>(decoderLibrary, "WebPDecodeRGBAInto");
                if (decode(data.Bytes, data.Size, pixels, new UIntPtr((uint)length), width * 4) == IntPtr.Zero)
                    throw new InvalidDataException("无法解码这张 WebP 图片。");
                return PetArt.FromImage(pixels, width, height);
            }
            finally
            {
                Marshal.FreeHGlobal(pixels);
            }
        }

        private PetArt DecodeAnimation(ref WebPData data, int width, int height)
        {
            if (animationLibrary == IntPtr.Zero)
                animationLibrary = LoadLibrary("libwebpdemux.dll");
            var create = Function<CreateAnimation>(animationLibrary, "WebPAnimDecoderNewInternal");
            var info = Function<GetAnimationInfo>(animationLibrary, "WebPAnimDecoderGetInfo");
            var frame = Function<GetFrame>(animationLibrary, "WebPAnimDecoderGetNext");
            var destroy = Function<DestroyAnimation>(animationLibrary, "WebPAnimDecoderDelete");
            IntPtr animation = create(ref data, IntPtr.Zero, 0x0107);
            if (animation == IntPtr.Zero)
                throw new InvalidDataException("无法解码 WebP 动画。");
            try
            {
                AnimationInfo details;
                if (info(animation, out details) == 0 || details.Width != width || details.Height != height ||
                    details.FrameCount == 0)
                    throw new InvalidDataException("无法读取 WebP 动画的帧数或尺寸。");
                if (details.FrameCount > MaxFrames || (long)width * height * details.FrameCount > 180000000)
                    throw new InvalidDataException("动画最多支持 120 帧。较大的图片需要缩小或减少帧数。");

                var frames = new PetFrame[details.FrameCount];
                int previousTime = 0;
                bool visible = false;
                for (int i = 0; i < frames.Length; i++)
                {
                    IntPtr pixels;
                    int time;
                    if (frame(animation, out pixels, out time) == 0 || pixels == IntPtr.Zero)
                        throw new InvalidDataException("WebP 动画包含无法读取的帧。");
                    uint duration = (uint)Math.Min(60000, Math.Max(80L, (long)time - previousTime));
                    frames[i] = PetArt.Scale(pixels, width, 0, 0, width, height, duration);
                    visible |= frames[i].HasVisiblePixels;
                    previousTime = time;
                }
                if (!visible)
                    throw new InvalidDataException("动画的所有帧都是透明的。");
                return new PetArt(frames);
            }
            finally
            {
                destroy(animation);
            }
        }

        private void InstallLibrary(string architecture, string name)
        {
            string resourceName = "WebP." + architecture + "." + name;
            byte[] expected;
            using (Stream resource = typeof(WebPCodec).Assembly.GetManifestResourceStream(resourceName))
            {
                if (resource == null)
                    throw new IOException("此处理器的 WebP 解码库不可用。");
                using (var buffer = new MemoryStream())
                {
                    resource.CopyTo(buffer);
                    expected = buffer.ToArray();
                }
            }
            string path = Path.Combine(codecFolder, name);
            if (File.Exists(path) && new FileInfo(path).Length == expected.Length &&
                BytesEqual(File.ReadAllBytes(path), expected))
                return;
            LocalFile.Write(path, stream => stream.Write(expected, 0, expected.Length));
        }

        private static bool BytesEqual(byte[] left, byte[] right)
        {
            if (left.Length != right.Length)
                return false;
            for (int i = 0; i < left.Length; i++)
                if (left[i] != right[i])
                    return false;
            return true;
        }

        private IntPtr LoadLibrary(string name)
        {
            const uint LoadFromDllFolderAndSystem32 = 0x00000900;
            IntPtr library = Native.LoadLibraryEx(Path.Combine(codecFolder, name), IntPtr.Zero, LoadFromDllFolderAndSystem32);
            if (library == IntPtr.Zero)
                throw new IOException("无法载入 WebP 解码库（Windows 错误 " + Marshal.GetLastWin32Error() + "）。");
            return library;
        }

        private static T Function<T>(IntPtr library, string name) where T : class
        {
            IntPtr address = Native.GetProcAddress(library, name);
            if (address == IntPtr.Zero)
                throw new IOException("WebP 解码库缺少所需接口。");
            return (T)(object)Marshal.GetDelegateForFunctionPointer(address, typeof(T));
        }

        public void Dispose()
        {
            if (animationLibrary != IntPtr.Zero)
                Native.FreeLibrary(animationLibrary);
            if (decoderLibrary != IntPtr.Zero)
                Native.FreeLibrary(decoderLibrary);
        }
    }
}
