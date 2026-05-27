using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace PicGuard
{
    public enum AdminRole
    {
        None = 0,
        Normal = 1,
        Super = 2
    }

    internal static class AdminAuth
    {
        private static readonly string PasswordFile = @"C:\icos\pic_guard.pass";

        private const string NormalPassword = "PicGuard@2025";
        private const string SuperPassword = "Delta9450";

        public static bool ValidateNormal(string input)
        {
            return string.Equals(input ?? string.Empty, NormalPassword, StringComparison.Ordinal);
        }

        public static bool ValidateSuper(string input)
        {
            string expected = ReadSuperPassword();
            return !string.IsNullOrEmpty(expected)
                && string.Equals(expected, input ?? string.Empty, StringComparison.Ordinal);
        }

        public static AdminRole GetRole(string input)
        {
            if (ValidateSuper(input)) return AdminRole.Super;
            if (ValidateNormal(input)) return AdminRole.Normal;
            return AdminRole.None;
        }

        public static string ReadSuperPassword()
        {
            try
            {
                if (File.Exists(PasswordFile))
                {
                    string s = File.ReadAllText(PasswordFile, Encoding.UTF8).Trim();
                    if (!string.IsNullOrEmpty(s)) return s;
                }
            }
            catch { }

            return SuperPassword;
        }

        public static bool PromptAndValidate(IWin32Window owner, string title)
        {
            string password;
            if (!PromptPassword(owner, title, out password)) return false;

            AdminRole role = GetRole(password);
            if (role != AdminRole.None) return true;

            MessageBox.Show(owner, "密码错误。", "认证失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        public static bool PromptAndGetRole(IWin32Window owner, string title, out AdminRole role)
        {
            role = AdminRole.None;

            string password;
            if (!PromptPassword(owner, title, out password)) return false;

            role = GetRole(password);
            if (role != AdminRole.None) return true;

            MessageBox.Show(owner, "密码错误。", "认证失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        public static bool PromptPassword(IWin32Window owner, string title, out string password)
        {
            password = string.Empty;

            using (Form box = new Form())
            using (TextBox tb = new TextBox())
            using (Button ok = new Button())
            using (Button cancel = new Button())
            {
                box.Text = "权限认证";
                box.StartPosition = FormStartPosition.CenterParent;
                box.FormBorderStyle = FormBorderStyle.FixedDialog;
                box.MaximizeBox = false;
                box.MinimizeBox = false;
                box.TopMost = true;
                box.ClientSize = new Size(390, 160);

                Label lbl = new Label();
                lbl.Text = string.IsNullOrEmpty(title) ? "请输入密码：" : title;
                lbl.AutoSize = true;
                lbl.Location = new Point(20, 30);

                tb.UseSystemPasswordChar = true;
                tb.Location = new Point(20, 60);
                tb.Width = 340;

                ok.Text = "确定";
                ok.Location = new Point(200, 110);
                ok.DialogResult = DialogResult.OK;

                cancel.Text = "取消";
                cancel.Location = new Point(290, 110);
                cancel.DialogResult = DialogResult.Cancel;

                box.Controls.AddRange(new Control[] { lbl, tb, ok, cancel });
                box.AcceptButton = ok;
                box.CancelButton = cancel;

                if (box.ShowDialog(owner) == DialogResult.OK)
                {
                    password = tb.Text ?? string.Empty;
                    return true;
                }
            }

            return false;
        }
    }
}