using System;
using System.Globalization;
using System.IO;

namespace MoveAndHydrate
{
    internal sealed class PetStore
    {
        internal struct State
        {
            public bool Enabled, Custom;
        }

        private readonly string stateFile, positionFile;

        public PetStore(string folder)
        {
            stateFile = Path.Combine(folder, "pet.dat");
            positionFile = Path.Combine(folder, "pet-position.txt");
        }

        public State Load()
        {
            if (!File.Exists(stateFile))
                return new State();
            using (var stream = File.OpenRead(stateFile))
                return ReadHeader(stream);
        }

        public byte[] ReadCustomImage()
        {
            using (var stream = File.OpenRead(stateFile))
                return ReadHeader(stream).Custom ? WebPCodec.ReadImage(stream) : null;
        }

        private static State ReadHeader(Stream stream)
        {
            if (stream.ReadByte() != 1)
                throw new InvalidDataException("宠物设置文件无法读取。");
            int enabled = stream.ReadByte();
            if (enabled != 0 && enabled != 1)
                throw new InvalidDataException("宠物设置文件不完整。");
            return new State { Enabled = enabled == 1, Custom = stream.Length > stream.Position };
        }

        public void Save(bool enabled, byte[] webp)
        {
            LocalFile.Write(stateFile, stream =>
            {
                stream.WriteByte(1); // Format version
                stream.WriteByte(enabled ? (byte)1 : (byte)0);
                if (webp != null)
                    stream.Write(webp, 0, webp.Length);
            });
        }

        public Native.Point? LoadPosition()
        {
            if (!File.Exists(positionFile))
                return null;
            string[] coordinates = File.ReadAllText(positionFile).Split(',');
            int x, y;
            if (coordinates.Length != 2 ||
                !int.TryParse(coordinates[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out x) ||
                !int.TryParse(coordinates[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out y) ||
                x < -100000 || x > 100000 || y < -100000 || y > 100000)
                return null;
            return new Native.Point { X = x, Y = y };
        }

        public void SavePosition(Native.Point point)
        {
            LocalFile.Write(positionFile, stream =>
            {
                using (var writer = new StreamWriter(stream))
                    writer.Write(string.Format(CultureInfo.InvariantCulture, "{0},{1}", point.X, point.Y));
            });
        }
    }
}
