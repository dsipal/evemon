using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using EVEMon.Common.Net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EVEMon.Common.Esi
{
    /// <summary>
    /// A minimal client for the ESI JSON API.
    /// </summary>
    /// <remarks>
    /// Failed requests throw an <see cref="HttpWebClientServiceException"/> carrying the HTTP status code
    /// and the error text returned by ESI.
    /// </remarks>
    internal static class EsiClient
    {
        private const string Datasource = "tranquility";

        /// <summary>
        /// Sends a GET request to ESI.
        /// </summary>
        /// <typeparam name="T">The type of the JSON response.</typeparam>
        /// <param name="path">The route, e.g. "/characters/123/skills/".</param>
        /// <param name="accessToken">The access token for authenticated routes, or null.</param>
        /// <param name="page">The page to request for paginated routes, or 0.</param>
        /// <returns></returns>
        internal static Task<EsiResponse<T>> GetAsync<T>(string path, string accessToken = null, int page = 0)
            => SendAsync<T>(HttpMethod.Get, path, accessToken, null, page);

        /// <summary>
        /// Gets every page of a paginated ESI route (X-Pages header).
        /// </summary>
        /// <typeparam name="T">The type of the items.</typeparam>
        /// <param name="path">The route.</param>
        /// <param name="accessToken">The access token for authenticated routes, or null.</param>
        /// <returns>All the items, with the latest expiry of the pages.</returns>
        internal static async Task<EsiResponse<List<T>>> GetAllPagesAsync<T>(string path, string accessToken = null)
        {
            EsiResponse<List<T>> first = await GetAsync<List<T>>(path, accessToken, 1).ConfigureAwait(false);
            List<T> items = new List<T>(first.Result ?? new List<T>());
            DateTime expires = first.Expires;

            for (int page = 2; page <= first.Pages; page++)
            {
                EsiResponse<List<T>> response = await GetAsync<List<T>>(path, accessToken, page).ConfigureAwait(false);
                if (response.Result != null)
                    items.AddRange(response.Result);
                if (response.Expires > expires)
                    expires = response.Expires;
            }

            return new EsiResponse<List<T>>(items, expires, first.ServerTime, first.Pages);
        }

        /// <summary>
        /// Sends a POST request with a JSON body to ESI.
        /// </summary>
        /// <typeparam name="T">The type of the JSON response.</typeparam>
        /// <param name="path">The route, e.g. "/universe/names/".</param>
        /// <param name="body">The object to serialize as the request body.</param>
        /// <param name="accessToken">The access token for authenticated routes, or null.</param>
        /// <returns></returns>
        internal static Task<EsiResponse<T>> PostAsync<T>(string path, object body, string accessToken = null)
            => SendAsync<T>(HttpMethod.Post, path, accessToken, JsonConvert.SerializeObject(body), 0);

        /// <summary>
        /// Creates an HTTP client honouring EVEMon's proxy and timeout settings.
        /// </summary>
        /// <returns></returns>
        internal static HttpClient CreateHttpClient()
        {
            HttpClientHandler handler = new HttpClientHandler
            {
                Proxy = HttpClientServiceRequest.GetWebProxy(),
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };

            if (EveMonClient.IsDebugBuild)
                handler.ServerCertificateCustomValidationCallback = HttpWebClientService.DummyCertificateValidationCallback;

            HttpClient client = new HttpClient(handler, true)
            {
                Timeout = TimeSpan.FromSeconds(Math.Max(Settings.Updates.HttpTimeout, 10))
            };
            client.DefaultRequestHeaders.UserAgent.ParseAdd(HttpWebClientServiceState.UserAgent);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            return client;
        }

        private static async Task<EsiResponse<T>> SendAsync<T>(HttpMethod method, string path, string accessToken,
            string jsonBody, int page)
        {
            string query = (path.Contains("?") ? "&" : "?") + $"datasource={Datasource}" +
                           (page > 0 ? $"&page={page}" : String.Empty);
            Uri url = new Uri(EsiConfiguration.EsiBase + path + query);

            using (HttpClient client = CreateHttpClient())
            using (HttpRequestMessage request = new HttpRequestMessage(method, url))
            {
                if (!String.IsNullOrEmpty(accessToken))
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

                if (jsonBody != null)
                    request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

                HttpResponseMessage response;
                string content;
                try
                {
                    response = await client.SendAsync(request).ConfigureAwait(false);
                    content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                }
                catch (TaskCanceledException ex)
                {
                    throw HttpWebClientServiceException.EsiException(url, HttpStatusCode.RequestTimeout,
                        $"The request to {url.Host} timed out.", ex);
                }
                catch (HttpRequestException ex)
                {
                    throw HttpWebClientServiceException.EsiException(url, null,
                        $"The request to {url.Host} failed: {ex.Message}", ex);
                }

                using (response)
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        throw HttpWebClientServiceException.EsiException(url, response.StatusCode,
                            $"ESI returned {(int)response.StatusCode} ({response.ReasonPhrase}) for {path}: " +
                            GetErrorText(content));
                    }

                    T result;
                    try
                    {
                        result = JsonConvert.DeserializeObject<T>(content);
                    }
                    catch (JsonException ex)
                    {
                        throw HttpWebClientServiceException.EsiException(url, response.StatusCode,
                            $"The response of {path} could not be read: {ex.Message}", ex);
                    }

                    return new EsiResponse<T>(result,
                        response.Content.Headers.Expires?.UtcDateTime ?? DateTime.UtcNow.AddMinutes(5),
                        response.Headers.Date?.UtcDateTime ?? DateTime.UtcNow,
                        GetPages(response));
                }
            }
        }

        /// <summary>
        /// Gets the error text from an ESI error response ({"error": "..."}).
        /// </summary>
        private static string GetErrorText(string content)
        {
            if (String.IsNullOrWhiteSpace(content))
                return "no details.";

            try
            {
                string error = JObject.Parse(content)["error"]?.ToString();
                if (!String.IsNullOrEmpty(error))
                    return error;
            }
            catch (JsonException)
            {
            }

            return content.Length > 200 ? content.Substring(0, 200) : content;
        }

        private static int GetPages(HttpResponseMessage response)
        {
            IEnumerable<string> values;
            int pages;
            return response.Headers.TryGetValues("X-Pages", out values) &&
                   Int32.TryParse(values.FirstOrDefault(), NumberStyles.Integer, CultureInfo.InvariantCulture, out pages)
                ? pages
                : 1;
        }
    }
}
