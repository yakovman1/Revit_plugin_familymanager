using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace FamilyMang
{
    public sealed class ApiClient : IDisposable
    {
        private readonly HttpClient _http;
        private readonly JavaScriptSerializer _json;

        public string BaseUrl { get; set; } = "http://localhost:8000";
        public string Token { get; set; } = "";

        public ApiClient()
        {
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
            _json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        }

        private string Url(string path) => $"{BaseUrl.TrimEnd('/')}{path}";

        private void ApplyAuth()
        {
            _http.DefaultRequestHeaders.Authorization =
                !string.IsNullOrWhiteSpace(Token)
                    ? new AuthenticationHeaderValue("Bearer", Token)
                    : null;
        }

        private void ClearAuth()
        {
            _http.DefaultRequestHeaders.Authorization = null;
        }

        private StringContent JsonBody(object obj) =>
            new StringContent(_json.Serialize(obj), Encoding.UTF8, "application/json");

        #region Download (catalog)

        public async Task<ListFamiliesResponseDto> GetFamiliesAsync(
            string projectId, int limit = 20, int offset = 0)
        {
            ApplyAuth();
            var resp = await _http.GetAsync(
                Url($"/projects/{projectId}/families?limit={limit}&offset={offset}"))
                .ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            return _json.Deserialize<ListFamiliesResponseDto>(body);
        }

        public async Task<string> GetDownloadUrlAsync(string familyId)
        {
            ApplyAuth();
            var resp = await _http.GetAsync(Url($"/families/{familyId}/download-url"))
                .ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            return _json.Deserialize<DownloadUrlResponseDto>(body).presigned_get_url;
        }

        public async Task<string> DownloadFileAsync(string presignedUrl, string fileName)
        {
            var dir = Path.Combine(Path.GetTempPath(), "FamilyMang");
            Directory.CreateDirectory(dir);
            var filePath = Path.Combine(dir, fileName);

            ClearAuth();
            try
            {
                using (var resp = await _http.GetAsync(presignedUrl).ConfigureAwait(false))
                {
                    resp.EnsureSuccessStatusCode();
                    using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        await resp.Content.CopyToAsync(fs).ConfigureAwait(false);
                    }
                }
                return filePath;
            }
            finally
            {
                ApplyAuth();
            }
        }

        #endregion

        #region Upload (family → backend)

        public async Task<InitUploadResponseDto> InitUploadAsync(
            string projectId, string filename, long sizeBytes, string sha256)
        {
            ApplyAuth();
            var resp = await _http.PostAsync(
                Url("/families/init-upload"),
                JsonBody(new
                {
                    project_id = projectId,
                    original_filename = filename,
                    size_bytes = sizeBytes,
                    sha256 = sha256
                })).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            return _json.Deserialize<InitUploadResponseDto>(body);
        }

        public async Task<string> UploadToS3Async(string presignedPutUrl, string filePath)
        {
            ClearAuth();
            try
            {
                using (var fs = File.OpenRead(filePath))
                {
                    var content = new StreamContent(fs);
                    content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");

                    var request = new HttpRequestMessage(HttpMethod.Put, presignedPutUrl)
                    {
                        Content = content
                    };
                    var resp = await _http.SendAsync(request).ConfigureAwait(false);
                    resp.EnsureSuccessStatusCode();

                    if (resp.Headers.TryGetValues("ETag", out var etags))
                        return etags.FirstOrDefault()?.Trim('"');
                    return null;
                }
            }
            finally
            {
                ApplyAuth();
            }
        }

        public async Task PostMetadataAsync(
            string familyId, Dictionary<string, object> metadata)
        {
            ApplyAuth();
            var resp = await _http.PostAsync(
                Url($"/families/{familyId}/metadata"),
                JsonBody(new { metadata }))
                .ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
        }

        public async Task CompleteUploadAsync(string familyId, string etag = null)
        {
            ApplyAuth();
            var resp = await _http.PostAsync(
                Url($"/families/{familyId}/complete"),
                JsonBody(new { etag }))
                .ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
        }

        #endregion

        public void Dispose() => _http?.Dispose();
    }
}
