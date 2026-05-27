using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime;
using System.Windows.Forms;

namespace PicGuard
{
    public class AdminSettingsForm : Form
    {
        private readonly string _machineId;
        private ComboBox _cboDay;
        private CheckBox _chkIgnoreWeek;
        private DateTimePicker _dtpTime;
        private ComboBox _cboDelay;
        private CheckBox _chkAutoStart;
        private Label _lblInfo;
        private Button _btnSave;
        private Button _btnReset;
        private Button _btnCancel;
        private readonly AdminRole _role;
        private ComboBox _cboMachine;


        public AdminSettingsForm(string machineId, AdminRole role)
        {
            _machineId = machineId ?? string.Empty;
            _role = role;
            BuildUi();
            LoadData();
        }

        private void BuildUi()
        {
            SuspendLayout();
            Controls.Clear();

            Text = "高级设置";
            AutoScaleMode = AutoScaleMode.None;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            TopMost = true;
            BackColor = SystemColors.Control;
            ClientSize = new Size(800, 520);

            int labelX = 30;
            int controlX = 270;
            int y = 25;
            int rowHeight = 42;

            Label lblMachine = new Label();
            lblMachine.Text = "当前机台：" + _machineId;
            lblMachine.AutoSize = false;
            lblMachine.Location = new Point(labelX, y);
            lblMachine.Size = new Size(560, 24);
            lblMachine.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            lblMachine.BackColor = SystemColors.Control;

            y += rowHeight;

            Label lblSelectMachine = new Label();
            lblSelectMachine.Text = "修改机台：";
            lblSelectMachine.AutoSize = false;
            lblSelectMachine.Location = new Point(labelX, y + 4);
            lblSelectMachine.Size = new Size(220, 24);
            lblSelectMachine.BackColor = SystemColors.Control;

            _cboMachine = new ComboBox();
            _cboMachine.DropDownStyle = ComboBoxStyle.DropDownList;
            _cboMachine.Location = new Point(controlX, y);
            _cboMachine.Size = new Size(290, 24);
            _cboMachine.SelectedIndexChanged += delegate
            {
                LoadSelectedMachineSchedule();
            };

            y += rowHeight;

            Label lblDay = new Label();
            lblDay.Text = "PIC排期星期：";
            lblDay.AutoSize = false;
            lblDay.Location = new Point(labelX, y + 4);
            lblDay.Size = new Size(220, 24);
            lblDay.BackColor = SystemColors.Control;

            _cboDay = new ComboBox();
            _cboDay.DropDownStyle = ComboBoxStyle.DropDownList;
            _cboDay.Location = new Point(controlX, y);
            _cboDay.Size = new Size(290, 24);
            _cboDay.Items.AddRange(new object[]
            {
        "跟随共享排期",
        "周日",
        "周一",
        "周二",
        "周三",
        "周四",
        "周五",
        "周六"
            });

            y += rowHeight;

            _chkIgnoreWeek = new CheckBox();
            _chkIgnoreWeek.Text = "忽略本周 PIC（只影响本周）";
            _chkIgnoreWeek.AutoSize = true;
            _chkIgnoreWeek.Location = new Point(labelX, y + 2);
            _chkIgnoreWeek.BackColor = SystemColors.Control;

            y += rowHeight;

            Label lblTime = new Label();
            lblTime.Text = "每日固定 PIC 报警时间：";
            lblTime.AutoSize = false;
            lblTime.Location = new Point(labelX, y + 4);
            lblTime.Size = new Size(220, 24);
            lblTime.BackColor = SystemColors.Control;

            _dtpTime = new DateTimePicker();
            _dtpTime.Format = DateTimePickerFormat.Custom;
            _dtpTime.CustomFormat = "HH:mm";
            _dtpTime.ShowUpDown = true;
            _dtpTime.Location = new Point(controlX, y);
            _dtpTime.Size = new Size(150, 24);
            _dtpTime.ValueChanged += delegate
            {
                RefreshInfo();
            };

            y += rowHeight;

            Label lblDelay = new Label();
            lblDelay.Text = "延迟报警：";
            lblDelay.AutoSize = false;
            lblDelay.Location = new Point(labelX, y + 4);
            lblDelay.Size = new Size(220, 24);
            lblDelay.BackColor = SystemColors.Control;

            _cboDelay = new ComboBox();
            _cboDelay.DropDownStyle = ComboBoxStyle.DropDownList;
            _cboDelay.Location = new Point(controlX, y);
            _cboDelay.Size = new Size(150, 24);
            _cboDelay.Items.AddRange(new object[] { "0", "5", "10", "30", "60" });
            _cboDelay.SelectedIndexChanged += delegate
            {
                RefreshInfo();
            };

            y += rowHeight;

            _chkAutoStart = new CheckBox();
            _chkAutoStart.Text = "开机自动启动（当前用户）";
            _chkAutoStart.AutoSize = true;
            _chkAutoStart.Location = new Point(labelX, y + 2);
            _chkAutoStart.BackColor = SystemColors.Control;

            y += rowHeight;

            _lblInfo = new Label();
            _lblInfo.AutoSize = false;
            _lblInfo.Location = new Point(labelX, y);
            _lblInfo.Size = new Size(560, 34);
            _lblInfo.Text = "当前生效报警时间：08:15";
            _lblInfo.BackColor = SystemColors.Control;

            int buttonY = ClientSize.Height - 60;

            _btnSave = new Button();
            _btnSave.Text = "保存";
            _btnSave.Location = new Point(430, buttonY);
            _btnSave.Size = new Size(90, 30);
            _btnSave.UseVisualStyleBackColor = true;
            _btnSave.Click += OnSave;

            _btnReset = new Button();
            _btnReset.Text = "恢复默认";
            _btnReset.Location = new Point(540, buttonY);
            _btnReset.Size = new Size(90, 30);
            _btnReset.UseVisualStyleBackColor = true;
            _btnReset.Click += OnReset;

            _btnCancel = new Button();
            _btnCancel.Text = "取消";
            _btnCancel.Location = new Point(650, buttonY);
            _btnCancel.Size = new Size(90, 30);
            _btnCancel.UseVisualStyleBackColor = true;
            _btnCancel.Click += delegate
            {
                DialogResult = DialogResult.Cancel;
                Close();
            };

            Controls.Add(lblMachine);
            Controls.Add(lblSelectMachine);
            Controls.Add(_cboMachine);
            Controls.Add(lblDay);
            Controls.Add(_cboDay);
            Controls.Add(_chkIgnoreWeek);
            Controls.Add(lblTime);
            Controls.Add(_dtpTime);
            Controls.Add(lblDelay);
            Controls.Add(_cboDelay);
            Controls.Add(_chkAutoStart);
            Controls.Add(_lblInfo);
            Controls.Add(_btnSave);
            Controls.Add(_btnReset);
            Controls.Add(_btnCancel);

            ResumeLayout(false);
            PerformLayout();
        }

