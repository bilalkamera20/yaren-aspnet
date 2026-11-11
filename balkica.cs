using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

class Program
{
    private static readonly string UA = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/147.0.0.0 Safari/537.36";
    private static readonly string CLIENT_VERSION = "3.1.4";
    private static readonly string CATALOG_URL = "https://vavoo.to/mediahubmx-catalog.json";
    private static readonly string OUTPUT_FILE = "nernur.txt";

    private static readonly List<string> FallbackProxies = new List<string>
    {
        "https://1.vavturktv.workers.dev", "https://2.vavturktv.workers.dev", "https://3.vavturktv.workers.dev",
        "https://4.vavturktv.workers.dev", "https://5.vavturktv.workers.dev", "https://6.vavturktv.workers.dev",
        "https://7.vavturktv.workers.dev", "https://8.vavturktv.workers.dev", "https://9.vavturktv.workers.dev",
        "https://10.vavturktv.workers.dev", "https://11.vavturktv.workers.dev", "https://12.vavturktv.workers.dev",
        "https://13.vavturktv.workers.dev", "https://14.vavturktv.workers.dev", "https://15.vavturktv.workers.dev",
        "https://16.vavturktv.workers.dev", "https://17.vavturktv.workers.dev", "https://18.vavturktv.workers.dev",
        "https://19.vavturktv.workers.dev", "https://20.vavturktv.workers.dev", "https://21.vavturktv.workers.dev",
        "https://22.vavturktv.workers.dev", "https://23.vavturktv.workers.dev", "https://24.vavturktv.workers.dev",
        "https://25.vavturktv.workers.dev", "https://nur.bilalkamera20.workers.dev", "https://halil.bilalkamera20.workers.dev",
        "https://adam.bilalkamera20.workers.dev", "https://bilal.bilalkamera20.workers.dev", "https://balkica.bilalkamera20.workers.dev",
        "https://hmeb.bilalkamera20.workers.dev", "https://nernur.bilalkamera20.workers.dev", "https://vav20.bilalkamera20.workers.dev",
        "https://vavoo-iptv-proxy.bilalkamera20.workers.dev", "https://yaren.bilalkamera20.workers.dev", "https://denem.bilalkamera20.workers.dev",
        "https://20.bilalkamera20.workers.dev", "https://yw.bilalkamera20.workers.dev", "https://fb.bilalkamera20.workers.dev",
        "https://ner.bilalkamera20.workers.dev"
    };

    private static readonly string[] PingUrls = {
        "https://www.vypn.net/api/app/ping",
        "https://www.vavoo.tv/api/app/ping",
        "https://cache.vypn.net/api/app/ping"
    };

    private static readonly string[] DefaultGroups = { "Turkey" };
    private static readonly HttpClient HttpClient = new HttpClient(new HttpClientHandler { ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true }) { Timeout = TimeSpan.FromSeconds(20) };

    static async Task Main(string[] args)
    {
        try
        {
            List<string> workerProxies = ParseEnvProxies();
            if (workerProxies.Count == 0)
            {
                workerProxies = FallbackProxies;
            }

            Console.WriteLine("İmza alınıyor...");
            string signature = await GetSignatureAsync();
            if (string.IsNullOrEmpty(signature))
            {
                Console.WriteLine("Hata: İmza alınamadı!");
                Environment.Exit(1);
            }

            List<Dictionary<string, string>> allChannels = new List<Dictionary<string, string>>();
            foreach (var g in DefaultGroups)
            {
                var channels = await FetchCatalogGroupAsync(g, signature);
                allChannels.AddRange(channels);
            }

            if (allChannels.Count == 0)
            {
                Console.WriteLine("Hata: Kanal bulunamadı!");
                Environment.Exit(1);
            }

            string m3uContent = BuildM3u(allChannels, workerProxies);
            File.WriteAllText(OUTPUT_FILE, m3uContent, Encoding.UTF8);
            Console.WriteLine($"Başarılı! {allChannels.Count} kanal işlendi ve '{OUTPUT_FILE}' dosyasına kaydedildi.");
        }
        catch (Exception ex)
        {
            Console.WriteLine("KRİTİK HATA OLUŞTU:");
            Console.WriteLine(ex.Message);
            Console.WriteLine(ex.StackTrace);
            Environment.Exit(1);
        }
    }

