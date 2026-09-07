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
        client.DefaultRequestHeaders.Add("User-Agent", "ETS2-Assist-Client");

        var response = await client.GetAsync(apiUrl);
        if (!response.IsSuccessStatusCode)
        {
            string errorDetails = await response.Content.ReadAsStringAsync();
            throw new Exception($"GitHub API returned {response.StatusCode} ({response.ReasonPhrase}). Details: {errorDetails}");
        }

        string json = await response.Content.ReadAsStringAsync();
        var jsonObj = JObject.Parse(json);

        string? tag = jsonObj["tag_name"]?.ToString();
        if (string.IsNullOrEmpty(tag))
            throw new Exception("Response does not contain 'tag_name' field.");

        if (tag.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            tag = tag.Substring(1);

        if (!Version.TryParse(tag, out var version) || version == null)
            throw new Exception($"Invalid version format: '{tag}'. Expected format: X.Y.Z");

        return version;
    }
}