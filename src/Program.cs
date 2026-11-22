using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;

[assembly: AssemblyTitle("健康提醒")]
[assembly: AssemblyProduct("健康提醒")]
[assembly: AssemblyDescription("久坐、喝水提醒与桌面宠物")]
[assembly: AssemblyVersion("1.2.0.0")]

namespace MoveAndHydrate
{
    internal sealed class Program
    {
        public const string AppName = "健康提醒";
        private const string WindowClassName = "MoveAndHydrate.TrayWindow";
        private const uint TrayCallback = 0x8001;
        private const uint OpenSettingsMessage = 0x8002;
        private const uint NimAdd = 0, NimModify = 1, NimDelete = 2, NimSetFocus = 3, NimSetVersion = 4;
        private const uint NifMessage = 1, NifIcon = 2, NifTip = 4, NifInfo = 16, NifShowTip = 128;
        private const uint WaterCommand = 100, SettingsCommand = 101, ExitCommand = 102, PetSettingsCommand = 103;
        private const uint PollInterval = 5000;
        private static readonly UIntPtr TimerId = new UIntPtr(1);

        private readonly SettingsFile settingsFile;
        private readonly StartupRegistration startup;
        private readonly ReminderSchedule schedule;
        private readonly DesktopPet pet;
        private readonly Native.WindowProc windowProc;
        private readonly IntPtr instance;
        private readonly uint taskbarCreated;
        private Settings settings;
        private SettingsDialog dialog;
        private PetSettingsDialog petDialog;
        private IntPtr window, largeIcon, smallIcon;
        private bool trayAdded, version4, sessionInactive, waterPending, lastBannerHadWater;
        private DateTime? lastDrink;

        [STAThread]
        private static int Main()
        {
            bool firstInstance;
            using (var singleInstance = new Mutex(false, @"Local\MoveAndHydrate.HealthReminder", out firstInstance))
            {
                if (!firstInstance)
                {
                    IntPtr runningWindow = Native.FindWindow(WindowClassName, null);
                    if (runningWindow != IntPtr.Zero)
                        Native.PostMessage(runningWindow, OpenSettingsMessage, IntPtr.Zero, IntPtr.Zero);
                    return 0;
                }
                return new Program().Run();
            }
        }

        private Program()
        {
            string dataFolder = Environment.GetEnvironmentVariable("LOCALAPPDATA");
            if (string.IsNullOrEmpty(dataFolder))
                dataFolder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string appFolder = Path.Combine(dataFolder, "HealthReminder");
            settingsFile = new SettingsFile(Path.Combine(appFolder, "settings.ini"));
            startup = new StartupRegistration(typeof(Program).Assembly.Location);
            settings = settingsFile.Load();
            schedule = new ReminderSchedule(settings, AwakeTime());
            instance = Native.GetModuleHandle(null);
            pet = new DesktopPet(appFolder, instance, position => ShowActionMenu(position, false));
            taskbarCreated = Native.RegisterWindowMessage("TaskbarCreated");
            windowProc = ProcessMessage;
        }

        private int Run()
        {
            var windowClass = new Native.WindowClass
            {
                ClassName = WindowClassName,
                Instance = instance,
                Proc = windowProc
            };
            if (Native.RegisterClass(ref windowClass) == 0)
                return StartupError();

            window = Native.CreateWindowEx(0, WindowClassName, AppName, 0x80000000, // WS_POPUP
                0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);
            if (window == IntPtr.Zero)
                return StartupError();

            Native.ExtractIconEx(typeof(Program).Assembly.Location, 0, out largeIcon, out smallIcon, 1);
            if (smallIcon == IntPtr.Zero || Native.SetTimer(window, TimerId, PollInterval, IntPtr.Zero) == UIntPtr.Zero)
            {
                Native.DestroyWindow(window);
                return StartupError();
            }

            Native.WTSRegisterSessionNotification(window, 0); // Only this Windows session.
            AddTrayIcon();
            pet.Restore();

            Native.Message message;
            int result;
            while ((result = Native.GetMessage(out message, IntPtr.Zero, 0, 0)) > 0)
            {
                Native.TranslateMessage(ref message);
                Native.DispatchMessage(ref message);
            }
            if (result < 0)
                Native.DestroyWindow(window);
            GC.KeepAlive(windowProc);
            return result < 0 ? 1 : 0;
        }

        private static int StartupError()
        {
            Native.MessageBox(IntPtr.Zero, "健康提醒无法启动。", AppName, 0x10);
            return 1;
        }

