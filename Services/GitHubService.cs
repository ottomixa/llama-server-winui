using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace llama_server_winui.Services
{
    public class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; set; } = string.Empty;

        [JsonPropertyName("html_url")]
        public string HtmlUrl { get; set; } = string.Empty;

        [JsonPropertyName("assets")]
        public List<GitHubAsset> Assets { get; set; } = new();
    }

    public class GitHubAsset
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("browser_download_url")]
        public string BrowserDownloadUrl { get; set; } = string.Empty;
    }

    public class GitHubService
    {
        private const string RepoOwner = "ggml-org";
        private const string RepoName = "llama.cpp";
        private readonly HttpClient _httpClient;

        public GitHubService()
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("LlamaServerWinUI"); // GitHub API requires User-Agent
        }

        public async Task<GitHubRelease?> GetLatestReleaseAsync()
        {
            try
            {
                var url = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";
                var release = await _httpClient.GetFromJsonAsync<GitHubRelease>(url);
                return release;
            }
            catch (Exception)
            {
                // Fallback or error handling logic (caller handles null)
                return null;
            }
        }
    }
}
