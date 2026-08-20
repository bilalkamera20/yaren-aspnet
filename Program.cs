using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

class Program
{
    private const string GROUP = "Turkey";
    private const string OUTPUT_FILENAME = "nernur.txt";
    private const int FETCH_TIMEOUT_SECONDS = 15;

    private static readonly List<string> PROXIES = new List<string>
    {
        "https://halil.bilalkamera20.workers.dev",
        "https://adam.bilalkamera20.workers.dev",
        "https://ner.bilalkamera20.workers.dev",
        "https://nur.bilalkamera20.workers.dev",
        "https://vavoo-iptv-proxy.bilalkamera20.workers.dev",
        "https://nernur.bilalkamera20.workers.dev",
        "https://balkica.bilalkamera20.workers.dev",
        "https://bilal.bilalkamera20.workers.dev",
        "https://vav20.bilalkamera20.workers.dev",
        "https://hmeb.bilalkamera20.workers.dev"
    };

    private static int proxyIndex = 0;

    static async Task Main(string[] args)
    {
        try
        {
            Console.WriteLine("[DEBUG] Uygulama başlatıldı.");
            Console.WriteLine($"[DEBUG] Çalışma dizini: {Directory.GetCurrentDirectory()}");

            var rawItems = await FetchAllAsync();
            Console.WriteLine($"[DEBUG] FetchAllAsync tamamlandı, {rawItems.Count} öğe döndü.");

            if (rawItems.Count == 0)
            {
                Console.WriteLine("[HATA] API'den hiçbir kanal çekilemedi.");
                // Environment.Exit(1); // Debug için devre dışı
            }

            var items = DeduplicateItems(rawItems);
            items = items.OrderBy(x => SanitizeName(x.Name).ToLowerInvariant()).ToList();

            var m3uContent = ToM3u(items);
            string outputPath = Path.Combine(Directory.GetCurrentDirectory(), OUTPUT_FILENAME);
            await File.WriteAllTextAsync(outputPath, m3uContent, Encoding.UTF8);

            Console.WriteLine($"[DEBUG] M3U dosyası yazıldı: {outputPath}");
            Console.WriteLine($"[BASARILI] Dosya oluşturuldu: {outputPath} ({items.Count} kanal)");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[KRITIK HATA] {ex.Message}");
            // Environment.Exit(1); // Debug için devre dışı
        }
    }

    private static async Task<List<VavooItem>> FetchAllAsync()
    {
        var items = new List<VavooItem>();
        string? cursor = null;
        int page = 0;

        using var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        };

        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(FETCH_TIMEOUT_SECONDS) };

        while (true)
        {
            page++;
            var data = await FetchPageWithProxyFallbackAsync(client, cursor);

            if (data?.Items != null && data.Items.Count > 0)
            {
                items.AddRange(data.Items);
                Console.WriteLine($"[DEBUG] Sayfa {page}: {data.Items.Count} kanal eklendi.");
            }
            else
            {
                Console.WriteLine($"[DEBUG] Sayfa {page}: Veri alınamadı veya liste sonuna ulaşıldı.");
                break;
            }

            cursor = data?.NextCursor;
            if (page >= 200 || string.IsNullOrEmpty(cursor))
            {
                break;
            }
        }

        return items;
    }

    private static async Task<VavooResponse?> FetchPageWithProxyFallbackAsync(HttpClient client, string? cursor)
    {
        var bodyObj = new
        {
            language = "de",
            region = "DE",
            catalogId = "iptv",
            id = "",
            adult = false,
            search = "",
            sort = "name",
            filter = new { group = GROUP },
            cursor = cursor
        };

        var jsonBody = JsonSerializer.Serialize(bodyObj);

        var endpoints = new List<string>
        {
            "https://vavoo.to/mediahubmx-catalog.json",
            "https://vavoo.to/vto-cluster/mediahubmx-catalog.json"
        };

        foreach (var url in endpoints)
        {
            try
            {
                Console.WriteLine($"[DEBUG] Denenen endpoint: {url}");

                using var request = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StringContent(jsonBody, Encoding.UTF8, "application/json"),
                    Version = HttpVersion.Version11
                };

                request.Headers.Add("User-Agent", "MediaHubMX/2.0.0");
                request.Headers.Add("Accept", "application/json");
                request.Headers.Add("Accept-Encoding", "gzip, deflate");

                var response = await client.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    var rawResponse = await response.Content.ReadAsStringAsync();
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var result = JsonSerializer.Deserialize<VavooResponse>(rawResponse, options);

                    if (result != null && result.Items
                            private static string ToM3u(List<VavooItem> items)
    {
        var sb = new StringBuilder();
        sb.AppendLine("#EXTM3U");

        foreach (var it in items)
        {
            var name = SanitizeName(it.Name);
            if (string.IsNullOrEmpty(name)) continue;

            var group = Categorize(name);
            var streamUrl = ToStreamUrl(it.Url ?? "");

            sb.AppendLine($"#EXTINF:-1 tvg-id=\"{it.Ids?.Id}\" tvg-name=\"{name}\" tvg-logo=\"{it.Logo}\" group-title=\"{group}\",{name}");
            sb.AppendLine(streamUrl);
        }

        return sb.ToString();
    }
}

// --- Model sınıfları ---
public class VavooResponse
{
    public List<VavooItem>? Items { get; set; }
    public string? NextCursor { get; set; }
}

public class VavooItem
{
    public string? Name { get; set; }
    public string? Url { get; set; }
    public string? Logo { get; set; }
    public VavooIds? Ids { get; set; }
}

public class VavooIds
{
    public string? Id { get; set; }
}
