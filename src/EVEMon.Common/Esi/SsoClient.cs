using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using EVEMon.Common.Helpers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EVEMon.Common.Esi
{
    /// <summary>
    /// EVE SSO (OAuth 2.0) client for a desktop application: authorization code flow with PKCE,
    /// with the login result received on a local HTTP listener.
    /// </summary>
    /// <remarks>
    /// See https://docs.esi.evetech.net/docs/sso/native_sso_flow.html
    /// </remarks>
    public static class SsoClient
    {
        private const string AuthorizePath = "/v2/oauth/authorize";
        private const string TokenPath = "/v2/oauth/token";
        private const string JwksPath = "/oauth/jwks";

        private static readonly string[] s_validIssuers = { "login.eveonline.com", "https://login.eveonline.com" };
        private static readonly SemaphoreSlim s_jwksLock = new SemaphoreSlim(1, 1);
        private static JArray s_signingKeys;

        /// <summary>
        /// Asks the user to log in with EVE SSO in their browser and authorize a character.
        /// </summary>
        /// <param name="cancellationToken">Cancels the wait for the browser login.</param>
        /// <returns>The tokens of the authorized character.</returns>
        /// <exception cref="SsoException">The authorization failed.</exception>
        /// <exception cref="OperationCanceledException">The authorization was canceled.</exception>
        public static async Task<SsoToken> AuthorizeAsync(CancellationToken cancellationToken)
        {
            if (!EsiConfiguration.IsClientIdConfigured)
            {
                throw new SsoException("No EVE SSO Client ID is configured. " +
                                       "See EsiConfiguration.cs for how to register the application.");
            }

            string state = Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
            string codeVerifier = Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
            string codeChallenge = Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier)));

            string authorizeUrl = EsiConfiguration.SsoBase + AuthorizePath +
                                  "?response_type=code" +
                                  $"&redirect_uri={Uri.EscapeDataString(EsiConfiguration.CallbackUri.AbsoluteUri)}" +
                                  $"&client_id={Uri.EscapeDataString(EsiConfiguration.ClientId)}" +
                                  $"&scope={Uri.EscapeDataString(String.Join(" ", EsiConfiguration.Scopes))}" +
                                  $"&code_challenge={codeChallenge}" +
                                  "&code_challenge_method=S256" +
                                  $"&state={state}";

            string code;
            using (HttpListener listener = new HttpListener())
            {
                listener.Prefixes.Add($"http://localhost:{EsiConfiguration.CallbackPort}/");
                try
                {
                    listener.Start();
                }
                catch (HttpListenerException ex)
                {
                    throw new SsoException($"EVEMon could not listen on port {EsiConfiguration.CallbackPort} " +
                                           $"for the EVE SSO login: {ex.Message}", ex);
                }

                Util.OpenURL(new Uri(authorizeUrl));

                using (cancellationToken.Register(() => listener.Stop()))
                {
                    code = await WaitForAuthorizationCodeAsync(listener, state, cancellationToken).ConfigureAwait(false);
                }
            }

            return await RequestTokenAsync(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["client_id"] = EsiConfiguration.ClientId,
                ["code_verifier"] = codeVerifier
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// Gets a new access token from a refresh token.
        /// </summary>
        /// <param name="refreshToken">The refresh token.</param>
        /// <returns>The new tokens; EVE SSO may rotate the refresh token, so it must be stored again.</returns>
        /// <exception cref="SsoException">The refresh failed.</exception>
        public static Task<SsoToken> RefreshAsync(string refreshToken)
        {
            if (!EsiConfiguration.IsClientIdConfigured)
                throw new SsoException("No EVE SSO Client ID is configured.");

            return RequestTokenAsync(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
                ["client_id"] = EsiConfiguration.ClientId
            });
        }

        #region Browser callback

        private static async Task<string> WaitForAuthorizationCodeAsync(HttpListener listener, string expectedState,
            CancellationToken cancellationToken)
        {
            while (true)
            {
                HttpListenerContext context;
                try
                {
                    context = await listener.GetContextAsync().ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is HttpListenerException || ex is ObjectDisposedException ||
                                           ex is InvalidOperationException)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    throw new SsoException($"The EVE SSO login could not be received: {ex.Message}", ex);
                }

                // Ignore anything but the callback (e.g. the browser asking for a favicon)
                if (!String.Equals(context.Request.Url?.AbsolutePath.TrimEnd('/'), EsiConfiguration.CallbackUri.AbsolutePath,
                    StringComparison.OrdinalIgnoreCase))
                {
                    context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                    context.Response.Close();
                    continue;
                }

                string state = context.Request.QueryString["state"];
                string code = context.Request.QueryString["code"];
                string error = context.Request.QueryString["error_description"] ?? context.Request.QueryString["error"];

                // Requests that don't belong to our login are answered and ignored
                if (state != expectedState)
                {
                    await RespondAsync(context, false, "This login was not started by EVEMon.").ConfigureAwait(false);
                    continue;
                }

                if (!String.IsNullOrEmpty(error) || String.IsNullOrEmpty(code))
                {
                    await RespondAsync(context, false, error ?? "No authorization code was returned.").ConfigureAwait(false);
                    throw new SsoException($"EVE SSO did not authorize the character: {error ?? "no authorization code"}.");
                }

                await RespondAsync(context, true, "The character has been authorized.").ConfigureAwait(false);
                return code;
            }
        }

        private static async Task RespondAsync(HttpListenerContext context, bool success, string message)
        {
            string html =
                "<!doctype html><html><head><meta charset=\"utf-8\"><title>EVEMon</title></head>" +
                "<body style=\"font-family:Segoe UI,sans-serif;margin:3em\">" +
                $"<h2>{(success ? "EVEMon login complete" : "EVEMon login failed")}</h2>" +
                $"<p>{HttpUtility.HtmlEncode(message)}</p>" +
                "<p>You can close this window and return to EVEMon.</p></body></html>";

            byte[] buffer = Encoding.UTF8.GetBytes(html);
            context.Response.StatusCode = success ? (int)HttpStatusCode.OK : (int)HttpStatusCode.BadRequest;
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.ContentLength64 = buffer.Length;
            try
            {
                await context.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
            }
            finally
            {
                context.Response.Close();
            }
        }

        #endregion


        #region Token endpoint

        private static async Task<SsoToken> RequestTokenAsync(IDictionary<string, string> form)
        {
            string content;
            HttpStatusCode statusCode;
            try
            {
                using (HttpClient client = EsiClient.CreateHttpClient())
                using (FormUrlEncodedContent body = new FormUrlEncodedContent(form))
                using (HttpResponseMessage response = await client.PostAsync(EsiConfiguration.SsoBase + TokenPath, body)
                    .ConfigureAwait(false))
                {
                    statusCode = response.StatusCode;
                    content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException)
            {
                throw new SsoException($"EVE SSO could not be reached: {ex.Message}", ex);
            }

            JObject json;
            try
            {
                json = JObject.Parse(content);
            }
            catch (JsonException ex)
            {
                throw new SsoException($"EVE SSO returned an unreadable response ({(int)statusCode}).", ex);
            }

            if (statusCode != HttpStatusCode.OK)
            {
                string error = json["error"]?.ToString();
                string description = json["error_description"]?.ToString() ?? error ?? statusCode.ToString();

                // invalid_grant: the refresh token was revoked, expired or issued to another client
                throw new SsoException($"EVE SSO refused the request: {description}",
                    error == "invalid_grant" || error == "invalid_token");
            }

            string accessToken = json["access_token"]?.ToString();
            string refreshToken = json["refresh_token"]?.ToString();
            int expiresIn = json["expires_in"]?.Value<int>() ?? 1199;

            if (String.IsNullOrEmpty(accessToken) || String.IsNullOrEmpty(refreshToken))
                throw new SsoException("EVE SSO did not return the expected tokens.");

            JObject claims = await ValidateAccessTokenAsync(accessToken).ConfigureAwait(false);

            // "sub" is "CHARACTER:EVE:<character id>"
            string subject = claims["sub"]?.ToString() ?? String.Empty;
            long characterID;
            if (!subject.StartsWith("CHARACTER:EVE:", StringComparison.Ordinal) ||
                !Int64.TryParse(subject.Substring("CHARACTER:EVE:".Length), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out characterID))
            {
                throw new SsoException("EVE SSO returned a token that is not for a character.");
            }

            return new SsoToken(accessToken, refreshToken, DateTime.UtcNow.AddSeconds(expiresIn - 30), characterID,
                claims["name"]?.ToString() ?? String.Empty,
                claims["owner"]?.ToString() ?? String.Empty,
                GetStringOrArray(claims["scp"]));
        }

        #endregion


        #region Access token (JWT) validation

        /// <summary>
        /// Validates the signature, issuer, audience and expiry of an access token and returns its claims.
        /// </summary>
        private static async Task<JObject> ValidateAccessTokenAsync(string jwt)
        {
            string[] parts = jwt.Split('.');
            if (parts.Length != 3)
                throw new SsoException("EVE SSO returned a malformed access token.");

            JObject header;
            JObject claims;
            byte[] signature;
            try
            {
                header = JObject.Parse(Encoding.UTF8.GetString(Base64UrlDecode(parts[0])));
                claims = JObject.Parse(Encoding.UTF8.GetString(Base64UrlDecode(parts[1])));
                signature = Base64UrlDecode(parts[2]);
            }
            catch (Exception ex) when (ex is FormatException || ex is JsonException)
            {
                throw new SsoException("EVE SSO returned a malformed access token.", ex);
            }

            if (header["alg"]?.ToString() != "RS256")
                throw new SsoException("EVE SSO returned an access token with an unexpected signature algorithm.");

            string keyID = header["kid"]?.ToString();
            JObject key = await GetSigningKeyAsync(keyID, false).ConfigureAwait(false) ??
                          await GetSigningKeyAsync(keyID, true).ConfigureAwait(false);
            if (key == null)
                throw new SsoException("The access token signing key is unknown.");

            using (RSA rsa = RSA.Create())
            {
                rsa.ImportParameters(new RSAParameters
                {
                    Modulus = Base64UrlDecode(key["n"].ToString()),
                    Exponent = Base64UrlDecode(key["e"].ToString())
                });

                byte[] signedData = Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]);
                if (!rsa.VerifyData(signedData, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
                    throw new SsoException("The access token signature is invalid.");
            }

            if (!s_validIssuers.Contains(claims["iss"]?.ToString()))
                throw new SsoException("The access token was not issued by EVE SSO.");

            IReadOnlyCollection<string> audience = GetStringOrArray(claims["aud"]);
            if (!audience.Contains(EsiConfiguration.ClientId) || !audience.Contains("EVE Online"))
                throw new SsoException("The access token was not issued for EVEMon.");

            long expires = claims["exp"]?.Value<long>() ?? 0;
            if (DateTimeOffset.FromUnixTimeSeconds(expires) < DateTimeOffset.UtcNow.AddMinutes(-1))
                throw new SsoException("The access token has expired.");

            return claims;
        }

        /// <summary>
        /// Gets the RSA key with the given ID from EVE SSO's published signing keys.
        /// </summary>
        private static async Task<JObject> GetSigningKeyAsync(string keyID, bool reload)
        {
            await s_jwksLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (s_signingKeys == null || reload)
                {
                    using (HttpClient client = EsiClient.CreateHttpClient())
                    {
                        string content = await client.GetStringAsync(EsiConfiguration.SsoBase + JwksPath).ConfigureAwait(false);
                        s_signingKeys = (JArray)JObject.Parse(content)["keys"];
                    }
                }
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException ||
                                       ex is JsonException || ex is InvalidCastException)
            {
                throw new SsoException($"The EVE SSO signing keys could not be retrieved: {ex.Message}", ex);
            }
            finally
            {
                s_jwksLock.Release();
            }

            return s_signingKeys?.OfType<JObject>()
                .FirstOrDefault(key => key["kty"]?.ToString() == "RSA" && (keyID == null || key["kid"]?.ToString() == keyID));
        }

        #endregion


        #region Helpers

        private static IReadOnlyCollection<string> GetStringOrArray(JToken token)
        {
            if (token == null)
                return Array.Empty<string>();

            return token.Type == JTokenType.Array
                ? token.Values<string>().ToList()
                : new[] { token.ToString() };
        }

        private static string Base64UrlEncode(byte[] data)
            => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        private static byte[] Base64UrlDecode(string text)
        {
            string base64 = text.Replace('-', '+').Replace('_', '/');
            switch (base64.Length % 4)
            {
                case 2:
                    base64 += "==";
                    break;
                case 3:
                    base64 += "=";
                    break;
            }
            return Convert.FromBase64String(base64);
        }

        #endregion
    }
}
