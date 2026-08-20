using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

class Program
{
    // Birincil ve Yedek Vavoo API Endpoints
    private static readonly string[] CATALOG_URLS = new[]
    {
        "https://vavoo.to/mediahubmx-catalog.json",
        "https://vavoo.to/vto-cluster/mediahubmx-catalog.json"
    };

    private const string GROUP = "Turkey";
    private const string OUTPUT_FILENAME = "nernur.txt";
    private const int FETCH_TIMEOUT_SECONDS = 30;
    private const int MAX_RETRIES = 5;

    private static readonly List<string> FALLBACK_PROXIES = new()
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

    private static List<string> PROXY_LIST = new();
    private static int proxyIndex = 0;

    static async Task Main(string[] args)
    {
        try
        {
            var envProxy = Environment.GetEnvironmentVariable("PROXY_BASE");
            var parsedProxies = ParseProxies(envProxy);
            PROXY_LIST = parsedProxies.Count > 0 ? parsedProxies : FALLBACK_PROXIES;

            Console.WriteLine($"[BILGI] Aktif Proxy Sayisi: {PROXY_LIST.Count}");

            var rawItems = await FetchAllAsync();
            Console.WriteLine($"[BILGI] Toplam ham kanal sayısı: {rawItems.Count}");

            if (rawItems.Count == 0)
            {
                throw new Exception("API'den hicbir kanal verisi alinamadi. Sunucu istegi engellemis olabilir.");
            }

            var items = DeduplicateItems(rawItems);
            if (rawItems.Count != items.Count)
            {
                Console.WriteLine($"[BILGI] Mukerrer yayinlar temizlendi. Kalan: {items.Count}");
            }

            items = items.OrderBy(x => SanitizeName(x.Name).ToLowerInvariant()).ToList();

            var m3uContent = ToM3u(items);

            string outputPath = Path.Combine(Directory.GetCurrentDirectory(), OUTPUT_FILENAME);
            await File.WriteAllTextAsync(outputPath, m3uContent, Encoding.UTF8);

            Console.WriteLine($"[BASARILI] Dosya olusturuldu: {outputPath} ({items.Count} kanal)");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n==================== HATA DETAYI ====================");
            Console.WriteLine($"Hata Mesaji : {ex.Message}");
            Console.WriteLine($"Hata Tipi   : {ex.GetType().FullName}");
            Console.WriteLine($"Izleme (Trace):\n{ex.StackTrace}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"Ic Hata     : {ex.InnerException.Message}");
            }
            Console.WriteLine($"=====================================================\n");
            
            Environment.Exit(1);
        }
    }

    private static List<string> ParseProxies(string? envVal)
    {
        if (string.IsNullOrWhiteSpace(envVal)) return new List<string>();

        var tokens = Regex.Split(envVal.Trim(), @"[\s,]+");
        var proxies = new List<string>();
        foreach (var p in tokens)
        {
            var cleanP = p.TrimEnd('/');
            if (cleanP.StartsWith("http://") || cleanP.StartsWith("https://"))
            {
                proxies.Add(cleanP);
            }
        }
        return proxies;
    }

