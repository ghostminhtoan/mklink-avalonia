using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using MkLink.Models;

namespace MkLink.Services
{
    public static class SymlinkScanner
    {
        private const uint FileShareReadWriteDelete = 0x00000001 | 0x00000002 | 0x00000004;
        private const uint FileFlagsBackupSemantics = 0x02000000;

        [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile);

        [DllImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int GetFinalPathNameByHandle(
            SafeFileHandle hFile,
            StringBuilder lpszFilePath,
            int cchFilePath,
            uint dwFlags);

        public static List<MklinkCheckEntry> BuildEntries(IEnumerable<PathItem> sourceItems)
        {
            var entries = new List<MklinkCheckEntry>();
            if (sourceItems == null)
            {
                return entries;
            }

            int index = 0;
            foreach (var item in sourceItems)
            {
                if (item == null || item.IsPlaceholder || item.IsEmpty)
                {
                    index++;
                    continue;
                }

                string normalizedInput = item.NormalizedInput.Trim().Trim('"');
                string outputPath = item.EffectiveOutputPath.Trim().Trim('"');

                if (string.IsNullOrWhiteSpace(normalizedInput))
                {
                    index++;
                    continue;
                }

                bool exists = false;
                bool isLink = false;
                bool destinationCheck = false;
                string alreadyMklinkTo = string.Empty;
                string status = "Missing";

                try
                {
                    string expanded = Environment.ExpandEnvironmentVariables(normalizedInput).Trim();
                    exists = Directory.Exists(expanded) || File.Exists(expanded);

                    if (exists)
                    {
                        FileAttributes attributes = File.GetAttributes(expanded);
                        isLink = (attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint;
                        status = isLink ? "MKLINK" : "Real folder";

                        if (isLink)
                        {
                            TryResolveLinkTarget(expanded, out alreadyMklinkTo);
                        }
                    }

                    string expandedOutput = Environment.ExpandEnvironmentVariables(outputPath).Trim();
                    destinationCheck = !string.IsNullOrWhiteSpace(expandedOutput) && Directory.Exists(expandedOutput);
                }
                catch
                {
                    status = "Invalid path";
                }

                entries.Add(new MklinkCheckEntry(index, normalizedInput, outputPath, exists, isLink, destinationCheck, alreadyMklinkTo, status));
                index++;
            }

            return entries;
        }

        public static bool TryResolveLinkTarget(string path, out string resolvedPath)
        {
            resolvedPath = string.Empty;
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            // Thử dùng API native của .NET trước
            try
            {
                if (Directory.Exists(path))
                {
                    var dirInfo = new DirectoryInfo(path);
                    var target = dirInfo.ResolveLinkTarget(true);
                    if (target != null)
                    {
                        resolvedPath = target.FullName;
                        return true;
                    }
                }
                else if (File.Exists(path))
                {
                    var fileInfo = new FileInfo(path);
                    var target = fileInfo.ResolveLinkTarget(true);
                    if (target != null)
                    {
                        resolvedPath = target.FullName;
                        return true;
                    }
                }
            }
            catch
            {
            }

            // Fallback sang Win32 API GetFinalPathNameByHandle
            try
            {
                using (SafeFileHandle handle = CreateFile(
                    path,
                    0,
                    FileShareReadWriteDelete,
                    IntPtr.Zero,
                    (uint)FileMode.Open,
                    FileFlagsBackupSemantics,
                    IntPtr.Zero))
                {
                    if (handle == null || handle.IsInvalid)
                    {
                        return false;
                    }

                    var buffer = new StringBuilder(1024);
                    int result = GetFinalPathNameByHandle(handle, buffer, buffer.Capacity, 0);
                    if (result <= 0)
                    {
                        return false;
                    }

                    string raw = buffer.ToString();
                    if (raw.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
                    {
                        resolvedPath = @"\\" + raw.Substring(8);
                        return true;
                    }

                    if (raw.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase))
                    {
                        resolvedPath = raw.Substring(4);
                        return true;
                    }

                    resolvedPath = raw;
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }
    }
}
