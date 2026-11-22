using System;
using System.IO;
using System.Security;

namespace MoveAndHydrate
{
    internal sealed class DesktopPet : IDisposable
    {
        private readonly string appFolder;
        private readonly PetStore store;
        private readonly PetWindow window;
        private PetArt currentArt;

        public PetStore.State State { get; private set; }
        public string LoadError { get; private set; }

        public DesktopPet(string appFolder, IntPtr instance, Action<Native.Point> openMenu)
        {
            this.appFolder = appFolder;
            store = new PetStore(appFolder);
            Native.Point? position = null;
            try
            {
                position = store.LoadPosition();
            }
            catch (Exception error)
            {
                if (!IsStorageError(error))
                    throw;
            }
            window = new PetWindow(instance, position, openMenu, SavePosition);
        }

        public void Restore()
        {
            try
            {
                State = store.Load();
                if (State.Enabled)
                {
                    PetArt restored = Decode(State.Custom ? store.ReadCustomImage() : null);
                    window.Show(restored);
                    currentArt = restored;
                }
            }
            catch (Exception error)
            {
                if (!IsStorageError(error))
                    throw;
                LoadError = "上次的宠物未能显示。可重新导入或恢复默认形象。";
            }
        }

        public PetArt Decode(byte[] image)
        {
            if (image == null)
            {
                using (Stream resource = typeof(DesktopPet).Assembly.GetManifestResourceStream("DesktopPet.Default"))
                {
                    if (resource == null)
                        throw new IOException("应用缺少默认宠物图片。");
                    image = WebPCodec.ReadImage(resource);
                }
            }
            using (var codec = new WebPCodec(appFolder))
                return codec.Decode(image);
        }

        public void Save(bool enabled, bool imageChanged, byte[] selectedImage, PetArt selectedArt)
        {
            if (!imageChanged && State.Enabled == enabled && LoadError == null)
                return;

            byte[] image = imageChanged ? selectedImage : State.Custom ? store.ReadCustomImage() : null;
            PetArt art = null;
            if (enabled)
            {
                art = selectedArt ?? (!imageChanged ? currentArt : null) ?? Decode(image);
                if (art != currentArt)
                    window.Show(art);
            }
            try
            {
                store.Save(enabled, image);
            }
            catch
            {
                if (art != null && art != currentArt)
                {
                    if (currentArt == null)
                        window.Dispose();
                    else
                        window.Show(currentArt);
                }
                throw;
            }
            if (!enabled)
                window.Dispose();
            currentArt = art;
            State = new PetStore.State { Enabled = enabled, Custom = image != null };
            LoadError = null;
        }

        private void SavePosition(Native.Point point)
        {
            try
            {
                store.SavePosition(point);
            }
            catch (Exception error)
            {
                if (!IsStorageError(error))
                    throw;
            }
        }

        public void SetAwaitingWater(bool awaiting)
        {
            window.SetAwaitingWater(awaiting);
        }

        public void Celebrate()
        {
            window.Celebrate();
        }

        public void SetPaused(bool paused)
        {
            window.SetPaused(paused);
        }

        public static bool IsStorageError(Exception error)
        {
            return error is IOException || error is InvalidDataException ||
                error is UnauthorizedAccessException || error is SecurityException;
        }

        public void Dispose()
        {
            window.Dispose();
        }
    }
}
