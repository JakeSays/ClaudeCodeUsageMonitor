using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ClaudeUsageMonitor.Models;



namespace ClaudeUsageMonitor.Services;

public class UsageService : IDisposable
{
    private static readonly bool IsMacOS = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

    private static readonly string CredentialsPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", ".credentials.json");

    private const string KeychainServiceName = "Claude Code-credentials";

    private const string TokenRefreshUrl = "https://platform.claude.com/v1/oauth/token";
    private const string UsageUrl = "https://api.anthropic.com/api/oauth/usage";
    private const string ClientId = "9d1c250a-e61b-44d9-88ed-5944d1962f5e";

    // A pooled connection that outlives a network change (sleep/resume, VPN,
    // DHCP renew) is dead but still gets reused, so every request hangs until
    // it times out. Recycling connections on an interval bounds that window.
    private static readonly TimeSpan ConnectionRecycleInterval = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan ConnectionIdleTimeout = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan ConnectionEstablishTimeout = TimeSpan.FromSeconds(10);

    // Well under the two-minute poll interval so a stalled request fails and
    // retries on the next tick instead of straddling it.
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    // Used when a rate-limited response carries no usable Retry-After header.
    private static readonly TimeSpan DefaultRetryAfter = TimeSpan.FromMinutes(5);

    private readonly HttpClient _httpClient = CreateHttpClient();
    private OAuthCredentials? _credentials;

