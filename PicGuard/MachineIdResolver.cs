
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace PicGuard
{
    /// <summary>
    /// 不依赖外部配置文件：自动用 Environment.MachineName，
    /// 与排期键进行规范化 / 0↔O / 包含关系 / 编辑距离 匹配；
    /// 若仍不命中，让用户从排期键里选择一次，并把选择写入 C:\icos\machine_id.txt 记忆。
    /// </summary>
    
    public static class MachineIdResolver
    {
        public static readonly string MappingFile = @"C:\icos\machine_id.txt";

        /// <summary>解析得到本机对应的排期机台键</summary>

        public static string ResolveWithoutIni(IEnumerable<string> scheduleKeys)
        {
            var keys = (scheduleKeys ?? new string[0])
                .Select(NormalizeKey)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            // 1) 记忆
            var remembered = ReadRemembered();
            if (!string.IsNullOrWhiteSpace(remembered))
            {
                var normR = NormalizeKey(remembered);
                var hit = keys.FirstOrDefault(k => string.Equals(NormalizeKey(k), normR, StringComparison.OrdinalIgnoreCase));
                if (hit != null) return hit;
            }

            // 2) 主机名容错匹配
            var host = Environment.MachineName ?? "";
            var candidate = BestMatch(host, keys);
            if (candidate != null) { Remember(candidate); return candidate; }

            // 3) 如果排期只有一个键，直接用它
            if (keys.Count == 1) { Remember(keys[0]); return keys[0]; }

            // 4) 弹窗选择并记忆
            var picked = PickMachineByDialog(keys);
            if (!string.IsNullOrWhiteSpace(picked)) { Remember(picked); return picked; }

            // 5) 兜底：返回规范化主机名
            return NormalizeKey(host);
        }

        public static string Resolve(IEnumerable<string> scheduleKeys)
        {
            // .NET 4.0 没有 Array.Empty<T>()，改为 new string[0]
            var keys = (scheduleKeys ?? new string[0])
                       .Select(NormalizeKey)
                       .Distinct(StringComparer.OrdinalIgnoreCase)
                       .ToList();

            // 1) 已有记忆映射
            var remembered = ReadRemembered();
            if (!string.IsNullOrWhiteSpace(remembered))
            {
                var normR = NormalizeKey(remembered);
                var hit = keys.FirstOrDefault(k => string.Equals(NormalizeKey(k), normR, StringComparison.OrdinalIgnoreCase));
                if (hit != null) return hit;
            }

            // 2) 尝试从 LotInfo.ini 猜（可选）
            var iniGuess = GuessFromLotInfoIni();
            if (!string.IsNullOrWhiteSpace(iniGuess))
            {
                var best = BestMatch(iniGuess, keys);
                if (best != null) { Remember(best); return best; }
            }

            // 3) 用主机名猜
            var host = Environment.MachineName ?? "";
            var candidate = BestMatch(host, keys);
            if (candidate != null) { Remember(candidate); return candidate; }

            // 4) 如果排期里只有一个键，直接用它（常见于单机测试）
            if (keys.Count == 1) { Remember(keys[0]); return keys[0]; }

            // 5) 兜底：让用户从排期键选择一次，并记忆
            var picked = PickMachineByDialog(keys);
            if (!string.IsNullOrWhiteSpace(picked))
            {
                Remember(picked);
                return picked;
            }

            // 6) 最后返回主机名（规范化后的），虽然大概率匹配不到
            return NormalizeKey(host);
        }

        // —— 实现细节 —— //

        private static string NormalizeKey(string s)
        {
            if (s == null) return string.Empty;
            // 去控制字符，Trim，Upper，去除中文全角空格
            var cleaned = new string(s.Where(ch => !char.IsControl(ch)).ToArray())
                            .Replace('\u3000', ' ')
                            .Trim()
                            .ToUpperInvariant();
            return cleaned;
        }

        /// <summary>根据 host 与 keys 的相似度挑选最佳</summary>
        private static string BestMatch(string hostName, List<string> keys)
        {
            if (string.IsNullOrWhiteSpace(hostName) || keys == null || keys.Count == 0) return null;

            var raw = NormalizeKey(hostName);
            var variants = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                raw,
                raw.Replace('0', 'O'),
                raw.Replace('O', '0')
            };

            string best = null;
            int bestScore = int.MinValue;

            foreach (var key in keys)
            {
                var k = NormalizeKey(key);

                // 评分：越高越好
                int score = -1000;

                if (variants.Contains(k)) score = 100;                                // 完全命中
                else if (variants.Any(v => v.Contains(k) || k.Contains(v))) score = 80; // 包含关系
                else
                {
                    // 编辑距离（Levenshtein）<=1 给 60
                    if (variants.Any(v => Levenshtein(v, k) <= 1)) score = 60;
                    else if (variants.Any(v => Levenshtein(v, k) <= 2)) score = 40;
                    else score = -Levenshtein(raw, k); // 越小越差
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    best = key;
                }
            }

            // 阈值：防止离谱匹配
            return bestScore >= 40 ? best : null;
        }

        private static int Levenshtein(string a, string b)
        {
            int n = a.Length, m = b.Length;
            if (n == 0) return m;
            if (m == 0) return n;
            var d = new int[n + 1, m + 1];
            for (int i = 0; i <= n; i++) d[i, 0] = i;
            for (int j = 0; j <= m; j++) d[0, j] = j;
            for (int i = 1; i <= n; i++)
                for (int j = 1; j <= m; j++)
                {
                    int cost = (a[i - 1] == b[j - 1]) ? 0 : 1;
                    d[i, j] = Math.Min(
                        Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                        d[i - 1, j - 1] + cost);
                }
            return d[n, m];
        }

        private static string PickMachineByDialog(List<string> keys)
        {
            try
            {
                using (var form = new Form())
                using (var list = new ListBox())
                using (var ok = new Button())
                using (var cancel = new Button())
                using (var tip = new Label())
                {
                    form.Text = "选择本机机台（仅第一次需要）";
                    form.StartPosition = FormStartPosition.CenterScreen;
                    form.FormBorderStyle = FormBorderStyle.FixedDialog;
                    form.MaximizeBox = false;
                    form.MinimizeBox = false;
                    form.TopMost = true;
                    form.ClientSize = new System.Drawing.Size(420, 360);

                    tip.Text = "未能从主机名匹配到排期机台，请选择本机对应的机台：";
                    tip.AutoSize = false;
                    tip.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
                    tip.SetBounds(10, 10, 400, 30);

                    list.Items.AddRange(keys.OrderBy(x => x).Cast<object>().ToArray());
                    list.SetBounds(10, 50, 400, 240);

                    ok.Text = "确定";
                    ok.SetBounds(220, 300, 90, 28);
                    ok.DialogResult = DialogResult.OK;

                    cancel.Text = "取消";
                    cancel.SetBounds(320, 300, 90, 28);
                    cancel.DialogResult = DialogResult.Cancel;

                    form.Controls.AddRange(new Control[] { tip, list, ok, cancel });
                    form.AcceptButton = ok;
                    form.CancelButton = cancel;

                    if (form.ShowDialog() == DialogResult.OK && list.SelectedItem != null)
                        return list.SelectedItem.ToString();
                }
            }
            catch { /* 忽略对话框异常 */ }
            return null;
        }

        private static string GuessFromLotInfoIni()
        {
            try
            {
                var path = @"C:\icos\LotInfo.ini";
                if (!File.Exists(path)) return null;

                // 读取几种常见写法的键
                var lines = File.ReadAllLines(path, Encoding.UTF8);
                string[] keys = { "MACHINE", "MACHINENAME", "EQPNAME", "NAME" };
                foreach (var line in lines)
                {
                    var s = line.Trim();
                    if (string.IsNullOrEmpty(s) || s.StartsWith("#") || s.StartsWith(";")) continue;
                    var idx = s.IndexOf('=');
                    if (idx <= 0) continue;
                    var k = s.Substring(0, idx).Trim().ToUpperInvariant();
                    var v = s.Substring(idx + 1).Trim();
                    if (keys.Contains(k)) return v;
                }
            }
            catch { }
            return null;
        }

        private static string ReadRemembered()
        {
            try
            {
                if (!File.Exists(MappingFile)) return null;
                var s = File.ReadAllText(MappingFile, Encoding.UTF8).Trim();
                return string.IsNullOrWhiteSpace(s) ? null : s;
            }
            catch { return null; }
        }

        private static void Remember(string machineKey)
        {
            try
            {
                var dir = Path.GetDirectoryName(MappingFile);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(MappingFile, machineKey ?? "", Encoding.UTF8);
            }
            catch { /* 忽略记忆失败 */ }
        }
    }
}
