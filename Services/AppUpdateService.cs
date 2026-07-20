using System;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace ZapretMirrlyGUI.Services;

public class GitHubRelease
{
    [JsonPropertyName("tag_name")]
    public string TagName { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("prerelease")]
    public bool IsPrerelease { get; set; }

    [JsonPropertyName("body")]
    public string Body { get; set; } = "";

    [JsonPropertyName("html_url")]
    public string HtmlUrl { get; set; } = "";
}

public class GuiUpdateResult
{
    public bool UpdateAvailable { get; set; }
    public string CurrentVersion { get; set; } = "";
    public string LatestVersion { get; set; } = "";
    public bool IsPrerelease { get; set; }
    public string Changelog { get; set; } = "";
    public string DownloadUrl { get; set; } = "";
    public string StatusText { get; set; } = "";
}

public static class AppUpdateService
{
    public const string CurrentGuiVersion = "1.1.5";
    private const string ReleasesUrl = "https://api.github.com/repos/joycecurcirt539-dot/zapret-mirrly-gui/releases";

    public static GuiUpdateResult? LastCheckResult { get; set; }

    public static async Task<GuiUpdateResult> CheckForGuiUpdatesAsync()
    {
        var result = new GuiUpdateResult
        {
            CurrentVersion = CurrentGuiVersion,
            LatestVersion = CurrentGuiVersion,
            DownloadUrl = "https://github.com/joycecurcirt539-dot/zapret-mirrly-gui/releases"
        };

        try
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) ZapretMirrlyGUIUpdateChecker");

            var response = await client.GetAsync(ReleasesUrl);
            if (!response.IsSuccessStatusCode)
            {
                result.StatusText = $"Ошибка подключения к серверу обновлений (HTTP {(int)response.StatusCode})";
                LastCheckResult = result;
                return result;
            }

            var json = await response.Content.ReadAsStringAsync();
            var releases = JsonSerializer.Deserialize<GitHubRelease[]>(json);

            if (releases == null || releases.Length == 0)
            {
                result.StatusText = "На сервере обновлений не найдено выпусков.";
                LastCheckResult = result;
                return result;
            }

            // The first release returned is the latest release (could be stable or pre-release)
            var latestRelease = releases[0];
            result.LatestVersion = latestRelease.TagName;
            result.IsPrerelease = latestRelease.IsPrerelease;
            result.DownloadUrl = latestRelease.HtmlUrl;
            result.Changelog = CleanChangelog(latestRelease.Body);

            if (IsNewerVersion(CurrentGuiVersion, latestRelease.TagName))
            {
                result.UpdateAvailable = true;
                result.StatusText = latestRelease.IsPrerelease 
                    ? $"Доступно экспериментальное обновление: {latestRelease.TagName}" 
                    : $"Доступно стабильное обновление: {latestRelease.TagName}";
            }
            else
            {
                result.StatusText = "У вас установлена самая последняя версия приложения.";
            }
        }
        catch (Exception ex)
        {
            result.StatusText = $"Не удалось проверить обновления: {ex.Message}";
        }

        LastCheckResult = result;
        return result;
    }

    public static bool IsNewerVersion(string currentVer, string latestVer)
    {
        var cleanCurrent = currentVer.TrimStart('v').Split('-')[0];
        var cleanLatest = latestVer.TrimStart('v').Split('-')[0];

        if (Version.TryParse(cleanCurrent, out var current) && Version.TryParse(cleanLatest, out var latest))
        {
            return latest > current;
        }

        // Fallback lexicographical check if parsing fails
        return string.Compare(cleanLatest, cleanCurrent, StringComparison.OrdinalIgnoreCase) > 0;
    }

    public static string CleanChangelog(string rawBody)
    {
        if (string.IsNullOrEmpty(rawBody)) return "Описание изменений отсутствует.";

        var lines = rawBody.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        for (int i = 0; i < lines.Length; i++)
        {
            lines[i] = lines[i].Trim();
        }

        return string.Join(Environment.NewLine, lines).Trim();
    }
}
