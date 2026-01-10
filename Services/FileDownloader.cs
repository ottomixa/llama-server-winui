using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace llama_server_winui.Services
{
    public record DownloadProgressInfo(long BytesDownloaded, long TotalBytes, double SpeedMBps);

    public interface IFileDownloader
    {
        Task DownloadFileAsync(string url, string destinationPath, IProgress<DownloadProgressInfo>? progress, CancellationToken token);
    }

    public class FileDownloader : IFileDownloader
    {
        private readonly HttpClient _httpClient;

        public FileDownloader(HttpClient? httpClient = null)
        {
            _httpClient = httpClient ?? new HttpClient();
            if (httpClient == null)
            {
                _httpClient.DefaultRequestHeaders.Add("User-Agent", "LlamaServerWinUI/1.0");
                _httpClient.Timeout = TimeSpan.FromMinutes(30);
            }
        }

        public async Task DownloadFileAsync(string url, string destinationPath, IProgress<DownloadProgressInfo>? progress, CancellationToken token)
        {
            using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1;
            using var contentStream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            
            var dir = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

            var buffer = new byte[81920];
            var totalRead = 0L;
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var lastReport = DateTime.UtcNow;

            int bytesRead;
            while ((bytesRead = await contentStream.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), token).ConfigureAwait(false);
                totalRead += bytesRead;

                var now = DateTime.UtcNow;
                if ((now - lastReport).TotalMilliseconds > 100)
                {
                    lastReport = now;
                    var elapsed = stopwatch.Elapsed.TotalSeconds;
                    var speed = elapsed > 0 ? (totalRead / 1048576.0) / elapsed : 0;
                    
                    progress?.Report(new DownloadProgressInfo(totalRead, totalBytes, speed));
                }
            }
            
            // Final report
            progress?.Report(new DownloadProgressInfo(totalRead, totalBytes, stopwatch.Elapsed.TotalSeconds > 0 ? (totalRead / 1048576.0) / stopwatch.Elapsed.TotalSeconds : 0));
        }
    }
}
