using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using PassKeeper.Shared;

namespace PassKeeper.Setup
{
    /// <summary>
    /// Copies of PassKeeper started from one installation folder. Setup and uninstall close exactly these copies:
    /// a portable copy or another installation keeps running.
    /// </summary>
    internal static class RunningCopies
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inherit, int processId);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder path, ref int size);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetNamedPipeServerProcessId(Microsoft.Win32.SafeHandles.SafePipeHandle pipe, out int serverProcessId);

        private const uint ProcessQueryLimitedInformation = 0x1000;

        /// <summary>Full path of a process image; works for other users' processes as far as the rights allow.</summary>
        public static string ImagePath(int pid)
        {
            var handle = OpenProcess(ProcessQueryLimitedInformation, false, pid);
            if (handle == IntPtr.Zero) return null;
            try
            {
                var size = 1024;
                var sb = new StringBuilder(size);
                return QueryFullProcessImageName(handle, 0, sb, ref size) ? sb.ToString() : null;
            }
            finally
            {
                CloseHandle(handle);
            }
        }

        /// <summary>PassKeeper processes whose executable lies in the folder.</summary>
        public static List<Process> In(string dir)
        {
            var root = Path.GetFullPath(dir).TrimEnd('\\') + "\\";
            var result = new List<Process>();
            foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(InstallLayout.ExeName)))
            {
                var path = ImagePath(p.Id);
                if (path != null && path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) result.Add(p);
                else p.Dispose();
            }
            return result;
        }

        /// <summary>
        /// Asks the copies from the folder to exit (the one listening on the current user's pipe gets "EXIT"), waits,
        /// then ends those that are still running so that their files can be replaced or removed.
        /// </summary>
        public static void Close(string dir, int gracefulMs = 6000)
        {
            var running = In(dir);
            if (running.Count == 0) return;
            var pids = new HashSet<int>(running.Select(p => p.Id));
            try
            {
                using (var client = new NamedPipeClientStream(".", InstallLayout.PipeName(), PipeDirection.Out))
                {
                    client.Connect(800);
                    int server;
                    if (GetNamedPipeServerProcessId(client.SafePipeHandle, out server) && pids.Contains(server))
                    {
                        using (var w = new StreamWriter(client))
                        {
                            w.WriteLine("EXIT");
                            w.Flush();
                        }
                    }
                }
            }
            catch (Exception)
            {
                // no copy of this user listens: the processes are ended below
            }

            var deadline = DateTime.UtcNow.AddMilliseconds(gracefulMs);
            while (DateTime.UtcNow < deadline && running.Any(p => !HasExited(p))) Thread.Sleep(200);
            foreach (var p in running.Where(p => !HasExited(p)))
            {
                try
                {
                    p.Kill();
                    p.WaitForExit(3000);
                }
                catch (Exception)
                {
                    // another user's copy without the rights to end it: its files are renamed if locked
                }
            }
            foreach (var p in running) p.Dispose();
        }

        private static bool HasExited(Process p)
        {
            try { return p.HasExited; }
            catch (Exception) { return true; }
        }
    }
}
