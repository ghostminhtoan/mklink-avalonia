using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

namespace MkLink.Services
{
    public static class AutoUpdateService
    {
        private const string RepoOwner = "ghostminhtoan";
        private const string RepoName = "mklink-avalonia";
        private const string BetaTag = "beta";
        private const string ExeFileName = "MkLink-GMTPC.exe";
        private const string DirectDownloadUrl = $"https://github.com/{RepoOwner}/{RepoName}/releases/download/{BetaTag}/{ExeFileName}";

        public static async Task<(bool success, string message)> CheckAndUpdateBetaAsync(Action<string>? statusCallback = null)
        {
            try
            {
                statusCallback?.Invoke("Đang kiểm tra bản phát hành Beta trên GitHub...");

                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromMinutes(5);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("MkLink-GMTPC-Updater/1.0");

                // 1. Kiểm tra thông tin release trên GitHub
                string apiUrl = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/tags/{BetaTag}";
                string downloadUrl = DirectDownloadUrl;
                long remoteSize = 0;

                try
                {
                    using var apiResp = await client.GetAsync(apiUrl);
                    if (apiResp.IsSuccessStatusCode)
                    {
                        string jsonStr = await apiResp.Content.ReadAsStringAsync();
                        using var doc = JsonDocument.Parse(jsonStr);
                        if (doc.RootElement.TryGetProperty("assets", out var assetsElem) && assetsElem.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var item in assetsElem.EnumerateArray())
                            {
                                string name = item.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                                if (string.Equals(name, ExeFileName, StringComparison.OrdinalIgnoreCase))
                                {
                                    if (item.TryGetProperty("browser_download_url", out var dl))
                                    {
                                        downloadUrl = dl.GetString() ?? DirectDownloadUrl;
                                    }
                                    if (item.TryGetProperty("size", out var sz))
                                    {
                                        remoteSize = sz.GetInt64();
                                    }
                                    break;
                                }
                            }
                        }
                    }
                }
                catch
                {
                    // Fallback dùng DirectDownloadUrl nếu không gọi được API
                }

                // 2. Định vị file exe hiện tại
                string currentExePath = Process.GetCurrentProcess().MainModule?.FileName ?? "";
                if (string.IsNullOrWhiteSpace(currentExePath) || !File.Exists(currentExePath))
                {
                    // Nếu đang chạy qua dll trong .portable/runtimes, tìm exe ở root thư mục cha
                    string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                    currentExePath = Path.Combine(baseDir, ExeFileName);
                    if (!File.Exists(currentExePath))
                    {
                        string parentDir = Path.GetFullPath(Path.Combine(baseDir, "..", ".."));
                        string parentExe = Path.Combine(parentDir, ExeFileName);
                        if (File.Exists(parentExe))
                        {
                            currentExePath = parentExe;
                        }
                    }
                }

                string targetDir = Path.GetDirectoryName(currentExePath) ?? AppDomain.CurrentDomain.BaseDirectory;
                string tempNewExe = Path.Combine(targetDir, ExeFileName + ".new");

                statusCallback?.Invoke($"Đang tải bản Beta mới từ GitHub ({downloadUrl})...");

                // 3. Tải file mới
                using (var downloadResp = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
                {
                    if (!downloadResp.IsSuccessStatusCode)
                    {
                        return (false, $"Tải file thất bại. Mã HTTP: {downloadResp.StatusCode}");
                    }

                    using (var fs = new FileStream(tempNewExe, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        await downloadResp.Content.CopyToAsync(fs);
                    }
                }

                var fi = new FileInfo(tempNewExe);
                if (!fi.Exists || fi.Length < 1024)
                {
                    try { File.Delete(tempNewExe); } catch { }
                    return (false, "File tải về không hợp lệ hoặc quá nhỏ.");
                }

                statusCallback?.Invoke("Đã tải xong. Đang kích hoạt tiến trình cập nhật và khởi động lại...");

                // 4. Tạo script cập nhật độc lập để tránh file lock
                string updateScript = Path.Combine(targetDir, "update_beta_restart.cmd");
                string backupExe = Path.Combine(targetDir, "zbackup_" + ExeFileName);

                string cmdContent = $@"@echo off
chcp 65001 >nul
timeout /t 1 /nobreak >nul
if exist ""{backupExe}"" del /f /q ""{backupExe}""
if exist ""{currentExePath}"" ren ""{currentExePath}"" ""{"zbackup_" + ExeFileName}""
move /y ""{tempNewExe}"" ""{currentExePath}""
start """" ""{currentExePath}""
del ""%~f0""
";
                File.WriteAllText(updateScript, cmdContent, System.Text.Encoding.UTF8);

                // 5. Khởi chạy script và đóng app
                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c \"{updateScript}\"",
                    WorkingDirectory = targetDir,
                    CreateNoWindow = true,
                    UseShellExecute = false
                };

                Process.Start(psi);
                Environment.Exit(0);

                return (true, "Đang khởi động lại ứng dụng...");
            }
            catch (Exception ex)
            {
                return (false, "Lỗi cập nhật: " + ex.Message);
            }
        }
    }
}