        private IntPtr ProcessMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
        {
            if (taskbarCreated != 0 && message == taskbarCreated)
            {
                AddTrayIcon();
                return IntPtr.Zero;
            }

            switch (message)
            {
                case TrayCallback:
                    HandleTrayEvent(lParam);
                    return IntPtr.Zero;
                case OpenSettingsMessage:
                    OpenSettings();
                    return IntPtr.Zero;
                case 0x0113: // WM_TIMER
                    if (wParam.ToInt64() == (long)TimerId.ToUInt64())
                        Poll();
                    return IntPtr.Zero;
                case 0x0218: // WM_POWERBROADCAST
                    if (wParam.ToInt64() == 4 || wParam.ToInt64() == 18) // Suspend or automatic resume.
                        schedule.ResetMovement(AwakeTime());
                    return new IntPtr(1);
                case 0x02b1: // WM_WTSSESSION_CHANGE
                    HandleSessionChange(wParam.ToInt32());
                    return IntPtr.Zero;
                case 0x0002: // WM_DESTROY
                    if (petDialog != null)
                        petDialog.Close();
                    if (dialog != null)
                        dialog.Close();
                    pet.Dispose();
                    Native.KillTimer(hwnd, TimerId);
                    Native.WTSUnRegisterSessionNotification(hwnd);
                    if (trayAdded)
                    {
                        Native.NotifyIcon icon = TrayData();
                        Native.Shell_NotifyIcon(NimDelete, ref icon);
                    }
                    if (smallIcon != IntPtr.Zero)
                        Native.DestroyIcon(smallIcon);
                    if (largeIcon != IntPtr.Zero)
                        Native.DestroyIcon(largeIcon);
                    Native.PostQuitMessage(0);
                    return IntPtr.Zero;
                default:
                    return Native.DefWindowProc(hwnd, message, wParam, lParam);
            }
        }

        private void HandleSessionChange(int reason)
        {
            switch (reason)
            {
                case 2: // WTS_CONSOLE_DISCONNECT
                case 4: // WTS_REMOTE_DISCONNECT
                case 7: // WTS_SESSION_LOCK
                    sessionInactive = true;
                    pet.SetPaused(true);
                    schedule.ResetMovement(AwakeTime());
                    break;
                case 1: // WTS_CONSOLE_CONNECT
                case 3: // WTS_REMOTE_CONNECT
                case 8: // WTS_SESSION_UNLOCK
                    sessionInactive = false;
                    pet.SetPaused(false);
                    schedule.ResetMovement(AwakeTime());
                    break;
            }
        }

        private void Poll()
        {
            if (!trayAdded)
                AddTrayIcon();
            if (sessionInactive)
                return;

            uint? idle = InputIdle();
            long now = AwakeTime();
            Reminder due = schedule.Check(now, idle);
            if (due == Reminder.None || !trayAdded)
                return;

            bool movement = (due & Reminder.Movement) != 0;
            bool water = (due & Reminder.Water) != 0;
            Native.NotifyIcon icon = TrayData();
            icon.Flags = NifInfo;
            icon.InfoFlags = 1; // NIIF_INFO, using the system notification settings.
            if (movement && water)
            {
                icon.Title = "起来活动，喝口水";
                icon.Text = "站起来换个姿势，顺便喝几口水。喝完后，右键托盘或桌面宠物，点「已喝水」。";
            }
            else if (movement)
            {
                icon.Title = "起来动一动";
                icon.Text = "站起来换个姿势，伸展一下，或走几步。";
            }
            else
            {
                icon.Title = "该喝水了";
                icon.Text = "喝几口水。喝完后，右键托盘或桌面宠物，点「已喝水」。";
            }

            if (!Native.Shell_NotifyIcon(NimModify, ref icon))
            {
                trayAdded = false;
                return;
            }

            schedule.MarkReminded(due, now);
            lastBannerHadWater = water;
            if (water && !waterPending)
            {
                waterPending = true;
                pet.SetAwaitingWater(true);
                UpdateTrayTip();
            }
        }

        private static uint? InputIdle()
        {
            var input = new Native.LastInput { Size = 8 };
            if (!Native.GetLastInputInfo(ref input))
                return null;
            return unchecked((uint)Native.GetTickCount64() - input.Time);
        }

        private static long AwakeTime()
        {
            ulong time;
            return Native.QueryUnbiasedInterruptTime(out time) ? (long)(time / 10000) : (long)Native.GetTickCount64();
        }

        private Native.NotifyIcon TrayData()
        {
            return new Native.NotifyIcon
            {
                Size = (uint)Marshal.SizeOf(typeof(Native.NotifyIcon)),
                Window = window,
                Id = 1
            };
        }

        private void AddTrayIcon()
        {
            Native.NotifyIcon icon = TrayData();
            icon.Flags = NifMessage | NifIcon | NifTip | NifShowTip;
            icon.Callback = TrayCallback;
            icon.Icon = smallIcon;
            icon.Tip = TrayTip();
            trayAdded = Native.Shell_NotifyIcon(NimAdd, ref icon) || Native.Shell_NotifyIcon(NimModify, ref icon);
            if (trayAdded)
            {
                icon.Version = 4;
                version4 = Native.Shell_NotifyIcon(NimSetVersion, ref icon);
            }
        }

        private void UpdateTrayTip()
        {
            if (!trayAdded)
                return;
            Native.NotifyIcon icon = TrayData();
            icon.Flags = NifTip | NifShowTip;
            icon.Tip = TrayTip();
            Native.Shell_NotifyIcon(NimModify, ref icon);
        }

        private string TrayTip()
        {
            return waterPending ? AppName + " · 喝水待确认" : AppName;
        }

