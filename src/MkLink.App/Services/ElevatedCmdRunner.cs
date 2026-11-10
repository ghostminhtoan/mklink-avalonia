using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace MkLink.Services
{
    public static class ElevatedCmdRunner
    {
        private static readonly object _lock = new object();
        private static readonly List<Process> _launchedProcesses = new List<Process>();

        public static string BuildBatchContent(string[]? stageNames, params string[] scripts)
        {
            var builder = new StringBuilder();
            builder.AppendLine("@echo off");
            builder.AppendLine("chcp 65001 > nul");
            builder.AppendLine("setlocal");
            builder.AppendLine("echo Current directory: %CD%");

            var stageScripts = new List<KeyValuePair<string, string>>();
            for (int i = 0; i < scripts.Length; i++)
            {
                string script = scripts[i];
                if (string.IsNullOrWhiteSpace(script))
                {
                    continue;
                }

                string stageName;
                if (stageNames != null && i < stageNames.Length && !string.IsNullOrWhiteSpace(stageNames[i]))
                {
                    stageName = stageNames[i];
                }
                else if (i == 0)
                {
                    stageName = "COPY";
                }
                else if (i == 1)
                {
                    stageName = scripts.Length == 2 ? "MKLINK D" : "DELETE";
                }
                else
                {
                    stageName = "MKLINK D";
                }

                stageScripts.Add(new KeyValuePair<string, string>(stageName, script));
            }

            if (stageScripts.Count == 0)
            {
                return string.Empty;
            }

            for (int i = 0; i < stageScripts.Count; i++)
            {
                string stageName = stageScripts[i].Key;
                string script = stageScripts[i].Value;

                builder.AppendLine("echo === [" + stageName + "] ===");
                string[] lines = script.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
                for (int j = 0; j < lines.Length; j++)
                {
                    string line = lines[j].Trim();
                    if (line.Length == 0)
                    {
                        continue;
                    }

                    builder.AppendLine("echo ^> " + EscapeEcho(line));
                    builder.AppendLine(line);
                }

                builder.AppendLine();
            }

            builder.AppendLine("echo ================================");
            builder.AppendLine("echo Tat ca cac lenh da hoan tat.");
            builder.AppendLine("echo ================================");
            return builder.ToString();
        }

        private static string EscapeEcho(string line)
        {
            return line
                .Replace("&", "^&")
                .Replace("<", "^<")
                .Replace(">", "^>")
                .Replace("|", "^|");
        }

        public static bool Run(string[]? stageNames, Action? onProcessExited, out string errorMessage, params string[] scripts)
        {
            errorMessage = string.Empty;
            string batchContent = BuildBatchContent(stageNames, scripts);
            if (string.IsNullOrWhiteSpace(batchContent))
            {
                errorMessage = "Không có lệnh nào để chạy.";
                return false;
            }

            string batchPath = Path.Combine(Path.GetTempPath(), "MkLink_GMTPC_" + Guid.NewGuid().ToString("N") + ".cmd");
            try
            {
                File.WriteAllText(batchPath, batchContent, Encoding.UTF8);

                string system32Path = Environment.SystemDirectory;
                var startInfo = new ProcessStartInfo
                {
                    FileName = Path.Combine(system32Path, "cmd.exe"),
                    Arguments = "/d /k call \"" + batchPath + "\"",
                    UseShellExecute = true,
                    Verb = "runas",
                    WorkingDirectory = system32Path
                };

                Process? process = Process.Start(startInfo);
                if (process != null)
                {
                    RegisterLaunchedProcess(process, onProcessExited);
                    return true;
                }
                else
                {
                    errorMessage = "Không thể khởi động cmd.exe";
                    return false;
                }
            }
            catch (Win32Exception ex)
            {
                errorMessage = "Lỗi cấp quyền Quản trị (UAC cancelled hoặc bị từ chối): " + ex.Message;
                return false;
            }
            catch (Exception ex)
            {
                errorMessage = "Lỗi thực thi lệnh: " + ex.Message;
                return false;
            }
        }

        private static void RegisterLaunchedProcess(Process process, Action? onProcessExited)
        {
            lock (_lock)
            {
                _launchedProcesses.Add(process);
            }

            process.EnableRaisingEvents = true;
            process.Exited += (s, e) =>
            {
                lock (_lock)
                {
                    _launchedProcesses.Remove(process);
                }

                try
                {
                    process.Dispose();
                }
                catch
                {
                }

                onProcessExited?.Invoke();
            };
        }

        public static void CleanupProcesses()
        {
            lock (_lock)
            {
                foreach (var proc in _launchedProcesses)
                {
                    try
                    {
                        if (!proc.HasExited)
                        {
                            proc.Kill();
                        }
                        proc.Dispose();
                    }
                    catch
                    {
                    }
                }
                _launchedProcesses.Clear();
            }
        }
    }
}
