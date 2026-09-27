using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace PassKeeper.Setup
{
    /// <summary>Creates .lnk shortcuts through the Windows Shell (IShellLinkW + IPersistFile).</summary>
    internal static class ShellLink
    {
        [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
        private class CShellLink
        {
        }

        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
        private interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cchMaxPath, IntPtr pfd, int fFlags);
            void GetIDList(out IntPtr ppidl);
            void SetIDList(IntPtr pidl);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cchMaxName);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cchMaxPath);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cchMaxPath);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
            void GetHotkey(out short pwHotkey);
            void SetHotkey(short wHotkey);
            void GetShowCmd(out int piShowCmd);
            void SetShowCmd(int iShowCmd);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cchIconPath, out int piIcon);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, int dwReserved);
            void Resolve(IntPtr hwnd, int fFlags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
        }

        public static void Create(string shortcutPath, string target, string arguments, string description)
        {
            var link = (IShellLinkW)new CShellLink();
            try
            {
                link.SetPath(target);
                link.SetArguments(arguments ?? "");
                link.SetWorkingDirectory(System.IO.Path.GetDirectoryName(target));
                link.SetDescription(description ?? "");
                link.SetIconLocation(target, 0);
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(shortcutPath));
                ((IPersistFile)link).Save(shortcutPath, true);
            }
            finally
            {
                Marshal.FinalReleaseComObject(link);
            }
        }
    }
}
