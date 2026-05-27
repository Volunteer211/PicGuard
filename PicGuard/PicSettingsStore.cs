using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace PicGuard
{
    [DataContract]
    internal class PicSettings
    {
        [DataMember] public int ManualVerifyDay = -1; // -1=跟随共享排期，0~6=Sunday~Saturday
        [DataMember] public string IgnoreWeekMonday = ""; // yyyy-MM-dd；为空表示不忽略
        [DataMember] public string AlarmTime = "08:15";
        [DataMember] public int DelayMinutes = 0;
        [DataMember] public bool AutoStart = true;
    }

    internal static class PicSettingsStore
    {
        public static readonly string SettingsFile = @"C:\icos\pic_guard_settings.json";
        private static readonly object _sync = new object();

        public static PicSettings Load()
        {
            lock (_sync)
            {
                try
                {
                    if (!File.Exists(SettingsFile))
                    {
                        PicSettings defaults = CreateDefault();
                        Save(defaults);
                        return defaults;
                    }

                    using (FileStream fs = File.Open(SettingsFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        DataContractJsonSerializer ser = new DataContractJsonSerializer(typeof(PicSettings));
                        PicSettings data = ser.ReadObject(fs) as PicSettings;
                        if (data == null) data = CreateDefault();
                        Normalize(data);
                        return data;
                    }
                }
                catch (Exception ex)
                {
                    PicChecker.Log("<SETTINGS> 读取配置失败，回退默认值：" + ex.Message);
                    return CreateDefault();
                }
            }
        }

        public static void Save(PicSettings settings)
        {
            lock (_sync)
            {
                if (settings == null) settings = CreateDefault();
                Normalize(settings);
                try
                {
                    string dir = Path.GetDirectoryName(SettingsFile);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    using (FileStream fs = File.Open(SettingsFile, FileMode.Create, FileAccess.Write, FileShare.Read))
                    {
                        DataContractJsonSerializer ser = new DataContractJsonSerializer(typeof(PicSettings));
                        ser.WriteObject(fs, settings);
                    }
                }
                catch (Exception ex)
                {
                    PicChecker.Log("<SETTINGS> 保存配置失败：" + ex.Message);
                    throw;
                }
            }
        }

        public static PicSettings CreateDefault()
        {
            PicSettings s = new PicSettings();
            Normalize(s);
            return s;
        }

        public static DateTime GetEffectiveAlarmDateTime(DateTime date)
        {
            PicSettings s = Load();
            TimeSpan baseTime = ParseAlarmTime(s.AlarmTime);
            return date.Date + baseTime + TimeSpan.FromMinutes(s.DelayMinutes);
        }

        public static string GetEffectiveAlarmText()
        {
            DateTime dt = GetEffectiveAlarmDateTime(DateTime.Today);
            return dt.ToString("HH:mm");
        }

        public static bool IsIgnoringCurrentWeek(PicSettings settings)
        {
            if (settings == null) settings = Load();
            if (string.IsNullOrWhiteSpace(settings.IgnoreWeekMonday)) return false;

            DateTime ignoreMonday;
            if (!DateTime.TryParse(settings.IgnoreWeekMonday, out ignoreMonday)) return false;
            return ignoreMonday.Date == GetMonday(DateTime.Today).Date;
        }

        public static string GetCurrentWeekMondayText()
        {
            return GetMonday(DateTime.Today).ToString("yyyy-MM-dd");
        }

        public static DateTime GetMonday(DateTime day)
        {
            int diff = (7 + ((int)day.DayOfWeek) - (int)DayOfWeek.Monday) % 7;
            return day.Date.AddDays(-diff);
        }

        public static TimeSpan ParseAlarmTime(string text)
        {
            TimeSpan ts;
            if (TimeSpan.TryParse(text ?? string.Empty, out ts))
            {
                return new TimeSpan(ts.Hours, ts.Minutes, 0);
            }
            return new TimeSpan(8, 15, 0);
        }

        private static void Normalize(PicSettings settings)
        {
            if (settings == null) return;
            if (settings.ManualVerifyDay < -1 || settings.ManualVerifyDay > 6) settings.ManualVerifyDay = -1;
            if (string.IsNullOrWhiteSpace(settings.AlarmTime)) settings.AlarmTime = "08:15";
            TimeSpan ts = ParseAlarmTime(settings.AlarmTime);
            settings.AlarmTime = string.Format("{0:00}:{1:00}", ts.Hours, ts.Minutes);
            if (settings.DelayMinutes < 0) settings.DelayMinutes = 0;
            if (settings.DelayMinutes > 24 * 60) settings.DelayMinutes = 24 * 60;
        }
    }
}
