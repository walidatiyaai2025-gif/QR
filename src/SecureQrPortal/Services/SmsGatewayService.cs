using System.Net;
using Microsoft.AspNetCore.WebUtilities;

namespace SecureQrPortal.Services;

public sealed record SmsGatewayResult(
    bool Success,
    string NormalizedMobile,
    int? HttpStatusCode,
    string ResponseText,
    string? Error = null);

public sealed class SmsGatewayService(IConfiguration configuration)
{
    private static readonly HttpClient Client = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        AutomaticDecompression = DecompressionMethods.All
    })
    {
        Timeout = TimeSpan.FromSeconds(20)
    };

    private IConfigurationSection Settings => configuration.GetSection("SmsGateway");

    public bool IsConfigured =>
        Settings.GetValue("Enabled", false) &&
        !string.IsNullOrWhiteSpace(Settings["BaseUrl"]) &&
        !string.IsNullOrWhiteSpace(Settings["IID"]) &&
        !string.IsNullOrWhiteSpace(Settings["UID"]) &&
        !string.IsNullOrWhiteSpace(Settings["Sender"]) &&
        !string.IsNullOrWhiteSpace(Settings["Password"]);

    public async Task<SmsGatewayResult> SendAsync(string? mobile, string? message, CancellationToken ct = default)
    {
        var normalizedMobile = NormalizeMobile(mobile, Settings["DefaultCountryCode"] ?? "965");
        if (normalizedMobile is null)
            return new(false, string.Empty, null, string.Empty, "Enter a valid destination mobile number using 8 to 15 digits.");

        if (string.IsNullOrWhiteSpace(message))
            return new(false, normalizedMobile, null, string.Empty, "SMS message cannot be empty.");

        if (!IsConfigured)
            return new(false, normalizedMobile, null, string.Empty, "SMS gateway is not configured. Set SmsGateway credentials in configuration or environment variables.");

        var baseUrl = Settings["BaseUrl"]!;
        var query = new Dictionary<string, string?>
        {
            ["IID"] = Settings["IID"],
            ["UID"] = Settings["UID"],
            ["S"] = Settings["Sender"],
            ["G"] = normalizedMobile,
            ["M"] = message.Trim(),
            ["L"] = Settings["Language"] ?? "L",
            ["p"] = Settings["Password"]
        };

        // The provider contract supplied by the administrator is GET-based. Do not
        // log this request URI because it contains the provider password parameter.
        var requestUrl = QueryHelpers.AddQueryString(baseUrl, query);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
            using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            body = SanitizeResponse(body);

            return new(
                response.IsSuccessStatusCode,
                normalizedMobile,
                (int)response.StatusCode,
                body,
                response.IsSuccessStatusCode ? null : $"SMS gateway returned HTTP {(int)response.StatusCode}.");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new(false, normalizedMobile, null, string.Empty, "SMS gateway request timed out.");
        }
        catch (HttpRequestException ex)
        {
            return new(false, normalizedMobile, null, string.Empty, $"SMS gateway connection failed: {ex.GetType().Name}.");
        }
    }

    public static string? NormalizeMobile(string? mobile, string defaultCountryCode)
    {
        if (string.IsNullOrWhiteSpace(mobile)) return null;

        var digits = new string(mobile.Where(char.IsDigit).ToArray());
        if (digits.StartsWith("00", StringComparison.Ordinal)) digits = digits[2..];

        var country = new string((defaultCountryCode ?? string.Empty).Where(char.IsDigit).ToArray());
        if (digits.Length == 8 && !string.IsNullOrWhiteSpace(country)) digits = country + digits;

        return digits.Length is >= 8 and <= 15 ? digits : null;
    }

    private static string SanitizeResponse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "(empty response)";
        var cleaned = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return cleaned.Length <= 500 ? cleaned : cleaned[..500];
    }
}
