using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

class Program
{
    private const string CATALOG_URL = "https://vavoo.to/vto-cluster/mediahubmx-catalog.json";
    private const string OUTPUT_FILE = "nernur.txt";

    private static readonly List<string> WORKER_PROXIES = new List<string>
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

    static async Task Main(string[] args)
    {
        try
        {
            Console.WriteLine("[DEBUG] Uygulama başlatıldı.");

            using var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
            };

            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };

            client.DefaultRequestHeaders.Clear();
            client.DefaultRequestHeaders.Add("User-Agent", "MediaHubMX/3.0.2");
            client.DefaultRequestHeaders.Add("Accept", "application/json");
            client.DefaultRequestHeaders.Add("X-MediaHubMX-Signature", "");
            client.DefaultRequestHeaders.Add("Connection", "keep-alive");

            object cursor = 0;
            bool hasNext = true;
            var seenCursors = new HashSet<string>();
            var seenUrls = new HashSet<string>();
            int pageCount = 0;
            int maxPages = 200;
            int proxyIndex = 0;
            int proxyCount = WORKER_PROXIES.Count;

            var output = new StringBuilder("#EXTM3U\n");

            while (hasNext && pageCount < maxPages)
            {
                pageCount++;

                var payload = new VavooPayload
                {
                    Language = "tr",
                    Region = "TR",
                    CatalogId = "iptv",
                    Id = "iptv",
                    Adult = false,
                    Search = "",
                    Sort = "name",
                    Filter = new object(),
                    ClientVersion = "3.0.2",
                    Cursor = cursor
                };

                string jsonBody = JsonSerializer.Serialize(payload);
                using var request = new HttpRequestMessage(HttpMethod.Post, CATALOG_URL)
                {
                    Content = new StringContent(jsonBody, Encoding.UTF8, "application/json")
                };

                HttpResponseMessage response;
                try
                {
                    response = await client.SendAsync(request);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[HATA] Baglanti hatasi: {ex.Message}");
                    break;
                }

                if (response.IsSuccessStatusCode)
                {
                    string rawResponse = await response.Content.ReadAsStringAsync();

                    // PHP'deki strpos($response, '{') mantigi (Baslangic çöp karakterlerini temizleme)
                    int jsonStart = rawResponse.IndexOf('{');
                    if (jsonStart >= 0)
                    {
                        rawResponse = rawResponse.Substring(jsonStart);
                    }

                    VavooCatalogResponse? data = null;
                    try
                    {
                        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                        data = JsonSerializer.Deserialize<VavooCatalogResponse>(rawResponse, options);
                    }
                    catch { }

                    var items = data?.Items;
                    if (items == null || items.Count == 0)
                    {
                        Console.WriteLine($"[DEBUG] Sayfa {pageCount}: Veri gelmedi veya bitti.");
                        break;
                    }

                    Console.WriteLine($"[DEBUG] Sayfa {pageCount}: {items.Count} kanal alindi.");

                    foreach (var item in items)
                    {
                        if (string.IsNullOrEmpty(item.Url)) continue;

                        if (seenUrls.Contains(item.Url)) continue;
                        seenUrls.Add(item.Url);

                        string rawName = string.IsNullOrEmpty(item.Name) ? "Bilinmeyen Kanal" : item.Name;
                        string cleanName = CleanChannelName(rawName);

                        string rawGroup = item.Group ?? "";
                        string group = string.Equals(rawGroup, "Turkey", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(rawGroup)
                            ? CategorizeChannel(cleanName)
                            : rawGroup;

                        string proxy = WORKER_PROXIES[proxyIndex];
                        proxyIndex = (proxyIndex + 1) % proxyCount;

                        string proxiedUrl = $"{proxy.TrimEnd('/')}/?url={Uri.EscapeDataString(item.Url)}&master&transport=http&.m3u8";
                        output.AppendLine($"#EXTINF:-1 group-title=\"{group}\",{cleanName}");
                        output.AppendLine(proxiedUrl);
                    }

                    var nextCursor = data?.NextCursor;
                    string nextCursorStr = nextCursor?.ToString() ?? "";

                    if (nextCursor == null || string.IsNullOrEmpty(nextCursorStr) || seenCursors.Contains(nextCursorStr))
                    {
                        hasNext = false;
                    }
                    else
                    {
                        seenCursors.Add(cursor.ToString() ?? "");
                        cursor = nextCursor;
                    }
                }
                else
                {
                    Console.WriteLine($"[HATA] HTTP Kod: {response.StatusCode}");
                    break;
                }
            }

            string outputPath = Path.Combine(Directory.GetCurrentDirectory(), OUTPUT_FILE);
            await File.WriteAllTextAsync(outputPath, output.ToString(), Encoding.UTF8);
            Console.WriteLine($"[BASARILI] Dosya yazildi: {outputPath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[KRITIK HATA] {ex.Message}\n{ex.StackTrace}");
            Environment.Exit(1);
        }
    }

    private static string CleanChannelName(string name)
    {
        var s = Regex.Replace(name, @"^\s*(?:[A-Z0-9-]+\s+)*TR:\s*", "", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\s*\.(?:b|c|s)\b", "", RegexOptions.IgnoreCase);
        return Regex.Replace(s, @"\s+", " ").Trim();
    }

    private static string NormalizeForCategory(string name)
    {
        var s = CleanChannelName(name);
        s = Regex.Replace(s, @"\s+(?:UHD|FHD|HD\+|HD|SD|HEVC|RAW|H265|H\.265|FEED)(?=\s|$)", " ", RegexOptions.IgnoreCase);
        return Regex.Replace(s, @"\s+", " ").Trim();
    }

    private static string CategorizeChannel(string name)
    {
        string s = NormalizeForCategory(name);

        var rules = new Dictionary<string, string>
        {
            { "TR SPOR", @"\b(BEIN SPO[RT]{0,3}S?|BEIN 1|S[- ]?SPORTS?|S SPORT|SPOR SMART|EUROSPORT|NBA|TJK TV|TIVIBU ?SPOR|TIVIBUSPOR|TRT SPOR|TABII SPOR|EXXEN SPO[RT]?|HT SPOR|EKOL SPOR|SPORTS TV|IDMAN TV|GALATASARAY TV|FB TV|GS TV|SARAN SPORT|SMART SPOR|SPOR|SPORT)\b" },
            { "TR ÇOCUK", @"\b(CARTOON|BOOMERANG|DISNEY|NICK(?:ELODEON|TOONS|JR|JUNIOR)?|BABY ?TV|BABYTV|M[İI]?N ?KA|MINIKA|POKEMON|POKÉMON|ANIMATION|ANIMASYON|TRT ?[ÇC]?OCUK|[ÇC]OCUK|BEN ?10|ANGRY BIRDS|CAILLOU|PEPPA|PEPE|HEIDI|SIRINLER|TOM & JERRY|SPIDERMAN|BARBIE|PIJAMA|PIRIL|RAFADAN|KELOGLAN|KUKULI|KUKILI|KOSTEBEK|CHICKY|BOOBA|WAKFU|GABBY|TAYO|NILOYA|PISI|LEYLEK|MASAL|CANIM KARDESIM|ADIBESA|MOMO|ALVIN|VIKINGLER|TRANSFORMERS|TROL AVCILARI|SMART COCUK|ILAHI COCUK|CILGIN ORMAN|KRAL SAKIR|SERCE KUS|ITFAYECI SAM|MUFFETIS|MAYMUNLAR|ELIF VE|ELIFIN|MIMOCAN|HAPSUU|RUYA TRENI|MASA KOCAAYI|PAK PIRPIR|LIMON ZEYTIN|GONCA TV|NASREDDIN|SEKER HOCA|SEVIMLI DOSTLAR|PAW PETROL|OSCAR COLLERDE|CBEEBIES|DUCK TV|JIM ?JAM|ENGLISH CLUB TV|EBA TV|PATRON BEBEK|DA VINC KIDS|DA VINCI KIDS)\b" },
            { "TR BELGESEL", @"\b(DISCOVERY|NATIONAL GEOGRAPHIC|NAT ?GEO|HISTORY|ANIMAL PLANET|DA VINCI|VIASAT|BBC EARTH|LOVE NATURE|TRT BELGESEL|EPIC DRAMA|TARIH TV|TARIM TV|TGRT BELGESEL|INVESTIGATION|DMAX|DOCUBOX|DOCU SCREEN|SCIENCE|IZ TV|YABAN|OUTDOOR|CHASSE|ANIMAUX|AGRO TV|CIFTCI TV|REDBULL TV|TLC)\b" },
            { "TR SİNEMA", @"\b(SINEMA|S[İI]NEMA|CINEMA|SINEMAX|SINEVIZYON|MOVIES?|MOVIEMAX|MOVIESMART|BEIN MOVIES|BEIN BOX|BOX OFFICE|FX|FX HD|YESILCAM|YE[ŞS]ILCAM|GLOBAL BOX|PROTURK|FIX CINEMA|KINGBOX|ARENA BOX|SHOWMAX|SHOW MAX|REAL BOX|SMART BOX|FILMBOX|HORROR|OSCAR|KEMAL SUNAL|007|CINE ?1|AKSIYON|KORKU|DRAM|WESTERN|BILIM ?KURGU|SAVAS|IMBD|IMDB|FILM)\b" },
            { "TR DİZİ", @"\b(SER[İI]ES|DIZI|BEIN SERIES|D[İI]Z[İI] ?SMART|DIZISMART)\b" },
            { "TR HABER", @"\b(HABER|NEWS|BLOOMBERG|CNN|EKOTURK|EKO ?T[UÜ]RK|EKOL|A ?PARA|APARA|PARANIN|HALK TV|TELE ?1|SOZCU|SZC|BENGU ?T[UÜ]RK|BENGUTURK|TRT WORLD|DHA|LIDER HABER|FLASH HABER|MEDYA HABER|GLOBAL HABER|TRABZON HABER|BEIN SPORTS HABER|T[UÜ]RKHABER|HABERT[UÜ]RK|HABERT RK|ARTI TV)\b" },
            { "TR MÜZİK", @"\b(POWER T[UÜ]RK|POWER ?TV|POWERTURK|POWER|KRAL POP|KRAL ?TV|KRAL|TRT M[UÜ]?Z[İI]?K|TRT MUZIK|NR ?1|NUMBER ?1|NUMBER ONE|DAMAR|ARABESK|AKUSTIK|AHMET KAYA|IBRAHIM ERKAL|IBRAHIM TATLISES|TATLISES|ZERRIN OZER|SEZEN AKSU|TARKAN|SELDA BAGCAN|CENGIZ KURTOGLU|MAHSUN KIRMIZIGUL|MUSLUM GURSES|YILDIZ TILBE|FERDI TAYFUR|MTV LIVE|VINTAGE MUSIC|RETRO TURK|MUZIK|FM TV|FMTV|REDBOX)\b" },
            { "TR RADYO", @"\b(RADIO|RADYO|FM|MBAT FM|EFKAR FM|FMTV|POWERTURK|POWER FM|SHOW RADYO|ALEM FM|BABA RADYO|KRAL POP RADYO|PAL STATION|X NOSTALJI|RADIO ROCK|ISTANBUL FM)\b" },
            { "TR DİNİ", @"\b(D[İI]YANET|AK[İI]?T|MEHTAP|H[İI]LAL|KUDUS|KUDÜS|SEMERKAND|LALEGUL|LÂLEGÜL|MERCAN TV|VUSLAT|KARDELEN|DIYAR TV|DOST TV|YOL TV|KANAL 7|TVNET|TRT DIYANET|TV5|REHBER|ILAHI|ILKE TV|MESAJ TV|SURELER|CEM TV)\b" },
            { "TR ULUSAL", @"\b(TRT|TRT 1|TRT 2|TRT 3|TRT AVAZ|TRT T[UÜ]RK|TRT KURD[İI]?|TRT WORLD|TRT 4K|TRT EBA|KANAL D|ATV|STAR TV|STAR|SHOW TV|SHOW|NOW ?TV|NOW|TV8|TV8[.,]5|BEYAZ TV|BEYAZ|360|24 TV|A2|A HABER|A NEWS|A PARA|A SPOR|TV100|TV4|FLASH TV|TEVE2|CNN T[UÜ]RK|KRT|ULUSAL KANAL|DREAM TURK|NTV|EXXEN TV|TABII|ULKE TV)\b" },
            { "TR YEREL", @"\b(ADANA|ADIYAMAN|AFYON|AKSARAY|ALANYA|ANKARA|ANTALYA|BURSA|ELAZIG|ERZURUM|ESKISEHIR|GAZIANTEP|KAHRAMANMARAS|KAYSERI|KOCAELI|KONYA|MALATYA|MERSIN|ORDU|SIVAS|TRABZON|URFA|IZMIR|KIBRIS|DENIZLI|KANAL 12|KANAL 15|KANAL 23|KANAL 24|KANAL 26|KANAL 3|KANAL 32|KANAL 33|KANAL 34|KANAL 42|KANAL 58|KANAL 68|KANAL FIRAT|KANAL URFA|KANAL V|KARADENIZ|EGE|MELTEM|CAY TV|OLAY TV|TIVI 6|TV 41|TV 42|TV 52|TV 264)\b" }
        };

        foreach (var rule in rules)
        {
            if (Regex.IsMatch(s, rule.Value, RegexOptions.IgnoreCase))
            {
                return rule.Key;
            }
        }

        return "TR GENEL";
    }
}

public class VavooPayload
{
    [JsonPropertyName("language")]
    public string Language { get; set; } = "tr";

    [JsonPropertyName("region")]
    public string Region { get; set; } = "TR";

    [JsonPropertyName("catalogId")]
    public string CatalogId { get; set; } = "iptv";

    [JsonPropertyName("id")]
    public string Id { get; set; } = "iptv";

    [JsonPropertyName("adult")]
    public bool Adult { get; set; } = false;

    [JsonPropertyName("search")]
    public string Search { get; set; } = "";

    [JsonPropertyName("sort")]
    public string Sort { get; set; } = "name";

    [JsonPropertyName("filter")]
    public object Filter { get; set; } = new object();

    [JsonPropertyName("clientVersion")]
    public string ClientVersion { get; set; } = "3.0.2";

    [JsonPropertyName("cursor")]
    public object? Cursor { get; set; }
}

public class VavooCatalogResponse
{
    [JsonPropertyName("items")]
    public List<VavooCatalogItem>? Items { get; set; }

    [JsonPropertyName("nextCursor")]
    public object? NextCursor { get; set; }
}

public class VavooCatalogItem
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("group")]
    public string? Group { get; set; }
}
