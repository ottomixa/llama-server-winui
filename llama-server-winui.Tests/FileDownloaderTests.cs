using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using llama_server_winui.Services;

namespace llama_server_winui.Tests
{
    [TestClass]
    public class FileDownloaderTests
    {
        private class MockHttpMessageHandler : HttpMessageHandler
        {
            private readonly byte[] _content;

            public MockHttpMessageHandler(byte[] content)
            {
                _content = content;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(_content)
                };
                response.Content.Headers.ContentLength = _content.Length;
                return Task.FromResult(response);
            }
        }

        [TestMethod]
        public async Task DownloadFileAsync_DownloadsFileAndReportsProgress()
        {
            // Arrange
            var data = new byte[1024 * 1024]; // 1MB dummy data
            new Random().NextBytes(data);
            
            var handler = new MockHttpMessageHandler(data);
            var client = new HttpClient(handler);
            var downloader = new FileDownloader(client);
            
            string tempFile = Path.GetTempFileName();
            var progress = new Progress<DownloadProgressInfo>();
            
            bool progressReported = false;
            progress.ProgressChanged += (s, e) => 
            {
                progressReported = true;
                Assert.IsTrue(e.BytesDownloaded > 0);
            };

            // Act
            await downloader.DownloadFileAsync("http://example.com/test.zip", tempFile, progress, CancellationToken.None);

            // Assert
            Assert.IsTrue(File.Exists(tempFile));
            var downloadedData = await File.ReadAllBytesAsync(tempFile);
            Assert.AreEqual(data.Length, downloadedData.Length);
            Assert.IsTrue(progressReported, "Progress should have been reported");

            // Cleanup
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
}
