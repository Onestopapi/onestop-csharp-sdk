// DataDome slider: the page answers 403 with `var dd={...rt:'c'}`. Load the
// captcha page, its script and both images, GET the captcha check with the
// generated query string, store the datadome cookie and load the page again.
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using OneStop;

namespace Examples;

public static class DatadomeSlider
{
    private const string apiKey = "YOUR_API_KEY";
    private const string identifier = "YOUR_IDENTIFIER"; // your identifier or account name
    private const string pageUrl = "https://www.example.com/events/example";
    private const string acceptLanguage = "en-GB,en;q=0.9";

    private const string proxy = ""; // your proxy, in the site's country

    private const string GeoOrigin = "https://geo.captcha-delivery.com";

    private static readonly Regex DdObject = new(@"var\s+dd\s*=\s*(\{[^}]*\})");
    private static readonly Regex ChallengePath = new(@"captchaChallengePath: '([^']+)'");
    private static readonly Regex CaptchaScript = new(@"<script[^>]*\ssrc=""([^""]*captcha[^""]*\.js)""");

    private sealed class Challenge
    {
        [JsonPropertyName("rt")] public string Rt { get; set; } = "";
        [JsonPropertyName("cid")] public string Cid { get; set; } = "";
        [JsonPropertyName("hsh")] public string Hash { get; set; } = "";
        [JsonPropertyName("t")] public string T { get; set; } = "";
        [JsonPropertyName("s")] public int S { get; set; }
        [JsonPropertyName("e")] public string E { get; set; } = "";
        [JsonPropertyName("cookie")] public string Cookie { get; set; } = "";
    }

    // CheckReply is the captcha check's JSON reply.
    private sealed class CheckReply
    {
        [JsonPropertyName("cookie")] public string Cookie { get; set; } = "";
    }

    public static async Task Run()
    {
        var hc = new HttpClientWrapper(proxy);
        var (client, ip) = await NewClient();
        var (page, dd) = GetBlockedPage(hc);
        var session = NewSession(client, ip, page.Url);
        var (geoUrl, geo) = GetChallengePage(hc, page, dd);
        await SolveSlider(hc, session, page, geoUrl, geo);
        var status = ReloadPage(hc, page);
        SiteFirewall(hc, pageUrl);
        Console.WriteLine("PASS captcha check cookie, page re-GET " + status);
    }

    private static async Task<(OneStopClient Client, string Ip)> NewClient()
    {
        var client = new OneStopClient(new OneStopOptions
        {
            ApiKey = apiKey,
            Identifier = identifier,
            IpHttpClient = HttpClientWrapper.IpClient(proxy),
        });
        var ip = await client.GetIpAsync(); // through the proxy: the IP the site sees
        return (client, ip);
    }

    private static Session NewSession(OneStopClient client, string ip, string site) => client.Session(new SessionOptions
    {
        Site = site,
        Referer = site,
        UserAgent = HttpClientWrapper.UserAgent,
        AcceptLanguage = acceptLanguage,
        Ip = ip,
    });

    private static (Response Page, Challenge Dd) GetBlockedPage(HttpClientWrapper hc)
    {
        var page = hc.Get(pageUrl, Headers.Navigate(acceptLanguage, ""));
        var match = DdObject.Match(page.Body);
        if (page.Status != 403 || !match.Success)
        {
            throw new Exception($"FAIL page returned {page.Status} without a DataDome challenge");
        }
        var dd = JsonSerializer.Deserialize<Challenge>(match.Groups[1].Value.Replace("'", "\""))!;
        if (dd.Rt != "c" || dd.T == "bv")
        {
            throw new Exception($"FAIL challenge type \"{dd.Rt}\" t=\"{dd.T}\" is not the slider");
        }
        return (page, dd);
    }