        private void LoadData()
        {
            PicChecker.LoadSchedule();

            _cboMachine.Items.Clear();

            if (_role == AdminRole.Super)
            {
                var machines = PicChecker.GetAllScheduleMachines();

                if (!machines.Contains(_machineId, StringComparer.OrdinalIgnoreCase))
                    machines.Insert(0, _machineId);

                foreach (string m in machines)
                    _cboMachine.Items.Add(m);

                _cboMachine.Enabled = true;
            }
            else
            {
                _cboMachine.Items.Add(_machineId);
                _cboMachine.Enabled = false;
            }

            if (_cboMachine.Items.Count > 0)
                _cboMachine.SelectedIndex = 0;

            PicSettings settings = PicSettingsStore.Load();

            TimeSpan ts = PicSettingsStore.ParseAlarmTime(settings.AlarmTime);
            _dtpTime.Value = DateTime.Today + ts;

            string delay = settings.DelayMinutes.ToString();
            int idx = _cboDelay.Items.IndexOf(delay);
            if (idx < 0) idx = 0;
            _cboDelay.SelectedIndex = idx;

            _chkAutoStart.Checked = settings.AutoStart;
            _chkIgnoreWeek.Checked = PicSettingsStore.IsIgnoringCurrentWeek(settings);

            LoadSelectedMachineSchedule();
            RefreshInfo();
        }
        private void LoadSelectedMachineSchedule()
        {
            string machine = Convert.ToString(_cboMachine.SelectedItem);
            if (string.IsNullOrWhiteSpace(machine)) machine = _machineId;

            HashSet<DayOfWeek> days;
            if (PicChecker.TryGetMachineScheduleDays(machine, out days) && days.Count > 0)
            {
                DayOfWeek day = days.First();
                _cboDay.SelectedIndex = MapDayToSelectedIndex((int)day);
            }
            else
            {
                _cboDay.SelectedIndex = 0;
            }
        }

