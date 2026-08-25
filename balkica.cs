using System;
using System.Collections.Generic;
using System.IO;
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
                    Sort = "",
                    Filter = new VavooFilter { Group = "Turkey" },
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
                    Console.WriteLine($"[HATA] Bağlantı hatası: {ex.Message}");
                    break;
                }

                if (response.IsSuccessStatusCode)
                {
                    string rawResponse = await response.Content.ReadAsStringAsync();

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

                    Console.WriteLine($"[DEBUG] Sayfa {pageCount}: {items.Count} kanal alındı.");

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

                        string logo = !string.IsNullOrEmpty(item.Logo) ? $" tvg-logo=\"{item.Logo}\"" : "";

                        string proxy = WORKER_PROXIES[proxyIndex];
                        proxyIndex = (proxyIndex + 1) % proxyCount;

                        string proxiedUrl = $"{proxy.TrimEnd('/')}/?url={Uri.EscapeDataString(item.Url)}&master&transport=http&.m3u8";
                        output.AppendLine($"#EXTINF:-1 group-title=\"{group}\"{logo},{cleanName}");
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
            Console.WriteLine($"[BAŞARILI] Dosya yazıldı: {outputPath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[KRİTİK HATA] {ex.Message}\n{ex.StackTrace}");
            Environment.Exit(1);
        }
    }

    private static string CleanChannelName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Bilinmeyen Kanal";

        string s = name;
        // 1. Baştaki "4K TR:", "TR:", "4K TR :" gibi ifadeleri kaldırır
        s = Regex.Replace(s, @"^\s*(?:4K\s*)?TR\s*:\s*", "", RegexOptions.IgnoreCase);
        // 2. Sondaki veya kelime aralarındaki .b, .c, .s gibi nokta uzantılarını kaldırır
        s = Regex.Replace(s, @"\s*\.[bcs]\b", "", RegexOptions.IgnoreCase);
        // 3. Çözünürlük ve yayın kalitesi etiketlerini temizler
        s = Regex.Replace(s, @"\s+(?:4K|UHD|FHD|HD\+|HD|SD|HEVC|RAW|H265|H\.265|FEED)(?=\s|$)", "", RegexOptions.IgnoreCase);
        // 4. Fazla boşlukları temizler
        s = Regex.Replace(s, @"\s+", " ");

        return s.Trim();
    }

    private static string NormalizeForCategory(string name)
    {
        string s = CleanChannelName(name);

        string[] search = new string[] 
        { 
            @"\bT RK\b", @"\bT RKIYEM\b", @"\bBENG\b", @"\bBENGT\b", @"\bAK T\b", 
            @"\bS NEMA\b", @"\bM N KA\b", @"\bOCUK\b", @"\bM Z K\b", @"\bS ZC\b", 
            @"\bSZC\b", @"\bLKE\b", @"\bYE IL AM\b", @"\bYE IL[ ]?CAM\b", @"\bT[ÜU]RK\b" 
        };

        string[] replace = new string[] 
        { 
            "TURK", "TURKIYEM", "BENGU", "BENGUT", "AKIT", 
            "SINEMA", "MINIKA", "COCUK", "MUZIK", "SOZCU", 
            "SOZCU", "ULKE", "YESILCAM", "YESILCAM", "TURK" 
        };

        for (int i = 0; i < search.Length; i++)
        {
            s = Regex.Replace(s, search[i], replace[i], RegexOptions.IgnoreCase);
        }

        return s;
    }

    private static string CategorizeChannel(string name)
    {
        string s = NormalizeForCategory(name);

        var rules = new Dictionary<string, string>
        {
            { "TR Radyo", @"\b(RADIO|RADYO)\b|\b(FM|MBAT FM|EFKAR FM|FMTV|F ?M)\b(?!\s*TV)|POWERTURK|POWER FM|SHOW RADYO|ALEM (?:FM|RADYO)|BABA RADYO|KRAL POP RADYO|PAL STATION|X NOSTALJI|RADIO ROCK|STANBUL FM" },
            { "TR Çocuk", @"CARTOON|BOOMERANG|DISNEY|NICK(?:ELODEON|TOONS|JR|JUNIOR|\b)|BABY ?TV|BABYTV|M[İI]?N ?KA|MINIKA|POKEMON|POKÉMON|ANIMATION|ANIMASYON|TRT ?[ÇC]?OCUK|OCUK HD|\bCOCUK\b|\b[ÇC]OCUK\b|BEN ?10|ANGRY BIRDS|CAILLOU|PEPPA|PEPE|HEIDI|SIRINLER|TOM & JERRY|S[ÜU]NGER|SPIDERMAN|BARBIE|PIJAMA|PIRIL|RAFADAN|KELOGLAN|KUKULI|KUKILI|KOSTEBEK|CHICKY|BOOBA|WAKFU|GABBY|TAYO|NILOYA|PISI|LEYLEK|MASAL|CANIM KARDESIM|ADIBESA|MOMO|ALVIN|VIKINGLER|TRANSFORMERS|TROL AVCILARI|SMART COCUK|ILAHI COCUK|CILGIN ORMAN|KRAL SAKIR|SERCE KUS|ITFAYECI SAM|MUFFETIS|MAYMUNLAR|ELIF VE|ELIFIN|MIMOCAN|HAPSUU|RUYA TRENI|MASA KOCAAYI|PAK PIRPIR|LIMON ZEYTIN|GONCA TV|NASREDDIN|SEKER HOCA|SEVIMLI DOSTLAR|PAW PETROL|OSCAR COLLERDE|SL NILOYA|CBEEBIES|DUCK TV|JIM ?JAM|ENGLISH CLUB TV|EBA TV|TAV[SŞ]AN|PATRON BEBEK|D[İI]YARI|BAHA\b|SEF ROKKA|BULMACA KULESI|AKILLI TAV[SŞ]AN|AKLILI|CANIM KARDESIM|DA VINC KIDS|DA VINCI KIDS|DINAMIK ANIMASYON|DREAM ANIMASYON|MAX ANIMASYON|ENO ANIMASYON|BEST ANIMASYON|YILDIZ KIZ|KONU[SŞ]AN TOM|JURASSIC WORLD|MONTAG" },
            { "TR Belgesel", @"DISCOVERY|NATIONAL GEOGRAPHIC|NAT ?GEO|\bHISTORY\b|ANIMAL PLANET|DA VINCI(?! KIDS)|VIASAT|BBC EARTH|LOVE NATURE|TRT BELGESEL|EPIC DRAMA|TARIH TV|TARIM TV|TGRT BELGESEL|INVESTIGATION|DMAX|DOCUBOX|DOCU SCREEN|SCIENCE|\bIZ TV\b|YABAN|OUTDOOR|CHASSE|ANIMAUX|AGRO TV|CIFTCI TV|REDBULL TV|\bTLC\b" },
            { "TR Spor", @"BEIN SPO[RT]{0,3}S?|\bBEIN 1\b|S[- ]?SPORTS?|\bS SPORT\b|SPOR SMART|EUROSPORT|\bNBA\b|TJK TV|TIVIBU ?SPOR|TIVIBUSPOR|TRT SPOR|TABII SPOR|EXXEN SPO[RT]?|\bHT SPOR\b|EKOL SPOR|SPORTS TV|IDMAN TV|GALATASARAY TV|\bFB TV\b|\bGS TV\b|SARAN SPORT|SMART SPOR|\bSPOR\b|\bSPORT\b" },
            { "TR Film", @"SINEMA|S[İI]NEMA|S NEMA|CINEMA|SINEMAX|SINEVIZYON|\bMOVIES?\b|MOVIEMAX|MOVIESMART|BEIN MOVIES|BEIN BOX|BOX OFFICE|\bFX\b|FX HD|YESILCAM|YE ?I ?L ?[ÇC] ?AM|YE ?I ?L ?AM|YEŞ?[İI]LC?AM|GLOBAL BOX|PROTURK|FIX CINEMA|KINGBOX|ARENA BOX|SHOWMAX|SHOW MAX|REAL BOX|SMART BOX|BEST (?:AKSIYON|BILIMKURGU|DRAM|HABABAM|IMBD|KOMEDI|KORKU|LOCA|NETFLIX|SALON|SAVAS|TURK|WESTERN|YESILCAM)|MAX (?:007|AKSIYON|GOLD|ORJINAL|PREMIER|STAR WARS|TURK|VIZYON|WESTERN)|DINAMIK (?:AKSIYON|BILIMKURGU|DRAM|IMBD|KOMEDI|KORKU|TURK|VIZYON|WESTERN)|DREAM (?:AKSIYON|BEIN OFFICE|BOX|DRAM|KEMAL|KOMEDI|KORKU|LOCA|NETFLIX|SAVAS|WESTERN)|ULTRA (?:AKSIYON|BILIMKURGU|IMBD|KEMAL|KOMEDI|KORKU|TURK)|ENO (?:AKSIYON|VIZYON|WESTERN)|\bLOCA\b|\bSALON\b|\bVIZYON\b|AKSIYON|AKS[İIY]?YON|AKS YON|KOMED[İI]|\bKORKU\b|\bDRAM\b|WESTERN|BILIM ?KURGU|\bSAVAS\b|\bIMBD\b|\bIMDB\b|\bFILM\b|FILMBOX|HORROR|OSCAR|KEMAL SUNAL|\b007\b|\bCINE ?1\b|SIFIR TV|SON C BOOM|\bYERL[İI]\b|SPIDERMAN(?! TV)|ARENA BOX|MOVIE SMART|\bM ?T[UÜ]RK TV\b|\bM TURK TV\b|\bM T RK TV\b" },
            { "TR Dizi", @"SER[İI]ES|\bDIZI\b|BEIN SERIES|D[İI]Z[İI] ?SMART|DIZISMART" },
            { "TR Müzik", @"POWER T[UÜ]RK|POWER ?TV|POWERTURK|POWER (?:DANCE|LOVE|HD)|\bPOWER\b|KRAL POP|KRAL ?TV|\bKRAL\b|TRT M[UÜ]?Z[İI]?K|TRT MUZIK|NR ?1|NUMBER ?1|NUMBER ONE|DAMAR|ARABESK|AKUS ?T[İI]K|AHMET KAYA|IBRAHIM ERKAL|IBRAHIM TATLISES|\bTATLISES\b|ZERRIN OZER|SEZEN AKSU|TARKAN|SELDA BAGCAN|CENGIZ KURTOGLU|MAHSUN KIRMIZIGUL|MUSLUM GURSES|YILDIZ TILBE|FERDI TAYFUR|DURSUN AL|MTV LIVE|VINTAGE MUSIC|RETRO T ?RK|RETRO TURK|T[UÜ]?RK ?E POP|T RK E POP|T RK E KLASIK|SLOW KARADENIZ|\bSLOW\b|\bZARA\b|\bSONER ARICA\b|M[UÜ]Z[İI]K|\bFM TV\b|\bFMTV\b|REDBOX" },
            { "TR Haber", @"\bHABER\b|\bNEWS\b|BLOOMBERG|\bCNN\b|EKOTURK|\bEKO ?T[UÜ]RK\b|\bEKOL\b|A ?PARA|APARA|PARANIN|HALK TV|TELE ?1|SOZCU|S ZC|\bSZC\b|BENGU ?T[UÜ]RK|BENGUTURK|TRT WORLD|\bDHA\b|LIDER HABER|FLASH HABER|MEDYA HABER|GLOBAL HABER|TRABZON HABER|BEIN SPORTS HABER|T[UÜ]RKHABER|HABERT[UÜ]RK|HABERT RK|\bARTI TV\b" },
            { "TR Dini", @"D[İI]YANET|\bAK[İIY]?T\b|MEHTAP|H[İI]LAL|KUDUS|KUDÜS|KUD S|SEMERKAND|LALEGUL|LÂLEGÜL|L[AÂ]LEG[UÜ]L|MERCAN TV|VUSLAT|KARDELEN|DIYAR TV|\bDOST TV\b|\bYOL TV\b|\bKANAL 7\b|HAYAT|HAYIRLI|HZ MERYEM|HZ OMER|HZ YUSUF|MAM EBU|ASHABI KEHF|HASAN VE HUSEYIN|SAT ?7 T[UÜ]RK|TVNET|TRT DIYANET|\bTV ?5\b|\bTV5\b|REHBER|ILAHI|ILKE TV|MESAJ TV|SURELER|T[UÜ]RK ?E MEAL|DURSUN AL ERZINCANLI|YUNUS EMRE|CEM TV|BARBAROS TV|ASLAN TV|TYT TURK|SATRAN[ÇC]|FASIL" },
            { "TR Yaşam", @"24 KITCHEN|GURME|BEIN GURME|LIFESTYLE|\bLIFE TV\b|FASHION|WM TV|EGE ILE GAGA|24 RAW|\bTVEM\b|\bTV EM\b|AUTOMOTO|LINE TV|BILGILENDIRME|WOMAN|TELEGRAM" },
            { "TR Ulusal", @"^24$|\bTRT\b|\bTRT 1\b|\bTRT ?2\b|TRT2|\bTRT 3\b|TRT AVAZ|TRT T[UÜ]RK|TRT TURK|TRT KURD[İI]?|TRT WORLD|TRT 4K|TRT EBA|\bKANAL D\b|\bATV\b|ATV AVRUPA|ATV EUROPA|STAR TV|\bSTAR\b|STAR HD|SHOW TV|SHOW T[UÜ]RK|\bSHOW\b|\bFOX\b|NOW ?TV|\bNOW\b|TV ?8|TV8[.,]5|BEYAZ TV|BEYAZ HD|\bBEYAZ\b|\b360\b|24 TV|\bA2\b|A HABER|A NEWS|A PARA|A SPOR|TV ?100|TV ?4|FLASH TV|TEVE ?2|TEVE2|CNN T[UÜ]RK|CNN TURK|\bKRT\b|ULUSAL KANAL|DREAM T[UÜ]RK|DREAM TURK|\bDREAM TV\b|\bBRT ?[0-9]|\bBRTV\b|EURO ?D|EURO ?STAR|\bNTV\b|EXXEN TV|TIVI ?T[UÜ]RK|TABII|OLAY T[UÜ]RK|OLAY TURK|24 HD|24 HABER|24 KITCHEN|LKE ?TV|[UÜ]LKE ?TV|ULKE ?TV|ULKETV|TV DEN|TVDEN|KANAL AVRUPA|KANAL 7 (?:AVRUPA|EUROPA)|LKE TV|EURO D|EURO STAR|SHOW TV EUROPA|BENGU ?T[UÜ]RK|BENGU TURK|BENGUTURK|TGRT EU|D ?[ĞG] ?N TV|\bTBMM\b|TV NET|\bTV 1\b|TVO TV|BEIN IZ|\bMAX\b" },
            { "TR Yerel", @"ADANA|AD[İI]YAMAN|AFYON|AKSARAY|ALANYA|ANAKKALE|\bANKARA\b|ANKA TV|ANKARA T[UÜ]RKIYEM|ANLIURFA|ANTALYA|\bBURSA\b|ELAZIG|ERCIS|ERZURUM|ESK[İI]SEHIR|ESK EH R|\bES TV\b|\bER TV\b|ETV KAYSERI|ETV MANISA|GAZIANTEP|\bICEL\b|K[İI]MARAS|KAHRAMANMARA|K MARAS|KAYSERI|KOCAELI|KON TV|KONYA|MALATYA|MERSIN|ORDU|ALTAS TV|SIVAS|TRABZON|TUNCELI|DERSIM|\bURFA\b|IZMIR TV|TON TV|KIBRIS|EDIRNE|DENIZLI|\bKAY TV\b|KENT T[UÜ]RK|KENT T RK|HUNAT|\bOBB\b|KANAL 12|KANAL 15|KANAL 23|KANAL 24|KANAL 26|KANAL 3\b|KANAL 32|KANAL 33|KANAL 34|KANAL 360|KANAL 42|KANAL 58|KANAL 68|KANAL FIRAT|KANAL URFA|KANAL V\b|\bKANAL Z\b|KANAL T\b|KANAL HAYAT|KANAL 68|KARADENIZ|GUNEYDOGU|GÜNEYDOĞU|\bEGE\b|MELTEM|CAY TV|TEK RUMEL|YENI KOCAELI|OLAY TV|\bGRT\b|SUN RTV|SUN TV|\bK[ÖO]Y TV\b|IZMIR|TIVI 6|TV 41|TV 42|TV 52|TV 264|KOZA TV|MC EU|MERCAN|KADIRGA|\bFANATIK\b|AS TV|ISVI|GURBET24|T\.A\.Y|TAY TV|\bTAY\b|\bTMB\b|AV TV|MAVI KARADENIZ|EGE ILE GAGA|GAZIANTEP GRT|VIYANA TV|LUYS|EDESSA|BIR TV|ANA[DK]OLU|B[İI]R TV|D[İI]YAR|ERTV|HRT|SIVAS|VIZYON 58|ADA TV|CAN TV|DEHA|SIFIR|EKIN T[UÜ]RK|AFROTURK|ARAS|ARKADAG|VATAN|D[ÖO]RU|AKSU TV|KARE TV|ON 4|ON 6|PAMUKKALE|UCANKUS|64 KARE|DENIZ POSTASI" }
        };

        foreach (var rule in rules)
        {
            if (Regex.IsMatch(s, rule.Value, RegexOptions.IgnoreCase))
            {
                return rule.Key;
            }
        }

        return "TR Diğer";
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
    public string Sort { get; set; } = "";

    [JsonPropertyName("filter")]
    public VavooFilter Filter { get; set; } = new VavooFilter();

    [JsonPropertyName("clientVersion")]
    public string ClientVersion { get; set; } = "3.0.2";

    [JsonPropertyName("cursor")]
    public object? Cursor { get; set; }
}

public class VavooFilter
{
    [JsonPropertyName("group")]
    public string Group { get; set; } = "Turkey";
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

    [JsonPropertyName("logo")]
    public string? Logo { get; set; }
}