    private static HttpClient CreateHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = ConnectionRecycleInterval,
            PooledConnectionIdleTimeout = ConnectionIdleTimeout,
            ConnectCallback = ConnectAsync
        };

        return new HttpClient(handler)
        {
            Timeout = RequestTimeout
        };
    }

    // The API publishes both A and AAAA records and the resolver hands back the
    // AAAA first, so on a network whose IPv6 route black-holes (common while a
    // VPN is up) the default connector stalls until the request times out.
    // Walk the addresses IPv4-first with a short per-address budget instead.
    private static async ValueTask<Stream> ConnectAsync(
        SocketsHttpConnectionContext context,
        CancellationToken cancellationToken)
    {
        var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken);
        var ordered = addresses
            .OrderBy(address => address.AddressFamily == AddressFamily.InterNetwork ? 0 : 1)
            .ToArray();

        Exception? lastFailure = null;
        foreach (var address in ordered)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
            {
                NoDelay = true
            };

            try
            {
                using var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                attempt.CancelAfter(ConnectionEstablishTimeout);
                await socket.ConnectAsync(address, context.DnsEndPoint.Port, attempt.Token);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                socket.Dispose();
                throw;
            }
            catch (Exception ex)
            {
                socket.Dispose();
                lastFailure = ex;
            }
        }

        throw lastFailure ?? new SocketException((int) SocketError.HostNotFound);
    }

    public void Dispose() => _httpClient.Dispose();

    public static bool CredentialsExist()
    {
        if (!IsMacOS)
        {
            return File.Exists(CredentialsPath);
        }

        try
        {
            return MacKeychain.Read(KeychainServiceName) != null;
        }
        catch
        {
            return false;
        }
    }

    public static string CredentialsLocation => IsMacOS
        ? $"macOS Keychain (service: \"{KeychainServiceName}\")"
        : CredentialsPath;

    public async Task<UsageResponse?> GetUsageAsync()
    {
        await LoadCredentialsAsync();
        if (_credentials?.AccessToken == null)
        {
            return null;
        }

        if (_credentials.ExpiresAt != null)
        {
            var expiresAt = DateTimeOffset.FromUnixTimeMilliseconds(_credentials.ExpiresAt.Value);
            if (expiresAt < DateTimeOffset.UtcNow.AddMinutes(5))
            {
                await RefreshTokenAsync();
            }
        }

        var result = await SendUsageRequestAsync();

        if (result.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            await RefreshTokenAsync();
            result = await SendUsageRequestAsync();
        }

        // A 429 here is a genuine rate limit, not an auth failure — the endpoint
        // answers an invalid token with 401, which the branch above already
        // handles. Refreshing would not lift the limit, and it would rotate the
        // refresh token in the credentials file Claude Code shares.
        if (result.StatusCode == HttpStatusCode.TooManyRequests)
        {
            var retryAfter = result.RetryAfter ?? DefaultRetryAfter;
            if (retryAfter <= TimeSpan.Zero)
            {
                retryAfter = DefaultRetryAfter;
            }
            throw new RateLimitedException(retryAfter);
        }

        if (!result.IsSuccess)
        {
            throw new HttpRequestException(
                $"Usage request failed with status {(int) result.StatusCode} {result.StatusCode}");
        }

        return JsonSerializer.Deserialize(result.Body, UsageJsonContext.Default.UsageResponse);
    }

    private async Task<UsageRequestResult> SendUsageRequestAsync()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, UsageUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _credentials!.AccessToken);
        request.Headers.Add("anthropic-beta", "oauth-2025-04-20");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await _httpClient.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        var retryAfter = response.Headers.RetryAfter?.Delta
                         ?? (response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow);

        return new UsageRequestResult(response.StatusCode, response.IsSuccessStatusCode, body, retryAfter);
    }

    private readonly record struct UsageRequestResult(
        HttpStatusCode StatusCode,
        bool IsSuccess,
        string Body,
        TimeSpan? RetryAfter);

    private async Task LoadCredentialsAsync()
    {
        // Always read fresh so we pick up any refresh Claude Code performs,
        // and never hold a stale refresh_token that the server has rotated.
        string? json;

        if (IsMacOS)
        {
            json = MacKeychain.Read(KeychainServiceName);
            if (json == null)
            {
                throw new InvalidOperationException(
                    $"No credentials found in macOS Keychain (service: \"{KeychainServiceName}\")");
            }
        }
        else
        {
            if (!File.Exists(CredentialsPath))
            {
                throw new FileNotFoundException($"No credentials file found at {CredentialsPath}");
            }

            json = await File.ReadAllTextAsync(CredentialsPath);
        }

        var credFile = JsonSerializer.Deserialize(json, UsageJsonContext.Default.CredentialsFile);
        _credentials = credFile?.ClaudeAiOauth;
    }

    private async Task RefreshTokenAsync()
    {
        if (_credentials?.RefreshToken == null)
        {
            return;
        }

        var scopeStr = _credentials.Scopes != null
            ? string.Join(" ", _credentials.Scopes)
            : "user:inference user:profile";

        var content = new FormUrlEncodedContent(
        [
            new KeyValuePair<string, string>("grant_type", "refresh_token"),
            new KeyValuePair<string, string>("refresh_token", _credentials.RefreshToken),
            new KeyValuePair<string, string>("client_id", ClientId),
            new KeyValuePair<string, string>("scope", scopeStr)
        ]);

        var response = await _httpClient.PostAsync(TokenRefreshUrl, content);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.TryGetProperty("access_token", out var accessToken))
        {
            _credentials.AccessToken = accessToken.GetString();
        }

        if (root.TryGetProperty("expires_in", out var expiresIn))
        {
            _credentials.ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn.GetInt64())
                .ToUnixTimeMilliseconds();
        }

        if (root.TryGetProperty("refresh_token", out var refreshToken))
        {
            _credentials.RefreshToken = refreshToken.GetString();
        }

        // Write updated credentials back
        var credFile = new CredentialsFile
        {
            ClaudeAiOauth = _credentials
        };
        var updatedJson = JsonSerializer.Serialize(credFile, UsageJsonContext.Default.CredentialsFile);

        if (IsMacOS)
        {
            MacKeychain.Write(KeychainServiceName, Environment.UserName, updatedJson);
        }
        else
        {
            await File.WriteAllTextAsync(CredentialsPath, updatedJson);
        }
    }
}