        private void RefreshInfo()
        {
            PicSettings temp = PicSettingsStore.Load();
            temp.AlarmTime = _dtpTime.Value.ToString("HH:mm");
            int delay = 0;
            int.TryParse(Convert.ToString(_cboDelay.SelectedItem), out delay);
            temp.DelayMinutes = delay;
            _lblInfo.Text = "当前生效报警时间：" + (DateTime.Today + PicSettingsStore.ParseAlarmTime(temp.AlarmTime) + TimeSpan.FromMinutes(temp.DelayMinutes)).ToString("HH:mm");
        }

        private void OnReset(object sender, EventArgs e)
        {
            _cboDay.SelectedIndex = 0;
            _chkIgnoreWeek.Checked = false;
            _dtpTime.Value = DateTime.Today.AddHours(8).AddMinutes(15);
            _cboDelay.SelectedItem = "0";
            _chkAutoStart.Checked = true;
            RefreshInfo();
        }

        private void OnSave(object sender, EventArgs e)
        {
            try
            {
                string targetMachine = Convert.ToString(_cboMachine.SelectedItem);
                if (string.IsNullOrWhiteSpace(targetMachine))
                    targetMachine = _machineId;

                int selectedDay = MapSelectedDay(_cboDay.SelectedIndex);

                if (selectedDay < 0)
                {
                    MessageBox.Show(this,
                        "请选择一个具体的 PIC 星期。现在排期统一由共享 pic_schedule.json 管理，不建议再使用“跟随共享排期”作为保存项。",
                        "请选择排期",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                string error;
                if (!PicChecker.SaveMachineScheduleDay(targetMachine, (DayOfWeek)selectedDay, out error))
                {
                    MessageBox.Show(this,
                        "保存共享排期失败：\r\n" + error,
                        "保存失败",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                    return;
                }

                PicSettings settings = PicSettingsStore.Load();

                settings.AlarmTime = _dtpTime.Value.ToString("HH:mm");

                int delay = 0;
                int.TryParse(Convert.ToString(_cboDelay.SelectedItem), out delay);
                settings.DelayMinutes = delay;

                settings.AutoStart = _chkAutoStart.Checked;
                settings.IgnoreWeekMonday = _chkIgnoreWeek.Checked ? PicSettingsStore.GetCurrentWeekMondayText() : string.Empty;

                PicSettingsStore.Save(settings);
                AutoStartHelper.SetAutoStart(settings.AutoStart);

                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "保存设置失败：\r\n" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private int MapDayToSelectedIndex(int manualDay)
        {
            switch (manualDay)
            {
                case 0: return 1;
                case 1: return 2;
                case 2: return 3;
                case 3: return 4;
                case 4: return 5;
                case 5: return 6;
                case 6: return 7;
                default: return 0;
            }
        }

        private int MapSelectedDay(int selectedIndex)
        {
            if (selectedIndex <= 0) return -1;
            switch (selectedIndex)
            {
                case 1: return (int)DayOfWeek.Sunday;
                case 2: return (int)DayOfWeek.Monday;
                case 3: return (int)DayOfWeek.Tuesday;
                case 4: return (int)DayOfWeek.Wednesday;
                case 5: return (int)DayOfWeek.Thursday;
                case 6: return (int)DayOfWeek.Friday;
                case 7: return (int)DayOfWeek.Saturday;
                default: return -1;
            }
        }
    }
}
