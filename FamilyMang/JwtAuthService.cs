using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace FamilyMang
{
    public sealed class JwtAuthService : IDisposable
    {
        private readonly HttpClient _http;
        private readonly string _windowsUser;
        private readonly JavaScriptSerializer _json = new JavaScriptSerializer();
        private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);

        private string _cachedToken;
        private DateTime _tokenExpiry = DateTime.MinValue;

        public string WindowsUser => _windowsUser;

        public JwtAuthService(string baseUrl, int timeoutSeconds = 120)
        {
            _windowsUser = Environment.UserName;

            _http = new HttpClient
            {
                BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/"),
                Timeout = TimeSpan.FromSeconds(timeoutSeconds)
            };
        }

        public async Task<string> GetTokenAsync()
        {
            if (!string.IsNullOrEmpty(_cachedToken) && DateTime.UtcNow < _tokenExpiry)
                return _cachedToken;

            await _lock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!string.IsNullOrEmpty(_cachedToken) && DateTime.UtcNow < _tokenExpiry)
                    return _cachedToken;

                var body = _json.Serialize(new Dictionary<string, object>
                {
                    { "windowsUser", _windowsUser }
                });

                using (var content = new StringContent(body, Encoding.UTF8, "application/json"))
                using (var response = await _http.PostAsync("api/v1/family/auth", content).ConfigureAwait(false))
                {
                    var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                        throw new AuthException((int)response.StatusCode, responseBody);

                    var dto = _json.Deserialize<Dictionary<string, object>>(responseBody);
                    if (dto == null || !dto.ContainsKey("accessToken"))
                        throw new AuthException(0, "Сервер вернул пустой токен");

                    _cachedToken = dto["accessToken"]?.ToString();
                    if (string.IsNullOrEmpty(_cachedToken))
                        throw new AuthException(0, "Сервер вернул пустой токен");

                    var expiresIn = dto.ContainsKey("expiresIn")
                        ? Convert.ToInt32(dto["expiresIn"])
                        : 28800;
                    var buffer = expiresIn > 60 ? expiresIn - 60 : expiresIn;
                    _tokenExpiry = DateTime.UtcNow.AddSeconds(buffer);

                    return _cachedToken;
                }
            }
            finally
            {
                _lock.Release();
            }
        }

        public void Invalidate()
        {
            _cachedToken = null;
            _tokenExpiry = DateTime.MinValue;
        }

        public void Dispose() => _http?.Dispose();
    }

    public sealed class AuthException : Exception
    {
        public int StatusCode { get; }

        public AuthException(int statusCode, string message)
            : base($"Auth error {statusCode}: {message}")
        {
            StatusCode = statusCode;
        }
    }
}
