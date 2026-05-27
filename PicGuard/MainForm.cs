using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace PicGuard
{
    /// <summary>
    /// 主窗体：按配置时间自动检查 + 监听 LotInfo.ini / 排期文件 / 配置文件变更 + 托盘守护
    /// </summary>
    public class MainForm : Form
    {
        private readonly string _machineId;
        private Label _status;

        private FileSystemWatcher _lotWatcher;
        private FileSystemWatcher _schedWatcher;
        private FileSystemWatcher _settingsWatcher;

        private Timer _heartbeatTimer;
        private Timer _guardTimer;

        private DateTime _lastDailyRunDate = DateTime.MinValue;
        private DateTime _lastLotFileChange = DateTime.MinValue;
        private DateTime _lastSchedFileChange = DateTime.MinValue;
        private DateTime _lastSettingsFileChange = DateTime.MinValue;
        private DateTime _lastNotRunningPrompt = DateTime.MinValue;
        private DateTime _lastFullScreenAlert = DateTime.MinValue;
        private DateTime _lastAutoStartAttempt = DateTime.MinValue;

        private bool _paused = false;
        private Process _pausedProcess = null;

        private const string TargetProcessName = "ReportWinFrom";
        private static readonly TimeSpan FullScreenAlertCooldown = TimeSpan.FromMinutes(10);
        private static readonly string ReportPathRememberFile = @"C:\icos\reportwinfrom_path.txt";
        private string _targetExePath = @"E:\ReportSoftware\Release\ReportWinfrom.exe";

        private NotifyIcon _tray;
        private ContextMenuStrip _trayMenu;
        private ToolStripMenuItem _menuOpen;
        private ToolStripMenuItem _menuAdmin;
        private ToolStripMenuItem _menuExit;

        public MainForm(string machineIdFromConfig)
        {
            Text = "PicGuard 4.0";
            Size = new Size(860, 180);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;

            _status = new Label();
            _status.Dock = DockStyle.Fill;
            _status.TextAlign = ContentAlignment.MiddleLeft;
            _status.Padding = new Padding(12);
            _status.Font = new Font("Microsoft YaHei UI", 10, FontStyle.Regular);
            _status.Text = "初始化中……";
            Controls.Add(_status);

            PicChecker.LoadSchedule();
            _machineId = !string.IsNullOrWhiteSpace(machineIdFromConfig)
                ? machineIdFromConfig.Trim()
                : MachineIdResolver.ResolveWithoutIni(PicChecker.DebugKeys());

            SetupTrayAndCloseProtection();
            Shown += MainForm_Shown;
        }

        private void MainForm_Shown(object sender, EventArgs e)
        {
            AutoStartHelper.ApplyFromSettings();
            RefreshStatusText("程序已启动。");
            EnsureDailyCheck("启动后检查");
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                PicChecker.LoadSchedule();
                SetupHeartbeatTimer();
                SetupLotFileWatcher();
                SetupScheduleWatcher();
                SetupSettingsWatcher();
                SetupGuardTimer();
                RefreshStatusText("初始化完成。");
                PicChecker.Log("<APP> MainForm 初始化完成。当前生效报警时间：" + PicSettingsStore.GetEffectiveAlarmText());
            }
            catch (Exception ex)
            {
                PicChecker.Log("<APP> OnHandleCreated 初始化异常：" + ex.Message);
            }
        }

        private void SetupHeartbeatTimer()
        {
            _heartbeatTimer = new Timer();
            _heartbeatTimer.Interval = 30000;
            _heartbeatTimer.Tick += HeartbeatTimer_Tick;
            _heartbeatTimer.Start();
            PicChecker.Log("<PIC-AUTO> 心跳定时器已启动（30秒间隔）。");
        }

        private void SetupGuardTimer()
        {
            _guardTimer = new Timer();
            _guardTimer.Interval = 15000;
            _guardTimer.Tick += GuardTimer_Tick;
            _guardTimer.Start();
            PicChecker.Log("<GUARD> 守护定时器已启动（15秒间隔）。");
        }

        private void HeartbeatTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                EnsureDailyCheck("心跳巡检");
                RefreshStatusText(null);
                AutoStartHelper.ApplyFromSettings();
            }
            catch (Exception ex)
            {
                PicChecker.Log("<PIC-AUTO> 心跳异常：" + ex.Message);
            }
        }

        private void EnsureDailyCheck(string sourceTag)
        {
            DateTime now = DateTime.Now;
            DateTime effectiveAlarm = PicSettingsStore.GetEffectiveAlarmDateTime(now.Date);
            if (_lastDailyRunDate != now.Date && now >= effectiveAlarm)
            {
                PicChecker.Log("<PIC-AUTO> 命中自动检测时间：" + effectiveAlarm.ToString("HH:mm") + "，来源=" + sourceTag);
                RunPicCheck(sourceTag + " @" + effectiveAlarm.ToString("HH:mm"));
                _lastDailyRunDate = now.Date;
            }
        }

        private void RefreshStatusText(string prefix)
        {
            bool todayPlan = PicChecker.ShouldRunToday(_machineId);
            string effective = PicSettingsStore.GetEffectiveAlarmText();
            string text = "机台：" + _machineId + "  今日计划：" + (todayPlan ? "是" : "否") + "  当日报警时间：" + effective;
            if (!string.IsNullOrEmpty(prefix)) text = "[" + DateTime.Now.ToString("HH:mm:ss") + "] " + prefix + "  " + text;
            _status.Text = text;
        }

        private void SetupLotFileWatcher()
        {
            try
            {
                string dir = @"C:\icos";
                string file = "LotInfo.ini";
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                _lotWatcher = new FileSystemWatcher(dir, file);
                _lotWatcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.Attributes | NotifyFilters.FileName;
                _lotWatcher.IncludeSubdirectories = false;
                _lotWatcher.InternalBufferSize = 64 * 1024;
                _lotWatcher.SynchronizingObject = this;
                _lotWatcher.Changed += LotWatcher_Fire;
                _lotWatcher.Created += LotWatcher_Fire;
                _lotWatcher.Renamed += LotWatcher_Renamed;
                _lotWatcher.Deleted += LotWatcher_Fire;
                _lotWatcher.EnableRaisingEvents = true;

                PicChecker.Log("<PIC-AUTO> LotInfo.ini 文件监控已启动。");
            }
            catch (Exception ex)
            {
                PicChecker.Log("<PIC-AUTO> LotInfo.ini 文件监控初始化异常：" + ex.Message);
            }
        }

        private void LotWatcher_Fire(object sender, FileSystemEventArgs e)
        {
            DateTime now = DateTime.Now;
            DateTime effectiveAlarm = PicSettingsStore.GetEffectiveAlarmDateTime(now.Date);

            if (now < effectiveAlarm)
            {
                PicChecker.Log("<PIC-AUTO> LotInfo.ini 变更发生在报警时间前（" + effectiveAlarm.ToString("HH:mm") + "），已忽略。");
                RefreshStatusText("报警时间前忽略 Lot 变更。");
                return;
            }

            if ((DateTime.Now - _lastLotFileChange).TotalSeconds < 1) return;
            _lastLotFileChange = DateTime.Now;

            if (!PicChecker.ShouldRunToday(_machineId))
            {
                PicChecker.Log("<PIC-AUTO> 非排期日，忽略 LotInfo.ini 变化。event=" + e.ChangeType);
                RefreshStatusText("非排期日，已忽略 Lot 变化。");
                return;
            }

            PicChecker.Log("<PIC-AUTO> 侦测到 LotInfo.ini 变化，触发一次 PIC 检测。event=" + e.ChangeType);
            RunPicCheck("LotInfo.ini 变化: " + e.ChangeType);
        }

        private void LotWatcher_Renamed(object sender, RenamedEventArgs e)
        {
            LotWatcher_Fire(sender, e);
        }

        private void SetupScheduleWatcher()
        {
            try
            {
                string path = PicChecker.PicScheduleFile;
                string dir = Path.GetDirectoryName(path) ?? @"C:\icos";
                string file = Path.GetFileName(path) ?? "pic_schedule.json";
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                _schedWatcher = new FileSystemWatcher(dir, file);
                _schedWatcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.Attributes | NotifyFilters.FileName;
                _schedWatcher.SynchronizingObject = this;
                _schedWatcher.Changed += ScheduleWatcher_Fire;
                _schedWatcher.Renamed += ScheduleWatcher_Renamed;
                _schedWatcher.EnableRaisingEvents = true;
            }
            catch (Exception ex)
            {
                PicChecker.Log("<PIC-SCHED> 计划文件监听初始化异常：" + ex.Message);
            }
        }

        private void ScheduleWatcher_Fire(object sender, FileSystemEventArgs e)
        {
            if ((DateTime.Now - _lastSchedFileChange).TotalSeconds < 1) return;
            _lastSchedFileChange = DateTime.Now;
            PicChecker.Log("<PIC-SCHED> 计划文件发生变化，重新加载。");
            PicChecker.LoadSchedule();
            RefreshStatusText("排期文件已重新加载。");
        }

        private void ScheduleWatcher_Renamed(object sender, RenamedEventArgs e)
        {
            if ((DateTime.Now - _lastSchedFileChange).TotalSeconds < 1) return;
            _lastSchedFileChange = DateTime.Now;
            PicChecker.Log("<PIC-SCHED> 计划文件重命名事件，重新加载。");
            PicChecker.LoadSchedule();
            RefreshStatusText("排期文件已重新加载。");
        }

        private void SetupSettingsWatcher()
        {
            try
            {
                string path = PicSettingsStore.SettingsFile;
                string dir = Path.GetDirectoryName(path) ?? @"C:\icos";
                string file = Path.GetFileName(path) ?? "pic_guard_settings.json";
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                _settingsWatcher = new FileSystemWatcher(dir, file);
                _settingsWatcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.Attributes | NotifyFilters.FileName;
                _settingsWatcher.SynchronizingObject = this;
                _settingsWatcher.Changed += SettingsWatcher_Fire;
                _settingsWatcher.Created += SettingsWatcher_Fire;
                _settingsWatcher.Renamed += SettingsWatcher_Renamed;
                _settingsWatcher.EnableRaisingEvents = true;
            }
            catch (Exception ex)
            {
                PicChecker.Log("<SETTINGS> 设置文件监听初始化异常：" + ex.Message);
            }
        }

        private void SettingsWatcher_Fire(object sender, FileSystemEventArgs e)
        {
            if ((DateTime.Now - _lastSettingsFileChange).TotalSeconds < 1) return;
            _lastSettingsFileChange = DateTime.Now;
            AutoStartHelper.ApplyFromSettings();
            RefreshStatusText("高级设置已更新。");
            PicChecker.Log("<SETTINGS> 设置文件已更新，当前生效报警时间：" + PicSettingsStore.GetEffectiveAlarmText());
        }

        private void SettingsWatcher_Renamed(object sender, RenamedEventArgs e)
        {
            SettingsWatcher_Fire(sender, e);
        }

        private bool TryLaunchReportWinfrom()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_targetExePath) || !File.Exists(_targetExePath))
                {
                    try
                    {
                        if (File.Exists(ReportPathRememberFile))
                        {
                            string p = (File.ReadAllText(ReportPathRememberFile) ?? string.Empty).Trim().Trim('"');
                            if (!string.IsNullOrEmpty(p) && File.Exists(p) && p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                                _targetExePath = p;
                        }
                    }
                    catch { }
                }

                if (string.IsNullOrWhiteSpace(_targetExePath) || !File.Exists(_targetExePath))
                {
                    MessageBox.Show(this,
                        "未能确定 ReportWinfrom.exe 的路径，请在 C:\\icos\\reportwinfrom_path.txt 写入绝对路径，或修正默认路径。",
                        "无法启动目标程序",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }

                string exeDir = Path.GetDirectoryName(_targetExePath) ?? string.Empty;
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = _targetExePath;
                psi.WorkingDirectory = exeDir;
                psi.UseShellExecute = true;
                Process.Start(psi);
                PicChecker.Log("<GUARD> 已尝试启动：" + _targetExePath);
                return true;
            }
            catch (Exception ex)
            {
                PicChecker.Log("<GUARD> 启动目标程序失败：" + ex.Message);
                MessageBox.Show(this, "启动 ReportWinfrom.exe 失败：\r\n" + ex.Message,
                    "启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        private bool EnsureReportRunningWithCooldown(int cooldownSeconds)
        {
            if ((DateTime.Now - _lastAutoStartAttempt).TotalSeconds < cooldownSeconds)
                return false;

            _lastAutoStartAttempt = DateTime.Now;
            Process proc;
            if (ProcessGuard.TryGetProcessByName(TargetProcessName, out proc))
                return true;
            return TryLaunchReportWinfrom();
        }

        private void GuardTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                Process proc;
                if (!ProcessGuard.TryGetProcessByName(TargetProcessName, out proc))
                {
                    if (EnsureReportRunningWithCooldown(30))
                    {
                        PicChecker.Log("<GUARD> 未检测到进程，已自动尝试启动目标程序。");
                        return;
                    }
                    if ((DateTime.Now - _lastNotRunningPrompt).TotalSeconds > 60)
                    {
                        _lastNotRunningPrompt = DateTime.Now;
                        ProcessGuard.ShowNotRunningPrompt(TargetProcessName);
                        PicChecker.Log("<GUARD> 未检测到目标程序运行：" + TargetProcessName);
                    }
                    return;
                }

                DateTime now = DateTime.Now;
                DateTime effectiveAlarm = PicSettingsStore.GetEffectiveAlarmDateTime(now.Date);
                bool needPicToday = PicChecker.ShouldRunToday(_machineId);
                string msg;
                bool hasTodayPic = PicChecker.TryCheckTodayPic(_machineId, out msg);

                if (now < effectiveAlarm)
                {
                    _status.Text = "[" + now.ToString("HH:mm:ss") + "] 机台：" + _machineId + " 今日计划：" + (needPicToday ? "是" : "否") + " （" + effectiveAlarm.ToString("HH:mm") + " 前不强制）";
                    return;
                }

                if (needPicToday && !hasTodayPic)
                {
                    if (!_paused)
                    {
                        if ((DateTime.Now - _lastFullScreenAlert) >= FullScreenAlertCooldown)
                        {
                            _lastFullScreenAlert = DateTime.Now;
                            ShowFullScreenAlert("今日未进行 PIC 验证！");
                            PicChecker.Log("<GUARD> 全屏提醒已弹出（10 分钟冷却）。");
                        }

                        PicChecker.Log("<GUARD> 需 PIC 且未完成，准备暂停并进入等待窗。");
                        if (ProcessGuard.SuspendProcess(proc))
                        {
                            _paused = true;
                            _pausedProcess = proc;
                            using (PicWaitDialog dlg = new PicWaitDialog(_machineId))
                            {
                                dlg.ShowDialog(this);
                            }
                            if (_paused && _pausedProcess != null)
                            {
                                ProcessGuard.ResumeProcess(_pausedProcess);
                                _paused = false;
                                _pausedProcess = null;
                                PicChecker.Log("<GUARD> PIC 完成，已恢复目标程序。");
                            }
                        }
                        else
                        {
                            MessageBox.Show(
                                "尝试暂停目标程序失败，请手动暂停或关闭后线程序，完成 PIC 后再继续。",
                                "暂停失败",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Warning);
                            PicChecker.Log("<GUARD> 暂停失败：可能权限不足或进程状态不允许。");
                        }
                    }
                }
                else
                {
                    if (_paused && _pausedProcess != null)
                    {
                        ProcessGuard.ResumeProcess(_pausedProcess);
                        _paused = false;
                        _pausedProcess = null;
                        PicChecker.Log("<GUARD> 当前无需 PIC 或已完成，恢复目标程序。");
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Guard 异常：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                PicChecker.Log("<GUARD> 异常：" + ex.Message);
            }
        }

        private void RunPicCheck(string sourceTag)
        {
            try
            {
                if (!PicChecker.ShouldRunToday(_machineId))
                {
                    PicChecker.Log("<PIC-AUTO> 今日非排期日，跳过。来源=" + sourceTag);
                    RefreshStatusText("今日非排期日，已跳过。");
                    return;
                }

                string msg;
                if (PicChecker.TryCheckTodayPic(_machineId, out msg))
                {
                    PicChecker.Log("<PIC-AUTO> 当日PNG已检测到，自动检测通过。来源=" + sourceTag);
                    RefreshStatusText("PIC 已完成。");
                }
                else
                {
                    PicChecker.Log("<PIC-AUTO> 未检测到当日PNG，触发全屏阻断报警。原因：" + msg + " 来源=" + sourceTag);
                    ShowFullScreenAlert("今日未进行 PIC 验证！");
                    RefreshStatusText("未检测到当日 PIC，已报警。");
                }
            }
            catch (Exception ex)
            {
                PicChecker.Log("<PIC-AUTO> 检测流程异常：" + ex.Message);
                RefreshStatusText("检测异常：" + ex.Message);
            }
        }

        private void ShowFullScreenAlert(string message)
        {
            using (Form alert = new Form())
            {
                alert.FormBorderStyle = FormBorderStyle.None;
                alert.WindowState = FormWindowState.Maximized;
                alert.StartPosition = FormStartPosition.CenterScreen;
                alert.BackColor = Color.DarkRed;
                alert.TopMost = true;
                alert.ShowInTaskbar = false;

                TableLayoutPanel layout = new TableLayoutPanel();
                layout.Dock = DockStyle.Fill;
                layout.BackColor = Color.DarkRed;
                layout.ColumnCount = 1;
                layout.RowCount = 3;
                layout.RowStyles.Add(new RowStyle(SizeType.Percent, 60));
                layout.RowStyles.Add(new RowStyle(SizeType.Percent, 25));
                layout.RowStyles.Add(new RowStyle(SizeType.Percent, 15));

                Label lblTitle = new Label();
                lblTitle.Dock = DockStyle.Fill;
                lblTitle.ForeColor = Color.White;
                lblTitle.TextAlign = ContentAlignment.MiddleCenter;
                lblTitle.Font = new Font("Microsoft YaHei UI", 56f, FontStyle.Bold);
                lblTitle.Text = message;

                Label lblHint = new Label();
                lblHint.Dock = DockStyle.Fill;
                lblHint.ForeColor = Color.White;
                lblHint.TextAlign = ContentAlignment.TopCenter;
                lblHint.Font = new Font("Microsoft YaHei UI", 28f, FontStyle.Regular);
                lblHint.Text = "当前管理员已将默认固定报警时间改为 08:15，可在高级设置中再次调整。";

                Button btnQuit = new Button();
                btnQuit.Anchor = AnchorStyles.None;
                btnQuit.AutoSize = true;
                btnQuit.BackColor = Color.White;
                btnQuit.ForeColor = Color.DarkRed;
                btnQuit.FlatStyle = FlatStyle.Flat;
                btnQuit.Font = new Font("Microsoft YaHei UI", 24f, FontStyle.Bold);
                btnQuit.Text = "立刻进行PIC检测";
                btnQuit.FlatAppearance.BorderSize = 0;
                btnQuit.Click += delegate { alert.Close(); };

                layout.Controls.Add(lblTitle, 0, 0);
                layout.Controls.Add(lblHint, 0, 1);
                layout.Controls.Add(btnQuit, 0, 2);
                alert.Controls.Add(layout);
                alert.ShowDialog(this);
            }
        }

        private void SetupTrayAndCloseProtection()
        {
            _trayMenu = new ContextMenuStrip();
            _menuOpen = new ToolStripMenuItem("打开窗口", null, OnOpenWindow);
            _menuAdmin = new ToolStripMenuItem("高级设置（需管理员密码）", null, OnOpenAdminSettings);
            _menuExit = new ToolStripMenuItem("退出（需管理员密码）", null, OnExitWithPassword);
            _trayMenu.Items.AddRange(new ToolStripItem[] { _menuOpen, _menuAdmin, new ToolStripSeparator(), _menuExit });

            _tray = new NotifyIcon();
            _tray.Text = "PicGuard 4.0";
            _tray.Visible = true;
            _tray.ContextMenuStrip = _trayMenu;
            _tray.Icon = SystemIcons.Shield;
            _tray.DoubleClick += OnOpenWindow;

            this.FormClosing += MainForm_FormClosing;
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                _tray.ShowBalloonTip(3000, "PicGuard", "程序仍在后台运行（托盘）。", ToolTipIcon.Info);
            }
        }

        private void OnOpenWindow(object sender, EventArgs e)
        {
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
        }

        private void OnOpenAdminSettings(object sender, EventArgs e)
        {
            AdminRole role;

            if (!AdminAuth.PromptAndGetRole(this,
                "请输入密码：\r\n普通权限可修改本机 PIC 排期；最高权限可修改任意机台 PIC 排期。",
                out role))
            {
                PicChecker.Log("<SECURITY> 密码错误，拒绝进入高级设置。");
                return;
            }

            using (AdminSettingsForm dlg = new AdminSettingsForm(_machineId, role))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    PicChecker.LoadSchedule();
                    AutoStartHelper.ApplyFromSettings();
                    RefreshStatusText("高级设置已保存。");
                    PicChecker.Log("<SECURITY> 用户进入高级设置并保存成功。role=" + role);
                }
            }
        }

        private void OnExitWithPassword(object sender, EventArgs e)
        {
            if (AdminAuth.PromptAndValidate(this, "请输入管理员密码："))
            {
                PicChecker.Log("<SECURITY> 管理员密码校验通过，进程即将退出。");
                _tray.Visible = false;
                Application.Exit();
            }
            else
            {
                PicChecker.Log("<SECURITY> 管理员密码错误，拒绝退出。");
            }
        }
    }
}
