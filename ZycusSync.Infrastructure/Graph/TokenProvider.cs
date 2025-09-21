using System.Text.Json;

namespace ZycusSync.Infrastructure.Graph;

public sealed class TokenProvider
{
    private readonly string _tenant, _client, _secret;
    private string? _token;
    private DateTimeOffset _exp;

    public TokenProvider(string tenant, string client, string secret)
        => (_tenant, _client, _secret) = (tenant, client, secret);

    public async Task<string> GetAsync(HttpClient http, CancellationToken ct)
    {
        if (_token is null || DateTimeOffset.UtcNow >= _exp.AddMinutes(-5))
            await RefreshAsync(http, ct);
        return _token!;
    }

    private async Task RefreshAsync(HttpClient http, CancellationToken ct)
    {
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = _client,
            ["client_secret"] = _secret,
            ["scope"] = "https://graph.microsoft.com/.default"
        };
        using var content = new FormUrlEncodedContent(form);
        using var resp = await http.PostAsync($"https://login.microsoftonline.com/{_tenant}/oauth2/v2.0/token", content, ct);
        var json = await resp.Content.ReadAsStringAsync(ct);
        resp.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(json);
        _token = doc.RootElement.GetProperty("access_token").GetString()!;
        _exp = DateTimeOffset.UtcNow.AddHours(1);
    }
}
