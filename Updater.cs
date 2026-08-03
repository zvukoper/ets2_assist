using System;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

public static class Updater
{
    public static async Task<Version> CheckLatestVersion(string apiUrl)
    {
        if (string.IsNullOrWhiteSpace(apiUrl))
            throw new ArgumentException("API URL cannot be empty", nameof(apiUrl));

        using var client = new HttpClient();
        // GitHub API требует User-Agent
        client.DefaultRequestHeaders.Add("User-Agent", "ETS2-Assist-Client");

        // Выполняем запрос
        var response = await client.GetAsync(apiUrl);

        // Проверяем статус
        if (!response.IsSuccessStatusCode)
        {
            string errorDetails = await response.Content.ReadAsStringAsync();
            throw new Exception($"GitHub API returned {response.StatusCode} ({response.ReasonPhrase}). Details: {errorDetails}");
        }

        // Читаем JSON
        string json = await response.Content.ReadAsStringAsync();
        var jsonObj = JObject.Parse(json);

        // Извлекаем tag_name
        string tag = jsonObj["tag_name"]?.ToString();
        if (string.IsNullOrEmpty(tag))
            throw new Exception("Response does not contain 'tag_name' field.");

        // Убираем префикс 'v' если есть
        if (tag.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            tag = tag.Substring(1);

        // Парсим версию
        if (!Version.TryParse(tag, out Version version))
            throw new Exception($"Invalid version format: '{tag}'. Expected format: X.Y.Z");

        return version;
    }
}