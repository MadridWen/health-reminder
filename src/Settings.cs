using System;
using System.Globalization;
using System.IO;
using System.Security;

namespace MoveAndHydrate
{
    internal sealed class Settings
    {
        public const int MinimumMinutes = 1;
        public const int MaximumMinutes = 1440;
        public static readonly Settings Defaults = new Settings(45, 60, 5);

        public readonly int MovementMinutes;
        public readonly int WaterMinutes;
        public readonly int BreakMinutes;

        public Settings(int movementMinutes, int waterMinutes, int breakMinutes)
        {
            MovementMinutes = movementMinutes;
            WaterMinutes = waterMinutes;
            BreakMinutes = breakMinutes;
        }

        public static bool IsValidMinutes(uint minutes)
        {
            return minutes >= MinimumMinutes && minutes <= MaximumMinutes;
        }
    }

    internal sealed class SettingsFile
    {
        private readonly string path;

        public SettingsFile(string path)
        {
            this.path = path;
        }

        public Settings Load()
        {
            try
            {
                int movement = Settings.Defaults.MovementMinutes;
                int water = Settings.Defaults.WaterMinutes;
                int rest = Settings.Defaults.BreakMinutes;
                foreach (string line in File.ReadLines(path))
                {
                    int separator = line.IndexOf('=');
                    uint minutes;
                    if (separator < 0 ||
                        !uint.TryParse(line.Substring(separator + 1), NumberStyles.None, CultureInfo.InvariantCulture, out minutes) ||
                        !Settings.IsValidMinutes(minutes))
                        continue;

                    switch (line.Substring(0, separator))
                    {
                        case "movementMinutes": movement = (int)minutes; break;
                        case "waterMinutes": water = (int)minutes; break;
                        case "breakMinutes": rest = (int)minutes; break;
                    }
                }
                return new Settings(movement, water, rest);
            }
            catch (IOException)
            {
                return Settings.Defaults;
            }
            catch (UnauthorizedAccessException)
            {
                return Settings.Defaults;
            }
            catch (SecurityException)
            {
                return Settings.Defaults;
            }
        }

        public void Save(Settings settings)
        {
            string content = string.Format(CultureInfo.InvariantCulture,
                "movementMinutes={0}\nwaterMinutes={1}\nbreakMinutes={2}\n",
                settings.MovementMinutes, settings.WaterMinutes, settings.BreakMinutes);

            LocalFile.Write(path, stream =>
            {
                using (var writer = new StreamWriter(stream))
                    writer.Write(content);
            });
        }
    }
}