    private static (string GeoUrl, Response Geo) GetChallengePage(HttpClientWrapper hc, Response page, Challenge dd)
    {
        var geoUrl = $"{GeoOrigin}/captcha/?initialCid={Uri.EscapeDataString(dd.Cid)}&hash={Uri.EscapeDataString(dd.Hash)}&cid={Uri.EscapeDataString(dd.Cookie)}&t={dd.T}&referer={Uri.EscapeDataString(page.Url)}&s={dd.S}";
        if (dd.E != "")
        {
            geoUrl += "&e=" + Uri.EscapeDataString(dd.E);
        }
        geoUrl += "&dm=cd";

        var origin = new Uri(page.Url).GetLeftPart(UriPartial.Authority);
        var geo = hc.Get(geoUrl, Headers.Frame(acceptLanguage, "cross-site", origin + "/").StorageAccess());
        if (geo.Status != 200 || !ChallengePath.IsMatch(geo.Body))
        {
            throw new Exception($"FAIL captcha page returned {geo.Status} without the slider challenge");
        }
        return (geoUrl, geo);
    }

    private static async Task SolveSlider(HttpClientWrapper hc, Session session, Response page, string geoUrl, Response geo)
    {
        // Without a script tag the bundle is inline and the API reads it from Html.
        var script = "";
        var match = CaptchaScript.Match(geo.Body);
        if (match.Success)
        {
            script = hc.Get(match.Groups[1].Value, Headers.Script(acceptLanguage, "same-site", GeoOrigin + "/").StorageAccess()).Body;
        }

        // The background is captchaChallengePath (.jpg); the puzzle piece is the
        // same path as .frag.png. Both go to the API as base64.
        var path = ChallengePath.Match(geo.Body).Groups[1].Value;
        var images = new List<string>();
        foreach (var p in new[] { path, path.Replace(".jpg", ".frag.png") })
        {
            var image = hc.Get(geo.Resolve(p).AbsoluteUri, Headers.Image(acceptLanguage, GeoOrigin, "same-site", GeoOrigin + "/"));
            if (image.Status != 200)
            {
                throw new Exception($"FAIL captcha image returned {image.Status}");
            }
            images.Add(Convert.ToBase64String(image.Content));
        }

        // Script and Html are the captcha page's; PageResponse is the blocked page.
        var res = await session.DatadomeSliderAsync(new DatadomeSliderCall
        {
            Script = script,
            Html = geo.Body,
            PageResponse = page.Body,
            BackgroundImage = images[0],
            PuzzlePiece = images[1],
        });

        // res.Payload is the generated check query string.
        var check = hc.Get(GeoOrigin + "/captcha/check" + res.Payload, Headers.Post(acceptLanguage, "application/x-www-form-urlencoded; charset=UTF-8", "", geoUrl).StorageAccess());
        var reply = check.Body.StartsWith('{') ? JsonSerializer.Deserialize<CheckReply>(check.Body)! : new CheckReply();
        if (check.Status != 200 || reply.Cookie == "")
        {
            throw new Exception($"FAIL captcha check returned {check.Status}: {check.Body}");
        }
        // reply.Cookie is the datadome Set-Cookie line.
        var jar = new CookieContainer();
        jar.SetCookies(new Uri(page.Url), reply.Cookie);
        var cookie = jar.GetAllCookies()["datadome"]!;
        hc.SetCookie(cookie.Domain, cookie.Name, cookie.Value);
    }

    private static int ReloadPage(HttpClientWrapper hc, Response page)
    {
        var again = hc.Get(page.Url, Headers.Navigate(acceptLanguage, page.Url));
        if (again.Status != 200 && again.Status != 302)
        {
            throw new Exception($"FAIL page re-GET returned {again.Status}");
        }
        return again.Status;
    }

    private static void SiteFirewall(HttpClientWrapper hc, string pageUrl)
    {
        // Insert the relevant requests for your site's firewall. This is up to you and your agent to experiment with.
        // Some sites (DataDome, Akamai) are strict on flow: you might need additional requests here.
    }
}
