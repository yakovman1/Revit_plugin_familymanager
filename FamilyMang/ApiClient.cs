using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
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
        private readonly JwtAuthService _auth;

        public string BaseUrl
        {
            get => _http.BaseAddress?.ToString().TrimEnd('/') ?? "";
            set => _http.BaseAddress = new Uri(value.TrimEnd('/') + "/");
        }

        public ApiClient(JwtAuthService auth)
        {
            _auth = auth ?? throw new ArgumentNullException(nameof(auth));
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
            _json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        }

        private string Url(string path) =>
            path.StartsWith("/") ? path.TrimStart('/') : path;

        private StringContent JsonBody(object obj) =>
            new StringContent(_json.Serialize(obj), Encoding.UTF8, "application/json");

        private async Task<HttpResponseMessage> SendAuthedAsync(
            Func<HttpRequestMessage> createRequest)
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var token = await _auth.GetTokenAsync().ConfigureAwait(false);
                var request = createRequest();
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

                var response = await _http.SendAsync(request).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
                {
                    _auth.Invalidate();
                    response.Dispose();
                    continue;
                }

                return response;
            }

            throw new AuthException(401, "Unauthorized after token refresh");
        }

        private void ClearAuth()
        {
            _http.DefaultRequestHeaders.Authorization = null;
        }

        #region Download (catalog)

        public async Task<ListFamiliesResponseDto> GetFamiliesAsync(
            int limit = 20,
            int offset = 0,
            bool? isPrimary = null,
            string parentId = null)
        {
            var query = $"/families?limit={limit}&offset={offset}";
            if (isPrimary.HasValue)
                query += isPrimary.Value ? "&is_primary=true" : "&is_primary=false";
            if (!string.IsNullOrWhiteSpace(parentId))
                query += "&parent_id=" + Uri.EscapeDataString(parentId);

            using (var resp = await SendAuthedAsync(() =>
                       new HttpRequestMessage(HttpMethod.Get, Url(query)))
                   .ConfigureAwait(false))
            {
                resp.EnsureSuccessStatusCode();
                var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                return _json.Deserialize<ListFamiliesResponseDto>(body);
            }
        }

        public async Task<List<FamilySummaryDto>> GetAllFamiliesAsync(int pageSize = 100)
        {
            var all = new List<FamilySummaryDto>();
            var offset = 0;
            int total;

            do
            {
                var page = await GetFamiliesAsync(pageSize, offset).ConfigureAwait(false);
                if (page?.items != null)
                    all.AddRange(page.items);
                total = page?.total ?? 0;
                offset += pageSize;
            }
            while (offset < total);

            return all;
        }

        public async Task<string> GetDownloadUrlAsync(string familyId)
        {
            using (var resp = await SendAuthedAsync(() =>
                       new HttpRequestMessage(HttpMethod.Get,
                           Url($"/families/{familyId}/download-url")))
                   .ConfigureAwait(false))
            {
                resp.EnsureSuccessStatusCode();
                var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                return _json.Deserialize<DownloadUrlResponseDto>(body).presigned_get_url;
            }
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
                ClearAuth();
            }
        }

        #endregion

        #region Upload (family → backend)

        public async Task<InitUploadResponseDto> InitUploadAsync(
            string filename,
            long sizeBytes,
            string sha256,
            string familyName,
            string category,
            bool isPrimary,
            string parentFamilyId = null)
        {
            using (var resp = await SendAuthedAsync(() =>
                   {
                       var body = new Dictionary<string, object>
                       {
                           { "original_filename", filename },
                           { "size_bytes", sizeBytes },
                           { "sha256", sha256 },
                           { "family_name", familyName ?? "" },
                           { "category", category ?? "" },
                           { "is_primary", isPrimary }
                       };
                       if (!string.IsNullOrWhiteSpace(parentFamilyId))
                           body["parent_family_id"] = parentFamilyId;

                       var request = new HttpRequestMessage(HttpMethod.Post, Url("/families/init-upload"))
                       {
                           Content = JsonBody(body)
                       };
                       return request;
                   }).ConfigureAwait(false))
            {
                await EnsureSuccessOrThrowAsync(resp, "init-upload").ConfigureAwait(false);
                var responseBody = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                return _json.Deserialize<InitUploadResponseDto>(responseBody);
            }
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
                ClearAuth();
            }
        }

        public async Task PostMetadataAsync(string familyId, Dictionary<string, object> metadata)
        {
            using (var resp = await SendAuthedAsync(() =>
                   {
                       var request = new HttpRequestMessage(HttpMethod.Post, Url($"/families/{familyId}/metadata"))
                       {
                           Content = JsonBody(new { metadata })
                       };
                       return request;
                   }).ConfigureAwait(false))
            {
                resp.EnsureSuccessStatusCode();
            }
        }

        public async Task CompleteUploadAsync(string familyId, string etag = null)
        {
            using (var resp = await SendAuthedAsync(() =>
                   {
                       var request = new HttpRequestMessage(HttpMethod.Post, Url($"/families/{familyId}/complete"))
                       {
                           Content = JsonBody(new { etag })
                       };
                       return request;
                   }).ConfigureAwait(false))
            {
                resp.EnsureSuccessStatusCode();
            }
        }

        #endregion

        private static async Task EnsureSuccessOrThrowAsync(HttpResponseMessage resp, string operation)
        {
            if (resp.IsSuccessStatusCode)
                return;

            var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            var detail = string.IsNullOrWhiteSpace(body)
                ? resp.ReasonPhrase
                : body.Length > 500 ? body.Substring(0, 500) + "…" : body;

            throw new HttpRequestException(
                $"{operation}: {(int)resp.StatusCode} {resp.ReasonPhrase}. {detail}");
        }

        public void Dispose()
        {
            _http?.Dispose();
            _auth?.Dispose();
        }
    }
}
