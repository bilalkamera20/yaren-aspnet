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

            var rawItems = await FetchAllAsync();
            Console.WriteLine($"[DEBUG] Çekilen Toplam Kanal Sayısı: {rawItems.Count}");

            var turkishItems = rawItems.Where(IsTurkishChannel).ToList();
            Console.WriteLine($"[DEBUG] Filtrelenen Türkçe Kanal Sayısı: {turkishItems.Count}");

            var items = DeduplicateItems(turkishItems);
            items = items.OrderBy(x => SanitizeName(x.Name).ToLowerInvariant()).ToList();

            var m3uContent = ToM3u(items);
            string outputPath = Path.Combine(Directory.GetCurrentDirectory(), OUTPUT_FILENAME);
            await File.WriteAllTextAsync(outputPath, m3uContent, Encoding.UTF8);

            Console.WriteLine($"[BASARILI] Dosya oluşturuldu: {outputPath} ({items.Count} kanal)");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[KRITIK HATA] {ex.Message}\n{ex.StackTrace}");
            Environment.Exit(1);
        }
    }

    private static async Task<List<VavooItem>> FetchAllAsync()
    {
        var items = new List<VavooItem>();

        using var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        };

        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(FETCH_TIMEOUT_SECONDS) };

        // Vavoo güncel kanallarını barındıran uç noktalar
        var targetEndpoints = new List<string>
        {
            "https://vavoo.to/channels",
            "https://vavoo.to/vto-cluster/channels",
            "https://vavoo.to/mediahubmx-catalog.json"
        };

        foreach (var endpoint in targetEndpoints)
        {
            // Direct GET
            var fetched = await TryFetchAsync(client, endpoint);
            if (fetched.Count > 0)
            {
                items.AddRange(fetched);
                break;
            }

            // Worker Proxies üzerinden GET
            foreach (var proxy in PROXIES)
            {
                string proxiedUrl = $"{proxy.TrimEnd('/')}/?url={Uri.EscapeDataString(endpoint)}";
                fetched = await TryFetchAsync(client, proxiedUrl);
                if (fetched.Count > 0)
                {
                    items.AddRange(fetched);
                    break;
                }
            }

            if (items.Count > 0) break;
        }

        return items;
    }

    private static async Task<List<VavooItem>> TryFetchAsync(HttpClient client, string url)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            request.Headers.Add("Accept", "application/json, text/plain, */*");

            var response = await client.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                var rawResponse = await response.Content.ReadAsStringAsync();
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

                // 1. Dizi formatında doğrudan liste gelirse
                try
                {
                    var directList = JsonSerializer.Deserialize<List<VavooItem>>(rawResponse, options);
                    if (directList != null && directList.Count > 0) return directList;
                }
                catch { }

                // 2. Obje içinde Items formatında gelirse
                try
                {
                    var objResult = JsonSerializer.Deserialize<VavooResponse>(rawResponse, options);
                    if (objResult?.Items != null && objResult.Items.Count > 0) return objResult.Items;
                }
                catch { }
            }
        }
        catch
        {
            // Sonraki URL/Proxy denemesine geç
        }

        return new List<VavooItem>();
    }

    private static bool IsTurkishChannel(VavooItem item)
    {
        if (string.IsNullOrEmpty(item.Name)) return false;

        if (Regex.IsMatch(item.Name, @"\bTR\b|TR:", RegexOptions.IgnoreCase)) return true;

        var name = item.Name.ToUpperInvariant();
        string[] trKeywords = {
            "BEIN", "EXXEN", "S SPORT", "SPOR SMART", "TRT", "KANAL D", "ATV", "STAR",
            "SHOW", "NOW", "TV8", "HABERTURK", "HALK TV", "TELE1", "A HABER", "A SPOR",
            "DMAX", "TEVE2", "A2", "TGRT", "CINEMA", "SINEMA"
        };

        return trKeywords.Any(kw => name.Contains(kw));
    }

    private static string SanitizeName(string? name)
    {
        if (string.IsNullOrEmpty(name)) return "";
        var s = Regex.Replace(name, @"^\s*(?:[A-Z0-9-]+\s+)*TR:\s*", "", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\s*\.(?:b|c|s)\b", "", RegexOptions.IgnoreCase);
        return Regex.Replace(s, @"\s+", " ").Trim();
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

        var currentProxy = PROXIES[proxyIndex];
        proxyIndex = (proxyIndex + 1) % PROXIES.Count;
        return $"{currentProxy.TrimEnd('/')}/?url={Uri.EscapeDataString(url)}&master&transport=http&.m3u8";
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