    private static List<string> ParseEnvProxies()
    {
        string envProxy = Environment.GetEnvironmentVariable("PROXY_BASE");
        if (string.IsNullOrEmpty(envProxy)) return new List<string>();

        string[] rawList = Regex.Split(envProxy, "[,;\r\n]+");
        List<string> proxies = new List<string>();
        foreach (var item in rawList)
        {
            string trimmed = item.Trim();
            if (!string.IsNullOrEmpty(trimmed))
            {
                proxies.Add(trimmed.TrimEnd('/'));
            }
        }
        return proxies;
    }

    private static async Task<Dictionary<string, object>> HttpPostAsync(string url, object payload, Dictionary<string, string> extraHeaders = null)
    {
        try
        {
            var jsonPayload = JsonConvert.SerializeObject(payload);
            var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
            };

            request.Headers.UserAgent.ParseAdd(UA);
            request.Headers.Accept.ParseAdd("*/*");

            if (extraHeaders != null)
            {
                foreach (var header in extraHeaders)
                {
                    request.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }

            var response = await HttpClient.SendAsync(request);
            string body = await response.Content.ReadAsStringAsync();
            int code = (int)response.StatusCode;

            return new Dictionary<string, object>
            {
                { "ok", response.IsSuccessStatusCode },
                { "body", body },
                { "code", code }
            };
        }
        catch (Exception ex)
        {
            return new Dictionary<string, object> { { "ok", false }, { "body", null }, { "code", 0 }, { "err", ex.Message } };
        }
    }

    private static async Task<string> GetSignatureAsync()
    {
        string uid = Guid.NewGuid().ToString("N");
        long ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var payload = new Dictionary<string, object>
        {
            { "reason", "app-focus" }, { "locale", "de" }, { "theme", "dark" },
            { "metadata", new Dictionary<string, object> {
                { "device", new Dictionary<string, object> { { "type", "desktop" }, { "uniqueId", uid } } },
                { "os", new Dictionary<string, object> { { "name", "win32" }, { "version", "Windows 10 Pro" }, { "abis", new[] { "x64" } }, { "host", "PC" } } },
                { "app", new Dictionary<string, object> { { "platform", "electron" } } },
                { "version", new Dictionary<string, object> { { "package", "net.vypn.app" }, { "binary", "3.1.0" }, { "js", "3.1.0" } } }
            }},
            { "appFocusTime", 0 }, { "playerActive", false }, { "playDuration", 0 },
            { "devMode", false }, { "hasAddon", true }, { "castConnected", false },
            { "package", "net.vypn.app" }, { "version", "3.1.0" }, { "process", "app" },
            { "firstAppStart", ts }, { "lastAppStart", ts }, { "ipLocation", null },
            { "adblockEnabled", true },
            { "proxy", new Dictionary<string, object> { { "supported", new[] { "ss" } }, { "engine", "Mu" }, { "enabled", false }, { "autoServer", true } } },
            { "iap", new Dictionary<string, object> { { "supported", false } } }
        };

        foreach (var u in PingUrls)
        {
            var r = await HttpPostAsync(u, payload);
            if ((bool)r["ok"])
            {
                try
                {
                    JObject d = JObject.Parse((string)r["body"]);
                    string sig = (string)d["sig"] ?? (string)d["addonSig"] ?? (string)d["signature"] ?? (string)d["token"];
                    if (!string.IsNullOrEmpty(sig)) return sig;
                }
                catch { continue; }
            }
        }
        return null;
    }

