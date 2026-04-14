using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using ClaudeUsageMonitor.Models;



namespace ClaudeUsageMonitor.Services;

public class UsageService : IDisposable
{
    private static readonly string CredentialsPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", ".credentials.json");

    private const string TokenRefreshUrl = "https://platform.claude.com/v1/oauth/token";
    private const string UsageUrl = "https://api.anthropic.com/api/oauth/usage";
    private const string ClientId = "9d1c250a-e61b-44d9-88ed-5944d1962f5e";

    private readonly HttpClient _httpClient = new();
    private OAuthCredentials? _credentials;

    public void Dispose() => _httpClient.Dispose();

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

        using var request = new HttpRequestMessage(HttpMethod.Get, UsageUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _credentials.AccessToken);
        request.Headers.Add("anthropic-beta", "oauth-2025-04-20");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        var response = await _httpClient.SendAsync(request);

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            await RefreshTokenAsync();

            using var retryRequest = new HttpRequestMessage(HttpMethod.Get, UsageUrl);
            retryRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _credentials.AccessToken);
            retryRequest.Headers.Add("anthropic-beta", "oauth-2025-04-20");
            retryRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            response = await _httpClient.SendAsync(retryRequest);
        }

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            var retryAfter = response.Headers.RetryAfter?.Delta
                             ?? (response.Headers.RetryAfter?.Date - DateTimeOffset.UtcNow)
                             ?? TimeSpan.FromMinutes(5);
            if (retryAfter <= TimeSpan.Zero)
            {
                retryAfter = TimeSpan.FromMinutes(5);
            }
            throw new RateLimitedException(retryAfter);
        }

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize(json, UsageJsonContext.Default.UsageResponse);
    }

    private async Task LoadCredentialsAsync()
    {
        // Always read fresh from Claude Code's credentials file so we pick up any
        // refresh it performs, and never hold a stale refresh_token that the server
        // has rotated away under us.
        if (!File.Exists(CredentialsPath))
        {
            throw new FileNotFoundException($"No credentials file found at {CredentialsPath}");
        }

        var json = await File.ReadAllTextAsync(CredentialsPath);
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
        await File.WriteAllTextAsync(CredentialsPath, updatedJson);
    }
}
