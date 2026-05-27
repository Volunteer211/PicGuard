using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PicGuard
{
    public static class ProcessGuard
    {
        public static bool TryGetProcessByName(string exeOrProcName, out Process proc)
        {
            proc = null;
            if (string.IsNullOrWhiteSpace(exeOrProcName)) return false;
            string name = exeOrProcName.Trim();
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                name = name.Substring(0, name.Length - 4);
            Process[] ps = Process.GetProcessesByName(name);
            if (ps != null && ps.Length > 0)
            {
                foreach (Process p in ps)
                {
                    if (!string.IsNullOrEmpty(p.MainWindowTitle)) { proc = p; return true; }
                }
                proc = ps[0];
                return true;
            }
            return false;
        }

        public static void ShowNotRunningPrompt(string procName)
        {
            try
            {
                MessageBox.Show(
                    "未检测到目标程序正在运行： " + procName + "\r\n请先启动后线程序再继续。",
                    "运行提示",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
            catch { }
        }

        public static bool SuspendProcess(Process process)
        {
            try
            {
                int pid = process.Id;
                IntPtr snapshot = Native.CreateToolhelp32Snapshot(Native.TH32CS_SNAPTHREAD, 0);
                if (snapshot == IntPtr.Zero || snapshot == (IntPtr)(-1)) return false;
                Native.THREADENTRY32 te = new Native.THREADENTRY32 { dwSize = (uint)Marshal.SizeOf(typeof(Native.THREADENTRY32)) };
                if (Native.Thread32First(snapshot, ref te))
                {
                    do
                    {
                        if (te.th32OwnerProcessID == (uint)pid)
                        {
                            IntPtr hThread = Native.OpenThread(Native.THREAD_SUSPEND_RESUME, false, te.th32ThreadID);
                            if (hThread != IntPtr.Zero)
                            {
                                Native.SuspendThread(hThread);
                                Native.CloseHandle(hThread);
                            }
                        }
                    } while (Native.Thread32Next(snapshot, ref te));
                }
                Native.CloseHandle(snapshot);
                return true;
            }
            catch { return false; }
        }

        public static bool ResumeProcess(Process process)
        {
            try
            {
                int pid = process.Id;
                IntPtr snapshot = Native.CreateToolhelp32Snapshot(Native.TH32CS_SNAPTHREAD, 0);
                if (snapshot == IntPtr.Zero || snapshot == (IntPtr)(-1)) return false;
                Native.THREADENTRY32 te = new Native.THREADENTRY32 { dwSize = (uint)Marshal.SizeOf(typeof(Native.THREADENTRY32)) };
                if (Native.Thread32First(snapshot, ref te))
                {
                    do
                    {
                        if (te.th32OwnerProcessID == (uint)pid)
                        {
                            IntPtr hThread = Native.OpenThread(Native.THREAD_SUSPEND_RESUME, false, te.th32ThreadID);
                            if (hThread != IntPtr.Zero)
                            {
                                while (Native.ResumeThread(hThread) > 0) { }
                                Native.CloseHandle(hThread);
                            }
                        }
                    } while (Native.Thread32Next(snapshot, ref te));
                }
                Native.CloseHandle(snapshot);
                return true;
            }
            catch { return false; }
        }

        private static class Native
        {
            internal const uint TH32CS_SNAPTHREAD = 0x00000004;
            internal const uint THREAD_SUSPEND_RESUME = 0x0002;

            [StructLayout(LayoutKind.Sequential)]
            internal struct THREADENTRY32
            {
                public uint dwSize;
                public uint cntUsage;
                public uint th32ThreadID;
                public uint th32OwnerProcessID;
                public int tpBasePri;
                public int tpDeltaPri;
                public uint dwFlags;
            }

            [DllImport("kernel32.dll", SetLastError = true)]
            internal static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);
            [DllImport("kernel32.dll", SetLastError = true)]
            internal static extern bool Thread32First(IntPtr hSnapshot, ref THREADENTRY32 lpte);
            [DllImport("kernel32.dll", SetLastError = true)]
            internal static extern bool Thread32Next(IntPtr hSnapshot, ref THREADENTRY32 lpte);
            [DllImport("kernel32.dll", SetLastError = true)]
            internal static extern IntPtr OpenThread(uint dwDesiredAccess, bool bInheritHandle, uint dwThreadId);
            [DllImport("kernel32.dll", SetLastError = true)]
            internal static extern uint SuspendThread(IntPtr hThread);
            [DllImport("kernel32.dll", SetLastError = true)]
            internal static extern uint ResumeThread(IntPtr hThread);
            [DllImport("kernel32.dll", SetLastError = true)]
            internal static extern bool CloseHandle(IntPtr hObject);
        }
    }
}
