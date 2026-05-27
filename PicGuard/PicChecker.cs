using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace PicGuard
{
    /// <summary>
    /// 排期加载 + 当日PIC检测 + 简易日志（专为 .NET Framework 4.0 设计，不用 System.Web/System.Configuration）
    /// </summary>
    public static class PicChecker
    {
        public static string PicBasePath = @"\\10.192.144.114\icos team\PIC check";
        public static string PicScheduleFile = @"\\10.192.144.114\icos team\zhy\pic_schedule.json";
        private static readonly string LogFile = @"C:\icos\PicGuard.log";

        private static Dictionary<string, HashSet<DayOfWeek>> _picSchedule =
            new Dictionary<string, HashSet<DayOfWeek>>(StringComparer.OrdinalIgnoreCase);
        private static readonly object _lock = new object();

        public static void Log(string msg)
        {
            try
            {
                string dir = Path.GetDirectoryName(LogFile);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.AppendAllText(LogFile,
                    string.Format("[{0:yyyy-MM-dd HH:mm:ss}] {1}\r\n", DateTime.Now, msg),
                    Encoding.UTF8);
            }
            catch { }
        }

        public static IEnumerable<string> DebugKeys()
        {
            lock (_lock) { return _picSchedule.Keys.ToArray(); }
        }
        public static string ResolveMachineForCurrentSchedule(string machineId)
        {
            return ResolveMachineKey(machineId);
        }

        public static List<string> GetAllScheduleMachines()
        {
            lock (_lock)
            {
                return _picSchedule.Keys
                    .OrderBy(x => x)
                    .ToList();
            }
        }

        public static bool TryGetMachineScheduleDays(string machineId, out HashSet<DayOfWeek> days)
        {
            days = new HashSet<DayOfWeek>();

            if (string.IsNullOrWhiteSpace(machineId)) return false;

            string resolved = ResolveMachineKey(machineId);

            lock (_lock)
            {
                HashSet<DayOfWeek> set;
                if (_picSchedule.TryGetValue(resolved, out set) && set != null)
                {
                    days = new HashSet<DayOfWeek>(set);
                    return true;
                }
            }

            return false;
        }

        public static bool SaveMachineScheduleDay(string machineId, DayOfWeek day, out string error)
        {
            error = null;

            try
            {
                if (string.IsNullOrWhiteSpace(machineId))
                {
                    error = "机台名为空，无法保存排期。";
                    return false;
                }

                string machineKey = NormalizeKey(machineId);

                Dictionary<string, HashSet<DayOfWeek>> snapshot;

                lock (_lock)
                {
                    snapshot = new Dictionary<string, HashSet<DayOfWeek>>(_picSchedule, StringComparer.OrdinalIgnoreCase);
                }

                snapshot[machineKey] = new HashSet<DayOfWeek>();
                snapshot[machineKey].Add(day);

                SaveScheduleSnapshot(snapshot);

                LoadSchedule();

                Log("<PIC-SCHED> 已保存共享排期。machine='" + machineKey + "', day=" + day);
                return true;
            }
            catch (UnauthorizedAccessException ua)
            {
                error = "没有权限写入共享排期文件：" + ua.Message;
                Log("<PIC-SCHED> 保存共享排期失败，权限不足：" + ua.Message);
                return false;
            }
            catch (IOException io)
            {
                error = "共享排期文件正在被占用，或网络路径异常：" + io.Message;
                Log("<PIC-SCHED> 保存共享排期失败，IO异常：" + io.Message);
                return false;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                Log("<PIC-SCHED> 保存共享排期失败：" + ex.Message);
                return false;
            }
        }

        private static void SaveScheduleSnapshot(Dictionary<string, HashSet<DayOfWeek>> data)
        {
            string dir = Path.GetDirectoryName(PicScheduleFile);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            string tempFile = PicScheduleFile + ".tmp";

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("{");

            var keys = data.Keys.OrderBy(x => x).ToList();

            for (int i = 0; i < keys.Count; i++)
            {
                string key = keys[i];
                HashSet<DayOfWeek> set = data[key] ?? new HashSet<DayOfWeek>();

                string[] days = set
                    .OrderBy(x => DayToNumber(x))
                    .Select(x => "\"" + DayToNumber(x).ToString() + "\"")
                    .ToArray();

                sb.Append("  \"");
                sb.Append(EscapeJson(key));
                sb.Append("\": [");
                sb.Append(string.Join(", ", days));
                sb.Append("]");

                if (i < keys.Count - 1) sb.Append(",");
                sb.AppendLine();
            }

            sb.AppendLine("}");

            File.WriteAllText(tempFile, sb.ToString(), Encoding.UTF8);

            if (File.Exists(PicScheduleFile))
            {
                File.Copy(tempFile, PicScheduleFile, true);
                File.Delete(tempFile);
            }
            else
            {
                File.Move(tempFile, PicScheduleFile);
            }
        }

        private static int DayToNumber(DayOfWeek day)
        {
            switch (day)
            {
                case DayOfWeek.Monday: return 1;
                case DayOfWeek.Tuesday: return 2;
                case DayOfWeek.Wednesday: return 3;
                case DayOfWeek.Thursday: return 4;
                case DayOfWeek.Friday: return 5;
                case DayOfWeek.Saturday: return 6;
                case DayOfWeek.Sunday: return 7;
                default: return 1;
            }
        }

        private static string EscapeJson(string s)
        {
            if (s == null) return string.Empty;
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
        private static void ProbeFile(string path)
        {
            try
            {
                Log("<PROBE> Path = " + path);
                if (!File.Exists(path))
                {
                    Log("<PROBE> 文件不存在");
                    return;
                }
                FileInfo fi = new FileInfo(path);
                Log(string.Format("<PROBE> Exists={0}, Length={1}, LastWrite={2:yyyy-MM-dd HH:mm:ss}", fi.Exists, fi.Length, fi.LastWriteTime));
                string text = File.ReadAllText(path, Encoding.UTF8);
                string preview = text.Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");
                if (preview.Length > 200) preview = preview.Substring(0, 200) + "...";
                Log("<PROBE> Preview(200) = " + preview);
            }
            catch (Exception ex) { Log("<PROBE> 异常: " + ex.Message); }
        }

        private static string ReadTextSafe(string path)
        {
            using (FileStream fs = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (StreamReader sr = new StreamReader(fs, Encoding.UTF8, true))
            {
                return sr.ReadToEnd();
            }
        }

        private static string NormalizeJsonText(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            StringBuilder sb = new StringBuilder(s.Length);
            foreach (char ch in s)
            {
                if (char.IsControl(ch) && ch != '\r' && ch != '\n' && ch != '\t') continue;
                sb.Append(ch);
            }
            string t = sb.ToString();
            t = t.Replace('“', '"').Replace('”', '"').Replace('＂', '"');
            t = t.Replace('‘', '"').Replace('’', '"').Replace('＇', '"');
            if (t.Length > 0 && t[0] == '\uFEFF') t = t.Substring(1);
            return t;
        }

        private static string PickScheduleFileByDialog()
        {
            try
            {
                using (OpenFileDialog dlg = new OpenFileDialog())
                {
                    dlg.Title = "请选择 pic_schedule.json";
                    dlg.InitialDirectory = @"C:\icos";
                    dlg.Filter = "JSON 或 文本|*.json;*.txt|所有文件|*.*";
                    dlg.CheckFileExists = true;
                    if (dlg.ShowDialog() == DialogResult.OK)
                        return dlg.FileName;
                }
            }
            catch { }
            return null;
        }

        public static void LoadSchedule()
        {
            lock (_lock)
            {
                _picSchedule.Clear();
                string path = PicScheduleFile;
                Log("<PIC-SCHED> 尝试读取: " + path);
                ProbeFile(path);
                if (!File.Exists(path))
                {
                    string picked = PickScheduleFileByDialog();
                    if (!string.IsNullOrEmpty(picked))
                    {
                        PicScheduleFile = picked;
                        Log("<PIC-SCHED> 用户选择: " + picked);
                        ProbeFile(PicScheduleFile);
                    }
                }
                if (!File.Exists(PicScheduleFile))
                {
                    Log("<PIC-SCHED> 仍未找到排期文件，放弃加载。");
                    return;
                }
                string raw;
                try { raw = ReadTextSafe(PicScheduleFile); }
                catch (Exception ex)
                {
                    Log("<PIC-SCHED> 读取异常: " + ex.Message);
                    return;
                }
                string text = NormalizeJsonText(raw).Trim();
                if (string.IsNullOrWhiteSpace(text))
                {
                    Log("<PIC-SCHED> 内容为空。");
                    return;
                }
                bool ok = false;
                if (text.StartsWith("{"))
                {
                    ok = TryLoadJsonSchedule(text);
                    if (!ok)
                    {
                        Log("<PIC-SCHED> DCJS 解析后为 0 台，启用正则兜底解析 JSON。");
                        ok = TryLoadJsonScheduleRegex(text);
                    }
                }
                if (!ok)
                {
                    ok = TryLoadWhitespaceSchedule(text);
                }
                Log(string.Format("<PIC-SCHED> 加载结果：ok={0}，总机台数={1}", ok, _picSchedule.Count));
            }
        }

        private static bool TryLoadJsonSchedule(string jsonText)
        {
            try
            {
                using (MemoryStream ms = new MemoryStream(Encoding.UTF8.GetBytes(jsonText)))
                {
                    DataContractJsonSerializer ser = new DataContractJsonSerializer(typeof(Dictionary<string, object>));
                    Dictionary<string, object> root = (Dictionary<string, object>)ser.ReadObject(ms);
                    int count = 0;
                    foreach (KeyValuePair<string, object> kv in root)
                    {
                        string key = NormalizeKey(kv.Key);
                        HashSet<DayOfWeek> set = new HashSet<DayOfWeek>();
                        System.Collections.IEnumerable enumerable = kv.Value as System.Collections.IEnumerable;
                        if (enumerable == null)
                        {
                            Log("<PIC-SCHED> JSON 机台 '" + key + "' 值不是数组，忽略。");
                            continue;
                        }
                        foreach (object tokenObj in enumerable)
                        {
                            string token = tokenObj == null ? string.Empty : tokenObj.ToString();
                            DayOfWeek dow;
                            if (!string.IsNullOrWhiteSpace(token) && TryParseWeekday(token, out dow))
                                set.Add(dow);
                        }
                        if (set.Count > 0)
                        {
                            _picSchedule[key] = set;
                            count++;
                            Log("<PIC-SCHED> JSON 加载: '" + key + "' => [" + string.Join(",", set.ToArray()) + "]");
                        }
                    }
                    Log("<PIC-SCHED> JSON 加载完成（" + count + " 台）。");
                    return count > 0;
                }
            }
            catch (Exception ex)
            {
                Log("<PIC-SCHED> JSON 解析异常：" + ex.Message);
                return false;
            }
        }

        private static bool TryLoadJsonScheduleRegex(string text)
        {
            try
            {
                MatchCollection obj = Regex.Matches(text, "\"([^\"]+)\"\\s*:\\s*\\[(.*?)\\]", RegexOptions.Singleline);
                int count = 0;
                foreach (Match m in obj)
                {
                    string key = NormalizeKey(m.Groups[1].Value);
                    string arr = m.Groups[2].Value;
                    string[] items = arr.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(x => x.Trim().Trim('"', '“', '”', '‘', '’'))
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .ToArray();
                    HashSet<DayOfWeek> set = new HashSet<DayOfWeek>();
                    foreach (string token in items)
                    {
                        string t = token;
                        if (t.EndsWith(".0")) t = t.Substring(0, t.Length - 2);
                        DayOfWeek dow;
                        if (TryParseWeekday(t, out dow)) set.Add(dow);
                    }
                    if (set.Count > 0)
                    {
                        _picSchedule[key] = set;
                        count++;
                        Log("<PIC-SCHED> REGEX 加载: '" + key + "' => [" + string.Join(",", set.ToArray()) + "]");
                    }
                }
                Log("<PIC-SCHED> REGEX 加载完成（" + count + " 台）。");
                return count > 0;
            }
            catch (Exception ex)
            {
                Log("<PIC-SCHED> REGEX 解析异常：" + ex.Message);
                return false;
            }
        }

        private static bool TryLoadWhitespaceSchedule(string text)
        {
            try
            {
                string[] tokens = text.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                int added = 0;
                for (int i = 0; i + 1 < tokens.Length; i += 2)
                {
                    string machine = NormalizeKey(tokens[i]);
                    string dayToken = tokens[i + 1].Trim();
                    DayOfWeek dow;
                    if (!TryParseWeekday(dayToken, out dow)) continue;
                    HashSet<DayOfWeek> set;
                    if (!_picSchedule.TryGetValue(machine, out set))
                        _picSchedule[machine] = set = new HashSet<DayOfWeek>();
                    if (set.Add(dow)) added++;
                    Log("<PIC-SCHED> WHITESPACE 加载: '" + machine + "' => add " + dow);
                }
                Log(string.Format("<PIC-SCHED> WHITESPACE 加载完成（新增项数 {0}，机台总数 {1}）。", added, _picSchedule.Count));
                return _picSchedule.Count > 0;
            }
            catch (Exception ex)
            {
                Log("<PIC-SCHED> WHITESPACE 解析异常：" + ex.Message);
                return false;
            }
        }

        public static bool ShouldRunToday(string machineId)
        {
            if (string.IsNullOrWhiteSpace(machineId)) return false;

            PicSettings settings = PicSettingsStore.Load();

            if (PicSettingsStore.IsIgnoringCurrentWeek(settings))
            {
                Log("<PIC-SCHED> 本周已设置忽略 PIC。machine='" + machineId + "'");
                return false;
            }

            string resolved = ResolveMachineKey(machineId);

            lock (_lock)
            {
                HashSet<DayOfWeek> set;
                bool has = _picSchedule.TryGetValue(resolved, out set);
                bool hit = has && set != null && set.Contains(DateTime.Now.DayOfWeek);

                Log("<PIC-SCHED> ShouldRunToday? machine='" + machineId
                    + "' -> key='" + resolved
                    + "', source=shared_json, has=" + has
                    + ", hit=" + hit
                    + ", day=" + DateTime.Now.DayOfWeek);

                return hit;
            }
        }

        public static bool TryCheckTodayPic(string machineId, out string message)
        {
            message = string.Empty;
            try
            {
                string resolved = ResolveMachineKey(machineId);
                string machineFolder = Path.Combine(PicBasePath, resolved);
                if (!Directory.Exists(machineFolder))
                {
                    message = "未找到机器 " + resolved + " 的PIC目录，请检查共享盘文件夹命名。";
                    return false;
                }
                string todayTag = DateTime.Now.ToString("yyyyMMdd");
                foreach (string f in Directory.EnumerateFiles(machineFolder, "*.png", SearchOption.AllDirectories))
                {
                    string name = Path.GetFileNameWithoutExtension(f);
                    if (!string.IsNullOrEmpty(name) && name.IndexOf(todayTag, StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                }
                message = "今日未进行PIC验证。";
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                message = "无法访问PIC验证目录（权限不足）。";
                return false;
            }
            catch (IOException)
            {
                message = "无法访问PIC验证目录（网络或共享路径异常）。";
                return false;
            }
            catch (Exception ex)
            {
                message = "检查PIC验证时出现异常：" + ex.Message;
                return false;
            }
        }

        public static bool TryParseWeekday(string s, out DayOfWeek dow)
        {
            dow = DayOfWeek.Monday;
            if (string.IsNullOrWhiteSpace(s)) return false;
            switch ((s ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "1": case "mon": case "monday": case "星期一": case "周一": dow = DayOfWeek.Monday; return true;
                case "2": case "tue": case "tuesday": case "星期二": case "周二": dow = DayOfWeek.Tuesday; return true;
                case "3": case "wed": case "wednesday": case "星期三": case "周三": dow = DayOfWeek.Wednesday; return true;
                case "4": case "thu": case "thursday": case "星期四": case "周四": dow = DayOfWeek.Thursday; return true;
                case "5": case "fri": case "friday": case "星期五": case "周五": dow = DayOfWeek.Friday; return true;
                case "6": case "sat": case "saturday": case "星期六": case "周六": dow = DayOfWeek.Saturday; return true;
                case "7": case "sun": case "sunday": case "星期日": case "周日": dow = DayOfWeek.Sunday; return true;
                default: return false;
            }
        }

        private static string NormalizeKey(string key)
        {
            if (key == null) return string.Empty;
            string cleaned = new string(key.Where(ch => !char.IsControl(ch)).ToArray())
                .Replace('\u3000', ' ')
                .Trim()
                .ToUpperInvariant();
            return cleaned;
        }

        private static string ResolveMachineKey(string machineId)
        {
            string id = NormalizeKey(machineId);
            if (_picSchedule.ContainsKey(id)) return id;
            string alt1 = id.Replace('0', 'O');
            if (_picSchedule.ContainsKey(alt1)) return alt1;
            string alt2 = id.Replace('O', '0');
            if (_picSchedule.ContainsKey(alt2)) return alt2;
            foreach (string k in _picSchedule.Keys)
            {
                string nk = NormalizeKey(k);
                if (string.Equals(nk, id, StringComparison.OrdinalIgnoreCase))
                    return k;
            }
            return id;
        }
    }
}
