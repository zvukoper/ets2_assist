using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace ETS2_Assist_GUI
{
    public static class Updater
    {
        public static async Task<Version?> CheckLatestVersion(string apiUrl)
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("ETS2-Assist/1.0");
            try
            {
                var response = await client.GetStringAsync(apiUrl);
                using var doc = JsonDocument.Parse(response);
                var root = doc.RootElement;
                if (root.TryGetProperty("tag_name", out var tagElement))
                {
                    string tag = tagElement.GetString() ?? "";
                    if (tag.StartsWith("v")) tag = tag.Substring(1);
                    if (Version.TryParse(tag, out var version))
                        return version;
                }
                return null;
            }
            catch
            {
                return null;
            }
        }
    }
}