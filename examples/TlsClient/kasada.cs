// Kasada: load the page and its p.js, then mfc, fp (429) and ips.js, and
// POST the generated /tl body. A page that is already the 429 block page
// links ips.js itself. /tl answers 200 with reload:true. Then build the
// x-kpsdk-cd header locally and make one protected POST.
using System.Text.RegularExpressions;
using OneStop;

namespace Examples;

public static class Kasada
{
    private const string apiKey = "YOUR_API_KEY";
    private const string identifier = "YOUR_IDENTIFIER"; // your identifier or account name
    private const string pageUrl = "https://www.example.com/";
    private const string protectedUrl = "https://www.example.com/api/protected"; // a request Kasada protects
    private const string protectedBody = "{}"; // its JSON body
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
        var page = GetPage(hc);
        var session = NewSession(client, ip, page.Url);
        var basePath = FindBase(page);
        var ps = await GetPScript(hc, session, page, basePath);
        var (script, h) = GetIps(hc, page, basePath, ps);
        var tl = await PostTl(hc, session, page, script);
        var cd = BuildCdHeader(ps, tl);
        PostProtected(hc, page, ps, tl, h, cd);
        SiteFirewall(hc, pageUrl);
        Console.WriteLine("PASS /tl 200 reload:true, protected request 200");
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

    private static bool IsBlockPage(Response page) => page.Status == 429 && IpsScript.IsMatch(page.Body);

    // Returns the script directory: p.js, mfc, fp, ips.js and tl sit in it. A
    // block page links ips.js there; any other page links p.js.
    private static string FindBase(Response page)
    {
        if (IsBlockPage(page))
        {
            var ipsUrl = IpsUrlFrom(page);
            return ipsUrl[..ipsUrl.IndexOf("ips.js", StringComparison.Ordinal)];
        }
        // p.js lives at /<uuid>/<uuid>/p.js.
        // Some pages write the path JSON-escaped (`\/` or `\u002f` for `/`).
        var match = PjsPath.Match(page.Body.Replace(@"\/", "/").Replace(@"\u002f", "/"));
        if (!match.Success)
        {
            throw new Exception($"FAIL page returned {page.Status} without a Kasada p.js");
        }
        return new Uri(page.Url).GetLeftPart(UriPartial.Authority) + match.Value[..^"p.js".Length];
    }

    // Loads p.js. Keep the result: x-kpsdk-v goes on the Kasada requests and
    // the CD header is built from it.
    private static async Task<KasadaPScriptResponse> GetPScript(HttpClientWrapper hc, Session session, Response page, string basePath)
    {
        var pjs = hc.Get(basePath + "p.js", Headers.Script(acceptLanguage, "same-origin", page.Url));
        return await session.KasadaPScriptAsync(pjs.Body);
    }

    // Loads ips.js and returns it with the x-kpsdk-h from mfc. A block page
    // links it directly (no mfc); otherwise mfc and fp lead to it.
    private static (Ips Script, string H) GetIps(HttpClientWrapper hc, Response page, string basePath, KasadaPScriptResponse ps)
    {
        var (from, h) = (page, "");
        if (!IsBlockPage(page))
        {
            (from, h) = GetFp(hc, page, basePath, ps.Headers["x-kpsdk-v"]);
        }
        var ipsUrl = IpsUrlFrom(from);
        var res = hc.Get(ipsUrl, Headers.Script(acceptLanguage, "same-origin", from.Url));
        if (res.Status != 200)
        {
            throw new Exception($"FAIL ips.js returned {res.Status}");
        }
        return (new Ips(ipsUrl, res.Body, from.Url), h);
    }

    // Loads mfc and fp and returns fp with mfc's x-kpsdk-h, which the
    // protected requests carry. fp answers 429 with a body that links ips.js.
    private static (Response Fp, string H) GetFp(HttpClientWrapper hc, Response page, string basePath, string version)
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
        return (fp, mfc.Header("x-kpsdk-h"));
    }

    // Returns the ips.js src a Kasada 429 body links, HTML-unescaped and
    // resolved against the response URL.
    private static string IpsUrlFrom(Response res) => res.Resolve(IpsScript.Match(res.Body).Groups[1].Value).AbsoluteUri;

    private static async Task<Response> PostTl(HttpClientWrapper hc, Session session, Response page, Ips script)
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
        return res;
    }

    // Builds x-kpsdk-cd locally (no API call) from the p.js result and the /tl
    // response's x-kpsdk-ct and x-kpsdk-st.
    private static string BuildCdHeader(KasadaPScriptResponse ps, Response tl)
    {
        var st = long.Parse(tl.Header("x-kpsdk-st")); // ms
        return ps.CdHeader(tl.Header("x-kpsdk-ct"), st);
    }

    // The request Kasada protects, a POST as most are: x-kpsdk-ct (the newest
    // one seen, here from /tl), x-kpsdk-v from p.js, x-kpsdk-h from mfc and
    // the CD header just built. Each protected POST gets a fresh CD header.
    private static void PostProtected(HttpClientWrapper hc, Response page, KasadaPScriptResponse ps, Response tl, string h, string cd)
    {
        var origin = new Uri(page.Url).GetLeftPart(UriPartial.Authority);
        var headers = Headers.Post(acceptLanguage, "application/json", origin, page.Url).With(new Dictionary<string, string>
        {
            ["x-kpsdk-ct"] = tl.Header("x-kpsdk-ct"),
            ["x-kpsdk-cd"] = cd,
            ["x-kpsdk-h"] = h,
            ["x-kpsdk-v"] = ps.Headers["x-kpsdk-v"],
        });
        var res = hc.Post(protectedUrl, headers, protectedBody);
        if (res.Status != 200)
        {
            throw new Exception($"FAIL protected request returned {res.Status}");
        }
    }

    private static void SiteFirewall(HttpClientWrapper hc, string pageUrl)
    {
        // Insert the relevant requests for your site's firewall. This is up to you and your agent to experiment with.
        // Some sites (DataDome, Akamai) are strict on flow: you might need additional requests here.
    }
}
