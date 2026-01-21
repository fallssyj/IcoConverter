using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace IcoConverter.Utils
{
    /// <summary>
    /// 工具类：将 Windows 快捷方式 (.lnk) 解析为真实目标路径。
    /// </summary>
    public static class ShortcutResolver
    {
        private const string ShortcutExtension = ".lnk";
        private const int StgmRead = 0;
        private const int MaxPath = 1024;

        /// <summary>
        /// 如果传入路径为快捷方式，则尝试解析其目标路径；否则返回原路径。
        /// </summary>
        public static string ResolveShortcutTarget(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !IsShortcut(path))
            {
                return path;
            }

            try
            {
                var shellLink = (IShellLinkW)new ShellLink();
                ((IPersistFile)shellLink).Load(path, StgmRead);

                var fileInfo = new WIN32_FIND_DATAW();
                var buffer = new StringBuilder(MaxPath);
                shellLink.GetPath(buffer, buffer.Capacity, out fileInfo, 0);

                var resolved = buffer.ToString();
                if (!string.IsNullOrWhiteSpace(resolved) && (File.Exists(resolved) || Directory.Exists(resolved)))
                {
                    return resolved;
                }
            }
            catch
            {
                // 解析失败时直接回退到原始路径。
            }

            return path;
        }

        private static bool IsShortcut(string path)
        {
            return string.Equals(Path.GetExtension(path), ShortcutExtension, StringComparison.OrdinalIgnoreCase);
        }

        #region COM interop

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WIN32_FIND_DATAW
        {
            public uint dwFileAttributes;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftCreationTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftLastAccessTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftLastWriteTime;
            public uint nFileSizeHigh;
            public uint nFileSizeLow;
            public uint dwReserved0;
            public uint dwReserved1;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string cFileName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
            public string cAlternateFileName;
        }

        [ComImport]
        [Guid("000214F9-0000-0000-C000-000000000046")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, out WIN32_FIND_DATAW pfd, uint fFlags);
            void GetIDList(out IntPtr ppidl);
            void SetIDList(IntPtr pidl);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
            void GetHotkey(out ushort pwHotkey);
            void SetHotkey(ushort wHotkey);
            void GetShowCmd(out int piShowCmd);
            void SetShowCmd(int iShowCmd);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
            void Resolve(IntPtr hwnd, uint fFlags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
        }

        [ComImport]
        [Guid("00021401-0000-0000-C000-000000000046")]
        private class ShellLink
        {
        }

        [ComImport]
        [Guid("0000010b-0000-0000-C000-000000000046")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPersistFile
        {
            void GetClassID(out Guid pClassID);
            [PreserveSig]
            int IsDirty();
            void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, int dwMode);
            void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, bool fRemember);
            void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
            void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
        }

        #endregion
    }
}
