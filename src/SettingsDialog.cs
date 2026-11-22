using System;
using System.Globalization;
using System.IO;
using System.Security;
using System.Text;

namespace MoveAndHydrate
{
    internal sealed class SettingsDialog
    {
        private const int MovementInput = 101;
        private const int WaterInput = 102;
        private const int BreakInput = 103;
        private const int StartupInput = 104;
        private const int PetButton = 105;
        private const int SaveButton = 1;
        private const int CancelButton = 2;

        private readonly Settings settings;
        private readonly bool? startupEnabled;
        private readonly Action<Settings, bool> save;
        private readonly Action<IntPtr> openPetSettings;
        private readonly IntPtr largeIcon;
        private readonly IntPtr smallIcon;
        private IntPtr window;

        public SettingsDialog(Settings settings, bool? startupEnabled, Action<Settings, bool> save,
            Action<IntPtr> openPetSettings, IntPtr largeIcon, IntPtr smallIcon)
        {
            this.settings = settings;
            this.startupEnabled = startupEnabled;
            this.save = save;
            this.openPetSettings = openPetSettings;
            this.largeIcon = largeIcon;
            this.smallIcon = smallIcon;
        }

        public void Run(IntPtr instance)
        {
            try
            {
                using (var template = new DialogTemplate("健康提醒设置", 248, 200, true))
                {
                    AddRow(template, 11, MovementInput, "起身提醒间隔(&M)", "持续使用电脑达到这个时长时提醒。");
                    AddRow(template, 51, WaterInput, "喝水提醒间隔(&W)", "点「已喝水」后，重新计算下次提醒。");
                    AddRow(template, 91, BreakInput, "离开多久算休息(&B)", "键盘鼠标闲置达到这个时长后，重计起身时间。");
                    template.Button(StartupInput, 3, 12, 127, 154, 14, "开机登录后自动启动(&A)");
                    template.Button(PetButton, 0, 12, 148, 85, 17, "桌面宠物设置…(&P)");
                    template.Button(SaveButton, 1, 128, 173, 50, 16, "保存");
                    template.Button(CancelButton, 0, 186, 173, 50, 16, "取消");
                    if (template.Run(instance, IntPtr.Zero, ProcessMessage) == new IntPtr(-1))
                        Native.MessageBox(IntPtr.Zero, "无法打开设置窗口。", Program.AppName, 0x10);
                }
            }
            finally
            {
                window = IntPtr.Zero;
            }
        }

        public void Activate()
        {
            if (window != IntPtr.Zero)
                Native.SetForegroundWindow(window);
        }

        public void Close()
        {
            if (window != IntPtr.Zero)
                Native.EndDialog(window, IntPtr.Zero);
        }

        private IntPtr ProcessMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
        {
            switch (message)
            {
                case 0x0110: // WM_INITDIALOG
                    window = hwnd;
                    Native.SendMessage(hwnd, 0x0080, IntPtr.Zero, smallIcon); // WM_SETICON
                    Native.SendMessage(hwnd, 0x0080, new IntPtr(1), largeIcon);
                    Native.SetDlgItemInt(hwnd, MovementInput, (uint)settings.MovementMinutes, false);
                    Native.SetDlgItemInt(hwnd, WaterInput, (uint)settings.WaterMinutes, false);
                    Native.SetDlgItemInt(hwnd, BreakInput, (uint)settings.BreakMinutes, false);
                    Native.CheckDlgButton(hwnd, StartupInput, startupEnabled == true ? 1u : 0u);
                    if (!startupEnabled.HasValue)
                        Native.EnableWindow(Native.GetDlgItem(hwnd, StartupInput), false);
                    return new IntPtr(1);
                case 0x0111: // WM_COMMAND
                    switch (wParam.ToInt64() & 0xffff)
                    {
                        case SaveButton:
                            Save(hwnd);
                            return new IntPtr(1);
                        case PetButton:
                            openPetSettings(hwnd);
                            return new IntPtr(1);
                        case CancelButton:
                            Native.EndDialog(hwnd, IntPtr.Zero);
                            return new IntPtr(1);
                    }
                    break;
                case 0x0010: // WM_CLOSE
                    Native.EndDialog(hwnd, IntPtr.Zero);
                    return new IntPtr(1);
            }
            return IntPtr.Zero;
        }

        private void Save(IntPtr hwnd)
        {
            int movement, water, rest;
            if (!TryReadMinutes(hwnd, MovementInput, out movement) ||
                !TryReadMinutes(hwnd, WaterInput, out water) ||
                !TryReadMinutes(hwnd, BreakInput, out rest))
                return;

            try
            {
                save(new Settings(movement, water, rest), Native.IsDlgButtonChecked(hwnd, StartupInput) == 1);
                Native.EndDialog(hwnd, new IntPtr(1));
            }
            catch (Exception error)
            {
                if (!(error is IOException || error is UnauthorizedAccessException || error is SecurityException))
                    throw;

                Native.MessageBox(hwnd, "无法保存设置。\n" + error.Message, Program.AppName, 0x10);
            }
        }

        private static bool TryReadMinutes(IntPtr hwnd, int id, out int result)
        {
            var text = new StringBuilder(12);
            uint length = Native.GetDlgItemText(hwnd, id, text, text.Capacity);
            uint value;
            if (length <= 4 && uint.TryParse(text.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out value) &&
                Settings.IsValidMinutes(value))
            {
                result = (int)value;
                return true;
            }

            Native.MessageBox(hwnd, "请输入 1 到 1440 之间的整数（分钟）。", Program.AppName, 0x30);
            IntPtr input = Native.GetDlgItem(hwnd, id);
            Native.SetFocus(input);
            Native.SendMessage(input, 0x00b1, IntPtr.Zero, new IntPtr(-1)); // EM_SETSEL
            result = 0;
            return false;
        }

        private static void AddRow(DialogTemplate template, short y, int id, string label, string description)
        {
            template.Text(-1, 12, (short)(y + 2), 108, 11, label);
            template.Edit(id, 0x2082, 128, y, 55, 14); // ES_NUMBER | ES_RIGHT | ES_AUTOHSCROLL
            template.Text(-1, 189, (short)(y + 2), 47, 11, "分钟");
            template.Text(-1, 12, (short)(y + 19), 224, 13, description);
        }
    }
}