    private static string CleanChannelName(string name)
    {
        if (string.IsNullOrEmpty(name)) return "Bilinmeyen Kanal";

        string s = name;
        s = Regex.Replace(s, @"^\s*(?:4K\s*)?TR\s*:\s*", "", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\s*\.[bcs]\b", "", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\s+(?:4K|UHD|FHD|HD\+|HD|SD|HEVC|RAW|H265|H\.265|FEED)(?=\s|$)", "", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"S\s*[\x00-\x1F\x7F-\xFF\?\s]*NEMA", "SINEMA", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"DREAM\s*T\s*R\s*K", "DREAM TURK", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"MINICA", "MINIKA", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\s+", " ");

        return s.Trim();
    }

    private static string NormalizeForCategory(string name)
    {
        string s = CleanChannelName(name);

        s = s.Replace('Ç', 'C').Replace('ç', 'c')
             .Replace('Ğ', 'G').Replace('ğ', 'g')
             .Replace('İ', 'I').Replace('ı', 'i')
             .Replace('Ö', 'O').Replace('ö', 'o')
             .Replace('Ş', 'S').Replace('ş', 's')
             .Replace('Ü', 'U').Replace('ü', 'u');

        string[] search = { @"\bT RK\b", @"\bT RKIYEM\b", @"\bBENG\b", @"\bBENGT\b", @"\bAK T\b", @"\bS\s*NEMA\b", @"\bM N KA\b", @"\bOCUK\b", @"\bM Z K\b", @"\bS ZC\b", @"\bSZC\b", @"\bLKE\b", @"\bYE IL AM\b", @"\bYE IL[ ]?CAM\b", @"\bT[ÜU]RK\b" };
        string[] replace = { "TURK", "TURKIYEM", "BENGU", "BENGUT", "AKIT", "SINEMA", "MINIKA", "COCUK", "MUZIK", "SOZCU", "SOZCU", "ULKE", "YESILCAM", "YESILCAM", "TURK" };

        for (int i = 0; i < search.Length; i++)
        {
            s = Regex.Replace(s, search[i], replace[i], RegexOptions.IgnoreCase);
        }
        return s;
    }

    private static string CategorizeChannel(string name)
    {
        string s = NormalizeForCategory(name);

        if (Regex.IsMatch(s, @"KARADENIZ\s*BRT|TRT\s*MUZIK", RegexOptions.IgnoreCase))
        {
            return "TR Diğer";
        }

        var rules = new Dictionary<string, string>
        {
            { "TR Spor", @"BEIN SPO[RT]{0,3}S?|\bBEIN 1\b|S[- ]?SPORTS?|\bS SPORT\b|SPOR SMART|EUROSPORT|\bNBA\b|TJK TV|TIVIBU ?SPOR|TIVIBUSPOR|TRT SPOR|TABII SPOR|EXXEN SPO[RT]?|\bHT SPOR\b|EKOL SPOR|SPORTS TV|IDMAN TV|GALATASARAY TV|\bFB TV\b|\bGS TV\b|SARAN SPORT|SMART SPOR|\bSPOR\b|\bSPORT\b" },
            { "TR Radyo", @"\b(RADIO|RADYO)\b|\b(FM|MBAT FM|EFKAR FM|FMTV|F ?M)\b(?!\s*TV)|POWERTURK|POWER FM|SHOW RADYO|ALEM (?:FM|RADYO)|BABA RADYO|KRAL POP RADYO|PAL STATION|X NOSTALJI|RADIO ROCK|STANBUL FM" },
            { "TR Çocuk", @"CARTOON|BOOMERANG|DISNEY|NICK(?:ELODEON|TOONS|JR|JUNIOR|\b)|BABY ?TV|BABYTV|MINIKA|MINICA|POKEMON|ANIMATION|ANIMASYON|TRT ?COCUK|COCUK HD|\bCOCUK\b|ANKA|BEN ?10|ANGRY BIRDS|CAILLOU|PEPPA|PEPE|HEIDI|SIRINLER|TOM & JERRY|SUNGER|SUNGER\s*BOB|SPIDERMAN|BARBIE|PIJAMA|PIRIL|RAFADAN|KELOGLAN|KUKULI|KUKILI|KOSTEBEK|CHICKY|BOOBA|WAKFU|GABBY|TAYO|NILOYA|PISI|LEYLEK|MASAL|CANIM KARDESIM|ADIBESA|MOMO|ALVIN|VIKINGLER|TRANSFORMERS|TROL AVCILARI|SMART\s*COCUK|COCUK\s*SMART|ILAHI COCUK|CILGIN ORMAN|KRAL SAKIR|SERCE KUS|ITFAYECI SAM|MUFFETIS|MAYMUNLAR|ELIF VE|ELIFIN|MIMOCAN|HAPSUU|RUYA TRENI|MASA KOCAAYI|PAK PIRPIR|LIMON ZEYTIN|GONCA TV|NASREDDIN|SEKER HOCA|SEVIMLI DOSTLAR|PAW PETROL|OSCAR COLLERDE|SL NILOYA|CBEEBIES|DUCK TV|JIM ?JAM|ENGLISH CLUB TV|EBA TV|TAVSAN|PATRON BEBEK|DIYARI|BAHA\b|SEF ROKKA|BULMACA KULESI|AKILLI TAVSAN|AKLILI|DA VINC KIDS|DA VINCI KIDS|DINAMIK ANIMASYON|BEST ANIMASYON|YILDIZ KIZ|KONUSAN TOM|JURASSIC WORLD|MONTAG|64\s*KARE" },
            { "TR Belgesel", @"DISCOVERY|NATIONAL GEOGRAPHIC|NAT ?GEO|\bHISTORY\b|ANIMAL PLANET|DA VINCI(?! KIDS)|VIASAT|BBC EARTH|LOVE NATURE|TRT BELGESEL|EPIC DRAMA|TARIH TV|TARIM TV|TGRT BELGESEL|INVESTIGATION|DMAX|DOCUBOX|DOCU SCREEN|SCIENCE|\bIZ TV\b|YABAN|OUTDOOR|CHASSE|ANIMAUX|AGRO TV|CIFTCI TV|REDBULL TV|\bTLC\b" },
            { "TR Yaşam", @"24 KITCHEN|GURME|BEIN GURME|LIFESTYLE|\bLIFE TV\b|FASHION|WM TV|EGE ILE GAGA|24 RAW|\bTVEM\b|\bTV EM\b|AUTOMOTO|LINE TV|BILGILENDIRME|WOMAN|TELEGRAM" },
            { "TR Haber", @"\b24\s*TV\b|\bTV\s*24\b|\b24\s*HABER\b|^24$|\bHABER\b|\bNEWS\b|BLOOMBERG|\bCNN\b|EKOTURK|\bEKO ?TURK\b|\bEKOL\b|A ?PARA|APARA|PARANIN|HALK TV|TELE ?1|SOZCU|\bSZC\b|BENGU|BENGUTURK|TRT WORLD|TRT HABER|\bDHA\b|LIDER HABER|FLASH|FLASH HABER|MEDYA HABER|GLOBAL HABER|TRABZON HABER|BEIN SPORTS HABER|TURKHABER|HABERTURK|\bARTI TV\b|\bAKIT\b|TVNET|TV NET|MELTEM|A HABER|ULKE|A NEWS|KRT|ULUSAL" },
            { "TR Film", @"DREAM|DINAMIK\s*TURK|SINEMA|CINEMA|SINEMAX|SINEVIZYON|\bMOVIES?\b|MOVIEMAX|MOVIESMART|BEIN MOVIES|BEIN BOX|BOX OFFICE|\bFX\b|FX HD|YESILCAM|GLOBAL BOX|PROTURK|FIX CINEMA|KINGBOX|ARENA BOX|SHOWMAX|SHOW MAX|REAL BOX|SMART BOX|BEST (?:AKSIYON|BILIMKURGU|DRAM|HABABAM|IMBD|KOMEDI|KORKU|LOCA|NETFLIX|SALON|SAVAS|TURK|WESTERN|YESILCAM)|MAX|\bLOCA\b|\bSALON\b|\bVIZYON\b|AKSIYON|KOMEDI|\bKORKU\b|\bDRAM\b|WESTERN|BILIM ?KURGU|\bSAVAS\b|\bIMBD\b|\bIMDB\b|\bFILM\b|FILMBOX|HORROR|OSCAR|KEMAL SUNAL|\b007\b|\bCINE ?1\b|SIFIR TV|SON C BOOM|\bYERLI\b|SPIDERMAN(?! TV)|ARENA BOX|MOVIE SMART|\bMTURK TV\b" },
            { "TR Ulusal", @"\bTRT\b|\bTRT 1\b|\bTRT ?2\b|TRT2|\bTRT 3\b|TRT AVAZ|TRT TURK|TRT KURDI|TRT 4K|TRT EBA|\bKANAL D\b|\bATV\b|ATV AVRUPA|ATV EUROPA|STAR TV|\bSTAR\b|STAR HD|SHOW TV|SHOW TURK|\bSHOW\b|\bFOX\b|NOW ?TV|\bNOW\b|TV ?8|TV8[.,]5|BEYAZ TV|BEYAZ HD|\bBEYAZ\b|\b360\b|\bA2\b|TV ?100|TV ?4|TEVE ?2|TEVE2|CNN TURK|\bBRT ?[0-9]|\bBRTV\b|EURO ?D|EURO ?STAR|\bNTV\b|EXXEN TV|TIVITURK|TABII|OLAY TURK|KANAL AVRUPA|\bKANAL 7\b|KANAL 7 (?:AVRUPA|EUROPA)|EURO D|EURO STAR|SHOW TV EUROPA|TGRT EU|DUGUN TV|\bTBMM\b|\bTV 1\b|TVO TV|BEIN IZ" },
            { "TR Dizi", @"SERIES|\bDIZI\b|BEIN SERIES|DIZISMART" },
            { "TR Müzik", @"POWER TURK|POWER ?TV|POWERTURK|POWER (?:DANCE|LOVE|HD)|\bPOWER\b|KRAL POP|KRAL ?TV|\bKRAL\b|NR ?1|NUMBER ?1|NUMBER ONE|DAMAR|ARABESK|AKUSTIK|AHMET KAYA|IBRAHIM ERKAL|IBRAHIM TATLISES|\bTATLISES\b|ZERRIN OZER|SEZEN AKSU|TARKAN|SELDA BAGCAN|CENGIZ KURTOGLU|MAHSUN KIRMIZIGUL|MUSLUM GURSES|YILDIZ TILBE|FERDI TAYFUR|DURSUN AL|MTV LIVE|VINTAGE MUSIC|RETRO TURK|TURKCE POP|TURKCE KLASIK|SLOW KARADENIZ|\bSLOW\b|\bZARA\b|\bSONER ARICA\b|MUZIK|\bFM TV\b|\bFMTV\b|REDBOX" },
            { "TR Dini", @"DIYANET|MEHTAP|HILAL|KUDUS|SEMERKAND|LALEGUL|MERCAN TV|VUSLAT|KARDELEN|DIYAR TV|\bDOST TV\b|\bYOL TV\b|HAYAT|HAYIRLI|HZ MERYEM|HZ OMER|HZ YUSUF|MAM EBU|ASHABI KEHF|HASAN VE HUSEYIN|SAT ?7 TURK|TRT DIYANET|\bTV ?5\b|\bTV5\b|REHBER|ILAHI|ILKE TV|MESAJ TV|SURELER|TURKCE MEAL|DURSUN AL ERZINCANLI|YUNUS EMRE|CEM TV|BARBAROS TV|ASLAN TV|TYT TURK|SATRANC|FASIL" },
            { "TR Yerel", @"TVDEN|TV DEN|ADANA|ADIYAMAN|AFYON|AKSARAY|ALANYA|ANAKKALE|\bANKARA\b|ANKA TV|ANKARA TURKIYEM|SANLIURFA|ANTALYA|\bBURSA\b|ELAZIG|ERCIS|ERZURUM|ESKISEHIR|\bES TV\b|\bER TV\b|ETV KAYSERI|ETV MANISA|GAZIANTEP|\bICEL\b|KAHRAMANMARAS|KAYSERI|KOCAELI|KON TV|KONYA|MALATYA|MERSIN|ORDU|ALTAS TV|SIVAS|TRABZON|TUNCELI|DERSIM|\bURFA\b|IZMIR TV|TON TV|KIBRIS|EDIRNE|DENIZLI|\bKAY TV\b|KENT TURK|HUNAT|\bOBB\b|KANAL 12|KANAL 15|KANAL 23|KANAL 24|KANAL 26|KANAL 3\b|KANAL 32|KANAL 33|KANAL 34|KANAL 360|KANAL 42|KANAL 58|KANAL 68|KANAL FIRAT|KANAL URFA|KANAL V\b|\bKANAL Z\b|KANAL T\b|KANAL HAYAT|GUNEYDOGU|\bEGE\b|TEK RUMELI|YENI KOCAELI|OLAY TV|\bGRT\b|SUN RTV|SUN TV|\bKOY TV\b|IZMIR|TIVI 6|TV 41|TV 42|TV 52|TV 264|KOZA TV|MC EU|MERCAN|KADIRGA|\bFANATIK\b|AS TV|ISVI|GURBET24|T\.A\.Y|TAY TV|\bTAY\b|\bTMB\b|AV TV|MAVI KARADENIZ|EGE ILE GAGA|GAZIANTEP GRT|VIYANA TV|LUYS|EDESSA|BIR TV|ANADOLU|DIYAR|ERTV|HRT|VIZYON 58|ADA TV|CAN TV|DEHA|SIFIR|EKIN TURK|AFROTURK|ARAS|ARKADAG|VATAN|DORU|AKSU TV|KARE TV|ON 4|ON 6|PAMUKKALE|UCANKUS|DENIZ POSTASI" }
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

    private static async Task<List<Dictionary<string, string>>> FetchCatalogGroupAsync(string group, string signature)
    {
        var channels = new List<Dictionary<string, string>>();
        int cursor = 0;

        for (int page = 0; page < 15; page++)
        {
            var payload = new Dictionary<string, object>
            {
                { "language", "tr" }, { "region", "TR" }, { "catalogId", "iptv" }, { "id", "iptv" },
                { "adult", false }, { "search", "" }, { "sort", "name" },
                { "filter", new Dictionary<string, string> { { "group", group } } },
                { "cursor", cursor }, { "clientVersion", CLIENT_VERSION }
            };

            var r = await HttpPostAsync(CATALOG_URL, payload, new Dictionary<string, string> { { "mediahubmx-signature", signature } });
            if (!(bool)r["ok"]) break;

            JObject data;
            try { data = JObject.Parse((string)r["body"]); } catch { break; }
            if (data == null) break;

            var items = data["items"] as JArray;
            if (items == null || items.Count == 0) break;

            foreach (var itm in items)
            {
                string rawName = (string)itm["name"] ?? "";
                string name = CleanChannelName(rawName);
                if (string.IsNullOrEmpty(name)) name = "Isimsiz Kanal";

                string resolvedGroup = CategorizeChannel(rawName);

                string chid = "";
                if (itm["ids"] is JObject idsObj)
                {
                    chid = (string)idsObj["id"] ?? "";
                }
                if (string.IsNullOrEmpty(chid)) chid = (string)itm["id"] ?? "";

                string url = (string)itm["url"] ?? (string)itm["play"] ?? "";
                if (string.IsNullOrEmpty(url) && !string.IsNullOrEmpty(chid))
                {
                    url = $"https://vavoo.to/vavoo-iptv/play/{chid}";
                }
                else if (!string.IsNullOrEmpty(url) && url.Contains("/play/") && !url.Contains("/vavoo-iptv/"))
                {
                    url = Regex.Replace(url, @"^https?://[^/]+/play/", "https://vavoo.to/vavoo-iptv/play/");
                }
                if (string.IsNullOrEmpty(url)) continue;

                string logo = "";
                if (itm["images"] is JObject imgObj)
                {
                    logo = (string)imgObj["logo"] ?? (string)imgObj["poster"] ?? "";
                }
                if (string.IsNullOrEmpty(logo)) logo = (string)itm["logo"] ?? (string)itm["poster"] ?? "";

                string tvg = "";
                if (itm["ids"] is JObject idsObj2)
                {
                    tvg = (string)idsObj2["tvg"] ?? "";
                }

                channels.Add(new Dictionary<string, string>
                {
                    { "name", name },
                    { "url", url },
                    { "group", resolvedGroup },
                    { "logo", logo },
                    { "tvg_id", tvg }
                });
            }

            var nextCursorToken = data["nextCursor"];
            if (nextCursorToken == null || nextCursorToken.Type == JTokenType.Null)
            {
                break;
            }
            cursor = nextCursorToken.Value<int>();
            if (cursor == 0) break;
        }
        return channels;
    }

    private static string BuildM3u(List<Dictionary<string, string>> channels, List<string> proxies)
    {
        var outList = new List<string>
        {
            "#EXTM3U",
            $"#EXT-X-USER-AGENT:{UA}",
            "#EXT-X-REFERER:https://vavoo.to/",
            "#EXT-X-ORIGIN:https://vavoo.to"
        };

        int proxyCount = proxies.Count;
        int proxyIndex = 0;

        foreach (var ch in channels)
        {
            string logo = !string.IsNullOrEmpty(ch["logo"]) ? $" tvg-logo=\"{ch["logo"]}\"" : "";
            string tvg = !string.IsNullOrEmpty(ch["tvg_id"]) ? $" tvg-id=\"{ch["tvg_id"]}\"" : "";

            outList.Add($"#EXTINF:-1{tvg} tvg-name=\"{ch["name"]}\"{logo} group-title=\"{ch["group"]}\",{ch["name"]}");

            string activeProxy = proxies[proxyIndex % proxyCount];
            proxyIndex++;

            outList.Add($"{activeProxy.TrimEnd('/')}/?url={Uri.EscapeDataString(ch["url"])}&master&transport=http&.m3u8");
        }
        return string.Join("\n", outList) + "\n";
    }
}
