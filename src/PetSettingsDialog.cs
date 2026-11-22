using System;
using System.IO;

namespace MoveAndHydrate
{
    internal sealed class PetSettingsDialog
    {
        private const int EnabledInput = 101, ImageText = 102, ImportButton = 103, ResetButton = 104;
        private const int CopyImageButton = 105, CopyAtlasButton = 106, StatusText = 107;
        private const int SaveButton = 1, CancelButton = 2;
        private const string ImagePrompt =
            "为桌面应用生成一只原创桌面宠物。\r\n" +
            "角色描述：[把这里替换为你想要的角色，例如背着蓝色水瓶的橘色小狐狸]。\r\n" +
            "生成一张 512×512 像素的正面全身图。角色居中，四周留约 10% 的透明空间，轮廓清楚，缩小到约 100 像素时仍然容易辨认。\r\n" +
            "使用真正的 Alpha 透明背景。画面里只放角色与贴身配件，去掉地面、独立阴影、场景、文字、水印和模拟透明的棋盘格。\r\n" +
            "交付透明 WebP 文件。如果工具只支持透明 PNG，请保持 Alpha 通道，再无损转换为 WebP。";
        private const string AtlasPrompt =
            "为桌面宠物制作一张原创像素动画图集。\r\n" +
            "角色描述：[把这里替换为你想要的角色，例如背着蓝色水瓶的橘色小狐狸]。\r\n" +
            "画布精确为 1536×1872 像素，8 列×9 行，每格 192×208 像素。格子连续排列，禁止画出分格线、标签、边框、留缝或水印。\r\n" +
            "从上往下数：第 1 行前 6 格为待机呼吸和眨眼，第 5 行前 5 格为开心跳跃（蓄力、腾空、最高点、下落、站稳），第 7 行前 6 格为等待喝水的动作。每行从左向右播放，其余格子完全透明。\r\n" +
            "每格只画一个完整角色。保持造型、大小、色板和脚部对齐一致，跳跃过程除外。不要跨格、不要额外背景或阴影，空白使用真正的 Alpha 透明。\r\n" +
            "交付无损透明 WebP，保存后核对像素尺寸。如果工具只能生成透明 PNG，请保持尺寸和 Alpha 通道，再无损转换为 WebP。";

        private readonly DesktopPet pet;
        private bool imageChanged;
        private byte[] selectedImage;
        private PetArt selectedArt;
        private IntPtr window;

        public PetSettingsDialog(DesktopPet pet)
        {
            this.pet = pet;
        }

        public void Run(IntPtr instance, IntPtr owner)
        {
            try
            {
                using (var template = new DialogTemplate("桌面宠物设置", 280, 175, owner == IntPtr.Zero))
                {
                    template.Button(EnabledInput, 3, 12, 11, 224, 15, "显示桌面宠物(&E)");
                    template.Text(ImageText, 12, 33, 256, 12, "");
                    template.Button(ImportButton, 0, 12, 51, 89, 17, "导入 WebP…(&I)");
                    template.Button(ResetButton, 0, 108, 51, 82, 17, "恢复默认(&D)");
                    template.Text(-1, 12, 76, 256, 12, "支持单图、WebP 动图及 8×9 / 8×11 动作图集。");
                    template.Button(CopyImageButton, 0, 12, 96, 108, 17, "复制单图提示词(&C)");
                    template.Button(CopyAtlasButton, 0, 128, 96, 114, 17, "复制动画图集提示词(&T)");
                    template.Text(StatusText, 12, 119, 256, 22, "替换提示词里的角色描述，即可交给 AI 绘图工具。");
                    template.Button(SaveButton, 1, 160, 147, 50, 16, "保存");
                    template.Button(CancelButton, 0, 218, 147, 50, 16, "取消");
                    if (template.Run(instance, owner, ProcessMessage) == new IntPtr(-1))
                        Native.MessageBox(owner, "无法打开宠物设置。", Program.AppName, 0x10);
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
            if (message == 0x0110) // WM_INITDIALOG
            {
                window = hwnd;
                Native.CheckDlgButton(hwnd, EnabledInput, pet.State.Enabled ? 1u : 0u);
                UpdateImageLabel(pet.State.Custom ? "当前形象 · 自定义 WebP" : "当前形象 · 默认小水滴");
                if (pet.LoadError != null)
                    SetStatus(pet.LoadError);
                return new IntPtr(1);
            }
            if (message == 0x0010) // WM_CLOSE
            {
                Close();
                return new IntPtr(1);
            }
            if (message != 0x0111) // WM_COMMAND
                return IntPtr.Zero;

            try
            {
                switch (wParam.ToInt64() & 0xffff)
                {
                    case ImportButton:
                        Import();
                        break;
                    case ResetButton:
                        selectedImage = null;
                        selectedArt = null;
                        imageChanged = true;
                        UpdateImageLabel("已选择 · 默认小水滴");
                        SetStatus("点「保存」应用更改。");
                        break;
                    case CopyImageButton:
                        CopyPrompt(ImagePrompt);
                        break;
                    case CopyAtlasButton:
                        CopyPrompt(AtlasPrompt);
                        break;
                    case SaveButton:
                        pet.Save(Native.IsDlgButtonChecked(hwnd, EnabledInput) == 1,
                            imageChanged, selectedImage, selectedArt);
                        Native.EndDialog(hwnd, new IntPtr(1));
                        break;
                    case CancelButton:
                        Close();
                        break;
                    default:
                        return IntPtr.Zero;
                }
            }
            catch (Exception error)
            {
                if (!DesktopPet.IsStorageError(error))
                    throw;
                Native.MessageBox(hwnd, error.Message, Program.AppName, 0x30);
            }
            return new IntPtr(1);
        }

        private void Import()
        {
            string path = Native.ChooseWebP(window);
            if (path == null)
                return;
            byte[] image = WebPCodec.ReadImage(path);
            PetArt art = pet.Decode(image);
            selectedImage = image;
            selectedArt = art;
            imageChanged = true;
            Native.CheckDlgButton(window, EnabledInput, 1);
            string filename = Path.GetFileName(path);
            UpdateImageLabel("已选择 · " + (filename.Length > 43 ? filename.Substring(0, 40) + "…" : filename));
            SetStatus("点「保存」在桌面显示这只宠物。");
        }

        private void CopyPrompt(string text)
        {
            SetStatus(Native.CopyText(window, text) ? "提示词已复制，可以粘贴至 AI 绘图工具。"
                : "剪贴板正在使用，请重试。");
        }

        private void UpdateImageLabel(string text)
        {
            Native.SetDlgItemText(window, ImageText, text);
        }

        private void SetStatus(string text)
        {
            Native.SetDlgItemText(window, StatusText, text);
        }
    }
}
