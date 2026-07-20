using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace ZapretMirrlyGUI.Services;

public static class IspService
{
    private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
    public static string CachedIspName { get; private set; } = "Не определен";
    private static bool _isFetching = false;

    public static async Task<string> GetIspNameAsync()
    {
        if (CachedIspName != "Не определен" && !string.IsNullOrWhiteSpace(CachedIspName))
            return CachedIspName;

        if (_isFetching)
            return CachedIspName;

        _isFetching = true;

        try
        {
            // Primary lookup: ip-api.com (returns clean ISP/Org name, no IP required in request body)
            var response = await _httpClient.GetStringAsync("http://ip-api.com/json/?fields=status,isp,org,as");
            using var doc = JsonDocument.Parse(response);
            var root = doc.RootElement;

            if (root.TryGetProperty("status", out var status) && status.GetString() == "success")
            {
                string isp = root.TryGetProperty("isp", out var ispProp) ? ispProp.GetString() ?? "" : "";
                string org = root.TryGetProperty("org", out var orgProp) ? orgProp.GetString() ?? "" : "";

                string finalName = !string.IsNullOrWhiteSpace(isp) ? isp : org;
                if (!string.IsNullOrWhiteSpace(finalName))
                {
                    CachedIspName = CleanIspName(finalName);
                    return CachedIspName;
                }
            }
        }
        catch
        {
            // Fallback lookup: yandex.ru internetometer API
            try
            {
                var yndxRes = await _httpClient.GetStringAsync("https://speed.yandex.ru/api/v2/as_info");
                using var doc = JsonDocument.Parse(yndxRes);
                if (doc.RootElement.TryGetProperty("name", out var yndxName))
                {
                    string name = yndxName.GetString() ?? "";
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        CachedIspName = CleanIspName(name);
                        return CachedIspName;
                    }
                }
            }
            catch { }
        }
        finally
        {
            _isFetching = false;
        }

        return CachedIspName;
    }

    private static string CleanIspName(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "Не определен";

        // Remove common corporate abbreviations for cleaner UI rendering
        string cleaned = raw
            .Replace("PJSC", "", StringComparison.OrdinalIgnoreCase)
            .Replace("JSC", "", StringComparison.OrdinalIgnoreCase)
            .Replace("LLC", "", StringComparison.OrdinalIgnoreCase)
            .Replace("ПАО", "", StringComparison.OrdinalIgnoreCase)
            .Replace("ООО", "", StringComparison.OrdinalIgnoreCase)
            .Replace("АО", "", StringComparison.OrdinalIgnoreCase)
            .Trim();

        return string.IsNullOrWhiteSpace(cleaned) ? raw : cleaned;
    }
}
