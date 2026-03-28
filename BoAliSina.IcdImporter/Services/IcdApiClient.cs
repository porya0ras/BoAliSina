using System.Net.Http.Headers;
using System.Text.Json;
using BoAliSina.IcdImporter.Models.Api;

namespace BoAliSina.IcdImporter.Services;

public interface IIcdApiClient
{
    Task<IcdApiConceptDto?> GetConceptAsync(string uri);
}

public class IcdApiClient : IIcdApiClient
{
    private readonly HttpClient _httpClient;
    private readonly string _clientId;
    private readonly string _clientSecret;
    private string? _accessToken;
    private DateTime _tokenExpiry = DateTime.MinValue;

    public IcdApiClient(HttpClient httpClient, string clientId, string clientSecret)
    {
        _httpClient = httpClient;
        _clientId = clientId;
        _clientSecret = clientSecret;
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _httpClient.DefaultRequestHeaders.Add("API-Version", "v2");
        _httpClient.DefaultRequestHeaders.Add("Accept-Language", "en");
    }

    private async Task EnsureAccessTokenAsync()
    {
        if (_accessToken != null && DateTime.UtcNow < _tokenExpiry.AddSeconds(-60)) return;

        // Try the standard endpoint
        var tokenEndpoint = "https://icdaccessmanagement.who.int/connect/token";
        var tokenRequest = new HttpRequestMessage(HttpMethod.Post, tokenEndpoint);
        
        var authHeaderValue = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{_clientId}:{_clientSecret}"));
        tokenRequest.Headers.Authorization = new AuthenticationHeaderValue("Basic", authHeaderValue);

        tokenRequest.Content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "client_credentials"),
            new KeyValuePair<string, string>("scope", "icdapi_access")
        });

        var response = await _httpClient.SendAsync(tokenRequest);
        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
             // Log more details into the exception message for debugging
             throw new Exception($"WHO Token Request Failed!\nStatus: {response.StatusCode}\nBody: {responseBody}\nEndpoint: {tokenEndpoint}\nClientId: {_clientId[..10]}...");
        }

        using var doc = JsonDocument.Parse(responseBody);
        _accessToken = doc.RootElement.GetProperty("access_token").GetString();
        var expiresIn = doc.RootElement.GetProperty("expires_in").GetInt32();
        _tokenExpiry = DateTime.UtcNow.AddSeconds(expiresIn);
    }

    public async Task<IcdApiConceptDto?> GetConceptAsync(string uri)
    {
        await EnsureAccessTokenAsync();

        // Enforce HTTPS as the WHO API drops tokens on HTTP redirects/downgrades
        var httpsUri = uri.Replace("http://id.who.int", "https://id.who.int");

        var request = new HttpRequestMessage(HttpMethod.Get, httpsUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

        var response = await _httpClient.SendAsync(request);
        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new Exception($"WHO Resource Request Failed!\nURI: {httpsUri}\nStatus: {response.StatusCode}\nBody: {responseBody}");
        }

        return JsonSerializer.Deserialize<IcdApiConceptDto>(responseBody);
    }
}
