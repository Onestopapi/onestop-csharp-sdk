// DataDome interstitial: the page answers 403 with `var dd={...rt:'i'}`. Load
// the interstitial page and its script, POST the generated form body, store
// the returned datadome cookie and load the page again.
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using OneStop;

namespace Examples;

public static class DatadomeInterstitial
{
    private const string apiKey = "YOUR_API_KEY";
    private const string identifier = "YOUR_IDENTIFIER"; // your identifier or account name
    private const string pageUrl = "https://www.example.com/";
    private const string acceptLanguage = "en-US,en;q=0.9";

    private const string proxy = ""; // your proxy, in the site's country

    private const string GeoOrigin = "https://geo.captcha-delivery.com";

    private static readonly Regex DdObject = new(@"var\s+dd\s*=\s*(\{[^}]*\})");
    private static readonly Regex DdScript = new(@"src=""([^""]*interstitial\.[^""]*)""");

    private sealed class Challenge
    {
        [JsonPropertyName("rt")] public string Rt { get; set; } = "";
        [JsonPropertyName("cid")] public string Cid { get; set; } = "";
        [JsonPropertyName("hsh")] public string Hash { get; set; } = "";
        [JsonPropertyName("t")] public string T { get; set; } = "";
        [JsonPropertyName("s")] public int S { get; set; }
        [JsonPropertyName("e")] public string E { get; set; } = "";
        [JsonPropertyName("b")] public int B { get; set; }
        [JsonPropertyName("cookie")] public string Cookie { get; set; } = "";
    }

    // InterstitialReply is the interstitial POST's JSON reply.
    private sealed class InterstitialReply
    {
        [JsonPropertyName("view")] public string View { get; set; } = "";
        [JsonPropertyName("cookie")] public string Cookie { get; set; } = "";
    }

    public static async Task Run()
    {
        var hc = new HttpClientWrapper(proxy);
        var (client, ip) = await NewClient();
        var (page, dd) = GetBlockedPage(hc);
        var session = NewSession(client, ip, page.Url);
        var (geoUrl, geo) = await GetChallengePage(hc, page, dd);
        await SolveInterstitial(hc, session, page, geoUrl, geo);
        var status = ReloadPage(hc, page);
        SiteFirewall(hc, pageUrl);
        Console.WriteLine("PASS interstitial view redirect, page re-GET " + status);
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
        if (dd.Rt != "i")
        {
            throw new Exception($"FAIL challenge type \"{dd.Rt}\" is not the interstitial (t=\"{dd.T}\")");
        }
        return (page, dd);
    }

    private static async Task<(string GeoUrl, Response Geo)> GetChallengePage(HttpClientWrapper hc, Response page, Challenge dd)
    {
        var geoUrl = $"{GeoOrigin}/interstitial/?initialCid={Uri.EscapeDataString(dd.Cid)}&hash={Uri.EscapeDataString(dd.Hash)}&cid={Uri.EscapeDataString(dd.Cookie)}&referer={Uri.EscapeDataString(page.Url)}&s={dd.S}";
        if (dd.E != "")
        {
            geoUrl += "&e=" + Uri.EscapeDataString(dd.E);
        }
        geoUrl += $"&b={dd.B}&dm=cd";

        // These pauses are browser pacing between the challenge requests, not the API's sleep.
        await Task.Delay(400);
        var geo = hc.Get(geoUrl, Headers.Frame(acceptLanguage, "cross-site", page.Url).StorageAccess());
        if (geo.Status != 200)
        {
            throw new Exception($"FAIL interstitial page returned {geo.Status}");
        }
        return (geoUrl, geo);
    }

    private static async Task SolveInterstitial(HttpClientWrapper hc, Session session, Response page, string geoUrl, Response geo)
    {
        // Without a script tag the bundle is inline and the API reads it from Html.
        var script = "";
        var match = DdScript.Match(geo.Body);
        if (match.Success)
        {
            await Task.Delay(200);
            script = hc.Get(match.Groups[1].Value, Headers.Script(acceptLanguage, "same-site", GeoOrigin + "/").StorageAccess()).Body;
        }

        await Task.Delay(400);
        // Script and Html are the interstitial's; PageResponse is the blocked page.
        var res = await session.DatadomeInterstitialAsync(new DatadomeInterstitialCall
        {
            Script = script,
            Html = geo.Body,
            PageResponse = page.Body,
        });

        await Task.Delay(1000);
        // res.Payload is the generated form body.
        var post = hc.Post(GeoOrigin + "/interstitial/", Headers.Post(acceptLanguage, "application/x-www-form-urlencoded; charset=UTF-8", GeoOrigin, geoUrl).StorageAccess(), res.Payload);
        var reply = post.Body.StartsWith('{') ? JsonSerializer.Deserialize<InterstitialReply>(post.Body)! : new InterstitialReply();
        if (reply.View != "redirect")
        {
            throw new Exception($"FAIL interstitial POST returned {post.Status}, view \"{reply.View}\": {post.Body}");
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
