using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace MkLink.Launcher
{
    public static class RuntimesDownloader
    {
        private const string AssetsDownloadUrl = "https://github.com/ghostminhtoan/mklink-avalonia/releases/download/assets/runtimes.zip";
        private const int ConnectionsCount = 16;

        public static bool DownloadAndExtractRuntimes(string targetRuntimesDir)
        {
            try
            {
                Console.WriteLine("=========================================================");
                Console.WriteLine("  MkLink-GMTPC - Trình Tải Tự Động Runtimes Độc Lập");
                Console.WriteLine("  Nguồn: https://github.com/ghostminhtoan/mklink-avalonia/releases/tag/assets");
                Console.WriteLine($"  Cơ chế: Tải đa luồng tăng tốc {ConnectionsCount} kết nối song song");
                Console.WriteLine("=========================================================\n");

                string tempZip = Path.Combine(Path.GetTempPath(), "mklink_runtimes_" + Guid.NewGuid().ToString("N") + ".zip");

                bool success = DownloadWith16ConnectionsAsync(AssetsDownloadUrl, tempZip).GetAwaiter().GetResult();
                if (!success || !File.Exists(tempZip) || new FileInfo(tempZip).Length == 0)
                {
                    Console.WriteLine("\n[LỖI] Tải runtimes.zip thất bại.");
                    return false;
                }

                Console.WriteLine("\n[INFO] Đang giải nén runtimes vào: " + targetRuntimesDir);
                if (!Directory.Exists(targetRuntimesDir))
                {
                    Directory.CreateDirectory(targetRuntimesDir);
                }

                ZipFile.ExtractToDirectory(tempZip, targetRuntimesDir, true);

                try
                {
                    File.Delete(tempZip);
                }
                catch
                {
                }

                Console.WriteLine("[THÀNH CÔNG] Đã chuẩn bị xong gói Runtimes đầy đủ!\n");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine("[LỖI] Xảy ra ngoại lệ khi tải/giải nén runtimes: " + ex.Message);
                return false;
            }
        }

        private static async Task<bool> DownloadWith16ConnectionsAsync(string url, string outputFilePath)
        {
            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = true,
                AutomaticDecompression = DecompressionMethods.None
            };

            using var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromMinutes(10)
            };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("MkLink-GMTPC-Bootstrap/1.0");

            // 1. Lấy thông tin kích thước file
            long totalBytes = -1;
            try
            {
                using var headReq = new HttpRequestMessage(HttpMethod.Head, url);
                using var headResp = await client.SendAsync(headReq, HttpCompletionOption.ResponseHeadersRead);
                if (headResp.IsSuccessStatusCode && headResp.Content.Headers.ContentLength.HasValue)
                {
                    totalBytes = headResp.Content.Headers.ContentLength.Value;
                }
            }
            catch
            {
            }

            if (totalBytes <= 0)
            {
                // Fallback thử GET với Range 0-0 để bắt redirect Content-Length của GitHub S3
                try
                {
                    using var rangeReq = new HttpRequestMessage(HttpMethod.Get, url);
                    rangeReq.Headers.Range = new RangeHeaderValue(0, 0);
                    using var rangeResp = await client.SendAsync(rangeReq, HttpCompletionOption.ResponseHeadersRead);
                    if (rangeResp.Content.Headers.ContentRange?.Length.HasValue == true)
                    {
                        totalBytes = rangeResp.Content.Headers.ContentRange.Length.Value;
                    }
                    else if (rangeResp.Content.Headers.ContentLength.HasValue)
                    {
                        totalBytes = rangeResp.Content.Headers.ContentLength.Value;
                    }
                }
                catch
                {
                }
            }

            // Nếu không lấy được Content-Length hoặc file quá nhỏ (<1MB), tải đơn luồng
            if (totalBytes <= 1024 * 1024)
            {
                Console.WriteLine("[INFO] Tải thông thường (Server không hỗ trợ Content-Length hoặc file nhỏ)...");
                using var stream = await client.GetStreamAsync(url);
                using var fileStream = File.Create(outputFilePath);
                await stream.CopyToAsync(fileStream);
                return true;
            }

            double totalMB = totalBytes / (1024.0 * 1024.0);
            Console.WriteLine($"[INFO] Tổng dung lượng: {totalMB:F2} MB. Đang kích hoạt 16 kết nối song song...");

            long chunkSize = totalBytes / ConnectionsCount;
            var tasks = new List<Task<string>>();
            long[] downloadedBytes = new long[ConnectionsCount];

            for (int i = 0; i < ConnectionsCount; i++)
            {
                int chunkIndex = i;
                long start = chunkIndex * chunkSize;
                long end = (chunkIndex == ConnectionsCount - 1) ? totalBytes - 1 : (start + chunkSize - 1);
                string chunkFile = Path.Combine(Path.GetTempPath(), $"mklink_part_{Guid.NewGuid():N}_{chunkIndex}.tmp");

                tasks.Add(Task.Run(async () =>
                {
                    using var chunkClient = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true });
                    chunkClient.Timeout = TimeSpan.FromMinutes(10);
                    chunkClient.DefaultRequestHeaders.UserAgent.ParseAdd("MkLink-GMTPC-Bootstrap/1.0");

                    using var req = new HttpRequestMessage(HttpMethod.Get, url);
                    req.Headers.Range = new RangeHeaderValue(start, end);

                    using var resp = await chunkClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
                    resp.EnsureSuccessStatusCode();

                    using var inStream = await resp.Content.ReadAsStreamAsync();
                    using var outStream = File.Create(chunkFile);

                    byte[] buffer = new byte[64 * 1024];
                    int bytesRead;
                    while ((bytesRead = await inStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        await outStream.WriteAsync(buffer, 0, bytesRead);
                        Interlocked.Add(ref downloadedBytes[chunkIndex], bytesRead);
                    }

                    return chunkFile;
                }));
            }

            // Hiển thị tiến độ real-time
            var progressTask = Task.Run(async () =>
            {
                DateTime startTime = DateTime.Now;
                while (!Task.WhenAll(tasks).IsCompleted)
                {
                    long totalDownloaded = 0;
                    for (int i = 0; i < ConnectionsCount; i++)
                    {
                        totalDownloaded += Interlocked.Read(ref downloadedBytes[i]);
                    }

                    double curMB = totalDownloaded / (1024.0 * 1024.0);
                    double pct = (totalDownloaded * 100.0) / totalBytes;
                    double elapsed = (DateTime.Now - startTime).TotalSeconds;
                    double speedMbps = elapsed > 0 ? (totalDownloaded * 8.0) / (elapsed * 1_000_000.0) : 0;

                    Console.Write($"\r[TIẾN ĐỘ] {curMB:F1}/{totalMB:F1} MB ({pct:F1}%) | Tốc độ: {speedMbps:F1} Mbps | Đang tải {ConnectionsCount} luồng...   ");
                    await Task.Delay(500);
                }
            });

            string[] chunkFiles = await Task.WhenAll(tasks);
            await progressTask;
            Console.WriteLine("\n[INFO] Đã tải xong 16 phần, đang ghép thành file zip hoàn chỉnh...");

            // Ghép 16 files lại
            using (var finalStream = File.Create(outputFilePath))
            {
                foreach (string chunkPath in chunkFiles)
                {
                    using (var partStream = File.OpenRead(chunkPath))
                    {
                        await partStream.CopyToAsync(finalStream);
                    }
                    try
                    {
                        File.Delete(chunkPath);
                    }
                    catch
                    {
                    }
                }
            }

            return true;
        }
    }
}
