using Microsoft.Win32;
using System;
using System.Runtime;
using System.Windows.Forms;

namespace PicGuard
{
    internal static class AutoStartHelper
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AppName = "PicGuard";

        public static void ApplyFromSettings()
        {
            try
            {
                PicSettings settings = PicSettingsStore.Load();
                SetAutoStart(settings.AutoStart);
            }
            catch (Exception ex)
            {
                PicChecker.Log("<AUTOSTART> 应用自启动设置失败：" + ex.Message);
            }
        }

        public static void SetAutoStart(bool enable)
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, true))
            {
                if (key == null) return;
                if (enable)
                {
                    string exePath = Application.ExecutablePath;
                    key.SetValue(AppName, "\"" + exePath + "\"");
                }
                else
                {
                    if (key.GetValue(AppName) != null)
                        key.DeleteValue(AppName, false);
                }
            }
        }

        public static bool IsEnabled()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, false))
            {
                if (key == null) return false;
                object value = key.GetValue(AppName);
                return value != null;
            }
        }
    }
}