    private static async Task<List<VavooItem>> FetchAllAsync()
    {
        var items = new List<VavooItem>();
        string? cursor = null;
        int page = 0;
        const int MAX_PAGES = 200;

        using var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
        };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(FETCH_TIMEOUT_SECONDS) };
        
        client.DefaultRequestHeaders.Clear();
        client.DefaultRequestHeaders.Add("User-Agent", "MediaHubMX/2.0.0");
        client.DefaultRequestHeaders.Add("Accept", "application/json");
        client.DefaultRequestHeaders.Add("Origin", "https://vavoo.to");
        client.DefaultRequestHeaders.Add("Referer", "https://vavoo.to/");

        while (true)
        {
            page++;
            var data = await FetchPageAsync(client, cursor);

            if (data?.Items != null && data.Items.Count > 0)
            {
                items.AddRange(data.Items);
            }

            Console.WriteLine($"Sayfa {page}: {data?.Items?.Count ?? 0} kanal eklendi. (NextCursor: {data?.NextCursor ?? "null"})");

            cursor = data?.NextCursor;

            if (page >= MAX_PAGES || string.IsNullOrEmpty(cursor))
            {
                break;
            }
        }

        return items;
    }

    private static async Task<VavooResponse?> FetchPageAsync(HttpClient client, string? cursor)
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
        Exception? lastErr = null;

        // Farklı Endpoint URL'leri ve Deneme Mantığı
        foreach (var targetUrl in CATALOG_URLS)
        {
            for (int attempt = 1; attempt <= MAX_RETRIES; attempt++)
            {
                try
                {
                    var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
                    var response = await client.PostAsync(targetUrl, content);
                    
                    if (response.IsSuccessStatusCode)
                    {
                        var rawResponse = await response.Content.ReadAsStringAsync();
                        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                        return JsonSerializer.Deserialize<VavooResponse>(rawResponse, options);
                    }
                    else
                    {
                        Console.WriteLine($"[UYARI] {targetUrl} Yanit Kodu: {response.StatusCode}. Deneme {attempt}/{MAX_RETRIES}");
                    }
                }
                catch (Exception err)
                {
                    lastErr = err;
                    Console.WriteLine($"[UYARI] Deneme {attempt}/{MAX_RETRIES} basarisiz ({err.Message}). {attempt * 2}sn bekleniyor...");
                }

                await Task.Delay(attempt * 2000);
            }
        }

        throw lastErr ?? new Exception("Tüm Vavoo API bağlantı noktaları yanıt vermeyi reddetti.");
    }

    private static string SanitizeName(string? name)
    {
        if (string.IsNullOrEmpty(name)) return "";
        var s = Regex.Replace(name, @"^\s*(?:[A-Z0-9-]+\s+)*TR:\s*", "", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\s*\.(?:b|c|s)\b", "", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\s+", " ");
        return s.Trim();
    }

    private static string Categorize(string name)
    {
        var clean = Regex.Replace(SanitizeName(name), @"\s+(?:UHD|FHD|HD\+|HD|SD|HEVC|RAW|H265|H\.265|FEED)(?=\s|$)", " ", RegexOptions.IgnoreCase);

        if (Regex.IsMatch(clean, @"\b(BEIN SPO[RT]{0,3}S?|BEIN 1|S[- ]?SPORTS?|S SPORT|SPOR SMART|EUROSPORT|TRT SPOR|EXXEN SPO[RT]?|A SPOR)\b", RegexOptions.IgnoreCase))
            return "TR SPOR";
        if (Regex.IsMatch(clean, @"\b(CARTOON|DISNEY|NICK|MINIKA|TRT ?[ÇC]?OCUK)\b", RegexOptions.IgnoreCase))
            return "TR ÇOCUK";
        if (Regex.IsMatch(clean, @"\b(DISCOVERY|NATIONAL GEOGRAPHIC|NAT ?GEO|HISTORY|TRT BELGESEL|DMAX)\b", RegexOptions.IgnoreCase))
            return "TR BELGESEL";
        if (Regex.IsMatch(clean, @"\b(SINEMA|CINEMA|MOVIES?|BEIN MOVIES|YESILCAM)\b", RegexOptions.IgnoreCase))
            return "TR SİNEMA";
        if (Regex.IsMatch(clean, @"\b(HABER|NEWS|CNN|HALK TV|TELE ?1|SOZCU|TRT WORLD|A HABER)\b", RegexOptions.IgnoreCase))
            return "TR HABER";
        if (Regex.IsMatch(clean, @"\b(TRT 1|KANAL D|ATV|STAR|SHOW|NOW|TV8|BEYAZ)\b", RegexOptions.IgnoreCase))
            return "TR ULUSAL";

        return "TR GENEL";
    }

    private static string ToStreamUrl(string url)
    {
        if (string.IsNullOrEmpty(url)) return "";

        if (PROXY_LIST.Count > 0)
        {
            var currentProxy = PROXY_LIST[proxyIndex];
            proxyIndex = (proxyIndex + 1) % PROXY_LIST.Count;
            return $"{currentProxy}/?url={Uri.EscapeDataString(url)}&master&transport=http&.m3u8";
        }
        return url;
    }

    private static List<VavooItem> DeduplicateItems(List<VavooItem> items)
    {
        var seen = new HashSet<string>();
        var filtered = new List<VavooItem>();

        foreach (var item in items)
        {
            if (string.IsNullOrEmpty(item.Url)) continue;
            var key = item.Ids?.Id != null ? $"{item.Ids.Id}-{item.Url}" : item.Url;

            if (seen.Add(key))
            {
                filtered.Add(item);
            }
        }
        return filtered;
    }

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