        private void HandleTrayEvent(IntPtr lParam)
        {
            int eventCode = (int)(lParam.ToInt64() & 0xffff);
            if ((version4 && eventCode == 0x007b) || (!version4 && eventCode == 0x0205))
            {
                Native.Point position;
                if (Native.GetCursorPos(out position))
                    ShowActionMenu(position, true);
            }
            else if (eventCode == 0x0203 || eventCode == 0x0401) // Double click or keyboard selection.
            {
                OpenSettings();
            }
        }

        private void ShowActionMenu(Native.Point position, bool fromTray)
        {
            IntPtr menu = Native.CreatePopupMenu();
            if (menu == IntPtr.Zero)
                return;

            uint command;
            try
            {
                Native.AppendMenu(menu, 0, new UIntPtr(WaterCommand), "已喝水(&W)");
                Native.AppendMenu(menu, 1, UIntPtr.Zero, WaterStatus()); // MF_GRAYED
                Native.AppendMenu(menu, 1, UIntPtr.Zero, NextWaterStatus());
                Native.AppendMenu(menu, 0x800, UIntPtr.Zero, null); // MF_SEPARATOR
                Native.AppendMenu(menu, 0, new UIntPtr(SettingsCommand), "设置…(&S)");
                Native.AppendMenu(menu, 0, new UIntPtr(PetSettingsCommand), "桌面宠物设置…(&P)");
                Native.AppendMenu(menu, 0, new UIntPtr(ExitCommand), "退出(&X)");
                Native.SetForegroundWindow(window);
                command = Native.TrackPopupMenuEx(menu, 0x102, position.X, position.Y, window, IntPtr.Zero); // TPM_RETURNCMD | TPM_RIGHTBUTTON
                Native.PostMessage(window, 0, IntPtr.Zero, IntPtr.Zero);
            }
            finally
            {
                Native.DestroyMenu(menu);
            }
            if (fromTray)
            {
                Native.NotifyIcon icon = TrayData();
                Native.Shell_NotifyIcon(NimSetFocus, ref icon);
            }

            switch (command)
            {
                case WaterCommand:
                    ConfirmWater();
                    break;
                case SettingsCommand:
                    OpenSettings();
                    break;
                case PetSettingsCommand:
                    OpenPetSettings(IntPtr.Zero);
                    break;
                case ExitCommand:
                    Native.DestroyWindow(window);
                    break;
            }
        }

        private void ConfirmWater()
        {
            schedule.ConfirmWater(AwakeTime());
            lastDrink = DateTime.Now;
            waterPending = false;
            pet.SetAwaitingWater(false);
            pet.Celebrate();
            if (lastBannerHadWater && trayAdded)
            {
                Native.NotifyIcon icon = TrayData();
                icon.Flags = NifInfo;
                icon.Text = "";
                Native.Shell_NotifyIcon(NimModify, ref icon);
            }
            lastBannerHadWater = false;
            UpdateTrayTip();
        }

        private string NextWaterStatus()
        {
            int seconds = (int)Math.Ceiling(schedule.TimeUntilWater(AwakeTime()).TotalSeconds);
            if (seconds == 0)
                return "下次喝水 · 即将提醒";
            return string.Format(CultureInfo.InvariantCulture, "下次喝水 · {0} 分 {1:00} 秒后", seconds / 60, seconds % 60);
        }

        private string WaterStatus()
        {
            if (waterPending)
                return "喝水提醒 · 待确认";
            if (!lastDrink.HasValue)
                return "本次运行尚未确认喝水";
            string format = lastDrink.Value.Date == DateTime.Today ? "HH:mm" : "MM-dd HH:mm";
            return "上次喝水  " + lastDrink.Value.ToString(format, CultureInfo.InvariantCulture);
        }

        private void OpenSettings()
        {
            if (petDialog != null)
            {
                petDialog.Activate();
                return;
            }
            if (dialog != null)
            {
                dialog.Activate();
                return;
            }

            bool? startupEnabled = null;
            try
            {
                startupEnabled = startup.IsEnabled();
            }
            catch (Exception error)
            {
                if (!DesktopPet.IsStorageError(error))
                    throw;
            }

            dialog = new SettingsDialog(settings, startupEnabled,
                (next, enabled) => SaveSettings(next, startupEnabled.HasValue, enabled),
                OpenPetSettings, largeIcon, smallIcon);
            try
            {
                dialog.Run(instance);
            }
            finally
            {
                dialog = null;
            }
        }

        private void OpenPetSettings(IntPtr owner)
        {
            if (petDialog != null)
            {
                petDialog.Activate();
                return;
            }
            petDialog = new PetSettingsDialog(pet);
            try
            {
                petDialog.Run(instance, owner);
            }
            finally
            {
                petDialog = null;
            }
        }

        private void SaveSettings(Settings next, bool changeStartup, bool startupEnabled)
        {
            if (changeStartup)
                startup.Apply(startupEnabled, () => settingsFile.Save(next));
            else
                settingsFile.Save(next);
            schedule.Apply(next, AwakeTime());
            settings = next;
        }
    }
}
