using System;
using System.IO;
using Microsoft.Win32;

namespace MoveAndHydrate
{
    internal sealed class StartupRegistration
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "MoveAndHydrate.HealthReminder";
        private readonly string command;

        public StartupRegistration(string executable)
        {
            command = "\"" + executable + "\"";
        }

        public bool IsEnabled()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey))
                return key != null && key.GetValue(ValueName) != null;
        }

        public void Apply(bool enabled, Action saveSettings)
        {
            string desired = enabled ? command : null;
            if (desired != null && desired.Length > 260)
                throw new IOException("开机启动命令过长。请把应用移到路径较短的文件夹。");

            using (RegistryKey existing = Registry.CurrentUser.OpenSubKey(RunKey))
            {
                object previous = existing == null ? null : existing.GetValue(ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                if (Equals(previous, desired))
                {
                    saveSettings();
                    return;
                }

                RegistryValueKind previousKind = previous == null ? RegistryValueKind.String : existing.GetValueKind(ValueName);
                using (RegistryKey writable = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (writable == null)
                        throw new IOException("无法访问当前用户的开机启动设置。");
                    Write(writable, desired, RegistryValueKind.String);
                    try
                    {
                        saveSettings();
                    }
                    catch
                    {
                        Write(writable, previous, previousKind);
                        throw;
                    }
                }
            }
        }

        private static void Write(RegistryKey key, object value, RegistryValueKind kind)
        {
            if (value == null)
                key.DeleteValue(ValueName, false);
            else
                key.SetValue(ValueName, value, kind);
        }
    }
}
