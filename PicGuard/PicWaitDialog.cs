using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace PicGuard
{
    public class PicWaitDialog : Form
    {
        private readonly string _machine;
        private Timer _pollTimer;
        private Label _label;
        private Button _btnRetry;
        private Button _btnConfirmShot;

        public PicWaitDialog(string machineKey)
        {
            _machine = machineKey ?? string.Empty;
            this.Text = "PIC 待完成";
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.TopMost = true;
            this.ClientSize = new System.Drawing.Size(520, 190);

            _label = new Label();
            _label.AutoSize = false;
            _label.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            _label.Text =
                "今天需要进行 PIC 验证，已暂停后线程序。\r\n" +
                "你可以：\r\n" +
                "1）完成 PIC 后点击“我已完成，继续”（仅检测共享盘是否已有当天 PNG）\r\n" +
                "2）或点击“确认完成PIC（截图保存）”，输入管理员密码后自动全屏截图保存到共享盘。";
            _label.Left = 10;
            _label.Top = 10;
            _label.Width = 500;
            _label.Height = 95;

            _btnRetry = new Button();
            _btnRetry.Text = "我已完成，继续";
            _btnRetry.Left = 330;
            _btnRetry.Top = 125;
            _btnRetry.Width = 170;
            _btnRetry.Height = 30;
            _btnRetry.Click += delegate { TryCloseIfPicReady(); };

            _btnConfirmShot = new Button();
            _btnConfirmShot.Text = "确认完成PIC（截图保存）";
            _btnConfirmShot.Left = 10;
            _btnConfirmShot.Top = 125;
            _btnConfirmShot.Width = 300;
            _btnConfirmShot.Height = 30;
            _btnConfirmShot.Click += delegate { ConfirmAndSaveScreenshot(); };

            this.Controls.Add(_label);
            this.Controls.Add(_btnRetry);
            this.Controls.Add(_btnConfirmShot);

            _pollTimer = new Timer();
            _pollTimer.Interval = 5000;
            _pollTimer.Tick += delegate { TryCloseIfPicReady(); };
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _pollTimer.Start();
        }

        private void TryCloseIfPicReady()
        {
            string msg;
            if (PicChecker.TryCheckTodayPic(_machine, out msg))
            {
                _pollTimer.Stop();
                this.DialogResult = DialogResult.OK;
                this.Close();
            }
            else
            {
                _label.Text =
                    "今天需要进行 PIC 验证，已暂停后线程序。\r\n" +
                    "状态：" + msg + "\r\n" +
                    "完成后点击“我已完成，继续”，或用“确认完成PIC（截图保存）”自动生成当天 PNG。";
            }
        }

        private void ConfirmAndSaveScreenshot()
        {
            try
            {
                DialogResult confirm = MessageBox.Show(
                    this,
                    "是否确认完成 PIC 检测？\r\n确认后将输入管理员密码并自动全屏截图保存到共享盘（同日会覆盖）。",
                    "确认完成 PIC",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);
                if (confirm != DialogResult.Yes) return;

                if (!AdminAuth.PromptAndValidate(this, "请输入管理员密码：")) return;

                string savedPath;
                string err;
                if (!TrySaveFullScreenShotToPicShare(_machine, out savedPath, out err))
                {
                    MessageBox.Show(this, "保存失败：\r\n" + err, "截图保存失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                MessageBox.Show(this, "截图已保存成功：\r\n" + savedPath, "保存成功",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                _pollTimer.Stop();
                this.DialogResult = DialogResult.OK;
                this.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "操作异常：\r\n" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static bool TrySaveFullScreenShotToPicShare(string machineKey, out string savedPath, out string error)
        {
            savedPath = null;
            error = null;
            try
            {
                string safeMachine = SanitizeFolderName(machineKey);
                if (string.IsNullOrWhiteSpace(safeMachine)) safeMachine = "UNKNOWN";
                string folder = Path.Combine(PicChecker.PicBasePath, safeMachine);
                Directory.CreateDirectory(folder);

                string fileName = DateTime.Now.ToString("yyyyMMdd") + ".png";
                string fullPath = Path.Combine(folder, fileName);

                Rectangle bounds = SystemInformation.VirtualScreen;
                using (Bitmap bmp = new Bitmap(bounds.Width, bounds.Height))
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size);
                    bmp.Save(fullPath, ImageFormat.Png);
                }
                savedPath = fullPath;
                return true;
            }
            catch (UnauthorizedAccessException ua)
            {
                error = "无权限访问共享路径/文件夹。\r\n" + ua.Message;
                return false;
            }
            catch (IOException io)
            {
                error = "网络共享路径异常或文件被占用。\r\n" + io.Message;
                return false;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static string SanitizeFolderName(string name)
        {
            string s = (name ?? string.Empty).Trim();
            if (s.Length == 0) return s;
            char[] invalid = Path.GetInvalidFileNameChars().Concat(Path.GetInvalidPathChars()).Distinct().ToArray();
            char[] arr = s.ToCharArray();
            for (int i = 0; i < arr.Length; i++)
            {
                if (invalid.Contains(arr[i])) arr[i] = '_';
            }
            return new string(arr).Trim();
        }
    }
}
