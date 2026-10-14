// Kasada: load the page and its p.js, then mfc, fp (429) and ips.js, and
// POST the generated /tl body. A page that is already the 429 block page
// links ips.js itself. /tl answers 200 with reload:true.
using System.Text.RegularExpressions;
using OneStop;

namespace Examples;

public static class Kasada
{
    private const string apiKey = "YOUR_API_KEY";
    private const string identifier = ""; // your identifier or account name
    private const string pageUrl = "https://www.example.com/";
    private const string acceptLanguage = "en-US,en;q=0.9";

    private const string proxy = ""; // your proxy, in the site's country

    private static readonly Regex PjsPath = new(@"/[0-9a-f-]{36}/[0-9a-f-]{36}/p\.js");
    private static readonly Regex IpsScript = new(@"src=""([^""]*/ips\.js[^""]*)""");

    // Ips is the loaded ips.js: its URL, its source and the page that loaded it.
    private sealed record Ips(string Url, string Source, string Referer);

    public static async Task Run()
    {
        var hc = new HttpClientWrapper(proxy);
        var (client, ip) = await NewClient();
        var session = NewSession(client, ip, pageUrl);
        var page = GetPage(hc);
        var script = await GetIps(hc, session, page);
        await PostTl(hc, session, page, script);
        SiteFirewall(hc, pageUrl);
        Console.WriteLine("PASS /tl 200 reload:true");
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

    private static Response GetPage(HttpClientWrapper hc) => hc.Get(pageUrl, Headers.Navigate(acceptLanguage, ""));

    // Loads ips.js. When the page itself is the 429 block page it links ips.js
    // directly; otherwise p.js, mfc and fp lead to it.
    private static async Task<Ips> GetIps(HttpClientWrapper hc, Session session, Response page)
    {
        var from = page;
        if (page.Status != 429 || !IpsScript.IsMatch(page.Body))
        {
            var (basePath, version) = await GetPScript(hc, session, page);
            from = GetFp(hc, page, basePath, version);
        }
        var ipsUrl = IpsUrlFrom(from);
        var res = hc.Get(ipsUrl, Headers.Script(acceptLanguage, "same-origin", from.Url));
        if (res.Status != 200)
        {
            throw new Exception($"FAIL ips.js returned {res.Status}");
        }
        return new Ips(ipsUrl, res.Body, from.Url);
    }

    // Returns the script directory and x-kpsdk-v from the p.js result.
    private static async Task<(string BasePath, string Version)> GetPScript(HttpClientWrapper hc, Session session, Response page)
    {
        // p.js lives at /<uuid>/<uuid>/p.js; mfc, fp, ips.js and tl sit next to it.
        // Some pages write the path JSON-escaped (\/ or \u002f for /).
        var match = PjsPath.Match(page.Body.Replace(@"\/", "/").Replace(@"\u002f", "/"));
        if (!match.Success)
        {
            throw new Exception($"FAIL page returned {page.Status} without a Kasada p.js");
        }
        var basePath = new Uri(page.Url).GetLeftPart(UriPartial.Authority) + match.Value[..^"p.js".Length];
        var pjs = hc.Get(basePath + "p.js", Headers.Script(acceptLanguage, "same-origin", page.Url));
        var ps = await session.KasadaPScriptAsync(pjs.Body);
        return (basePath, ps.Headers["x-kpsdk-v"]);
    }

    // Loads mfc and fp. fp answers 429 with a body that links ips.js.
    private static Response GetFp(HttpClientWrapper hc, Response page, string basePath, string version)
    {
        var mfc = hc.Get(basePath + "mfc", Headers.KasadaMfc(acceptLanguage, version, page.Url));
        if (mfc.Status != 200)
        {
            throw new Exception($"FAIL mfc returned {mfc.Status}");
        }
        var fp = hc.Get(basePath + "fp?x-kpsdk-v=" + version, Headers.Frame(acceptLanguage, "same-origin", page.Url));
        if (!IpsScript.IsMatch(fp.Body))
        {
            throw new Exception("FAIL fp returned no ips.js");
        }
        return fp;
    }

    // Returns the ips.js src a Kasada 429 body links, HTML-unescaped and
    // resolved against the response URL.
    private static string IpsUrlFrom(Response res) => res.Resolve(IpsScript.Match(res.Body).Groups[1].Value).AbsoluteUri;

    private static async Task PostTl(HttpClientWrapper hc, Session session, Response page, Ips script)
    {
        var tl = await session.KasadaTlAsync(script.Url, script.Source);
        var origin = new Uri(page.Url).GetLeftPart(UriPartial.Authority);
        var basePath = script.Url[..script.Url.IndexOf("ips.js", StringComparison.Ordinal)];
        var body = Convert.FromBase64String(tl.Payload); // Payload is the /tl body in base64
        var res = hc.Post(basePath + "tl", Headers.KasadaTl(acceptLanguage, tl.Headers, origin, script.Referer), body);
        if (res.Status != 200 || !res.Body.Contains("\"reload\":true"))
        {
            throw new Exception($"FAIL /tl returned {res.Status}: {res.Body}");
        }
    }

    private static void SiteFirewall(HttpClientWrapper hc, string pageUrl)
    {
        // Insert the relevant requests for your site's firewall. This is up to you and your agent to experiment with.
        // Some sites (DataDome, Akamai) are strict on flow: you might need additional requests here.
    }
}
