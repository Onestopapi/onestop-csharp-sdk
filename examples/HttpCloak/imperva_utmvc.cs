// Imperva utmvc: the page loads the utmvc script from
// /_Incapsula_Resource?SWJIYLWA=<hash>. Load the script, store the generated
// value as the ___utmvc cookie, send the SWKMTFSR image request and load the
// page again.
using System.Globalization;
using System.Text.RegularExpressions;
using OneStop;

namespace Examples;

public static class ImpervaUtmvc
{
    private const string apiKey = "YOUR_API_KEY";
    private const string identifier = "YOUR_IDENTIFIER"; // your identifier or account name
    private const string pageUrl = "https://www.example.com/";
    private const string acceptLanguage = "en-GB,en;q=0.9";

    private const string proxy = ""; // your proxy, in the site's country

    private static readonly Regex UtmvcScript = new(@"""([^""]*/_Incapsula_Resource\?SWJIYLWA=[^""]*)""");

    public static async Task Run()
    {
        var hc = new HttpClientWrapper(proxy);
        var (client, ip) = await NewClient();
        var page = GetChallengePage(hc);
        var scriptUrl = FindScript(page);
        var session = NewSession(client, ip, page.Url);
        var script = GetScript(hc, page, scriptUrl);
        await SolveChallenge(hc, session, page, scriptUrl, script);
        SendBeacon(hc, page);
        ReloadPage(hc, page);
        SiteFirewall(hc, pageUrl);
        Console.WriteLine("PASS ___utmvc cookie set, page re-GET 200");
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

    private static Response GetChallengePage(HttpClientWrapper hc) => hc.Get(pageUrl, Headers.Navigate(acceptLanguage, ""));

    // Returns the utmvc script the page loads from
    // /_Incapsula_Resource?SWJIYLWA=<hash>.
    private static Uri FindScript(Response page)
    {
        var match = UtmvcScript.Match(page.Body);
        if (!match.Success)
        {
            throw new Exception($"FAIL page returned {page.Status} without an Imperva utmvc script");
        }
        return page.Resolve(match.Groups[1].Value);
    }

    private static string GetScript(HttpClientWrapper hc, Response page, Uri scriptUrl)
    {
        var res = hc.Get(scriptUrl.AbsoluteUri, Headers.Script(acceptLanguage, "same-origin", page.Url));
        if (res.Status != 200)
        {
            throw new Exception($"FAIL script returned {res.Status}");
        }
        return res.Body;
    }

    private static async Task SolveChallenge(HttpClientWrapper hc, Session session, Response page, Uri scriptUrl, string script)
    {
        var cookies = hc.Cookies(page.Url, "incap_ses_");
        var res = await session.ImpervaUtmvcAsync(scriptUrl.AbsoluteUri, script, cookies);
        // The script stores res.Payload as the ___utmvc cookie on the page host, path /.
        hc.SetCookie(page.Resolve("/").Host, "___utmvc", res.Payload);
    }

    // SendBeacon is the image request the script makes after setting ___utmvc;
    // e is Math.random().
    private static void SendBeacon(HttpClientWrapper hc, Response page)
    {
        var beacon = page.Resolve("/_Incapsula_Resource?SWKMTFSR=1&e=" + Random.Shared.NextDouble().ToString(CultureInfo.InvariantCulture));
        hc.Get(beacon.AbsoluteUri, Headers.Image(acceptLanguage, "", "same-origin", page.Url));
    }

    private static void ReloadPage(HttpClientWrapper hc, Response page)
    {
        var loaded = hc.Get(page.Url, Headers.Navigate(acceptLanguage, ""));
        if (loaded.Status != 200)
        {
            throw new Exception($"FAIL page re-GET returned {loaded.Status}");
        }
    }

    private static void SiteFirewall(HttpClientWrapper hc, string pageUrl)
    {
        // Insert the relevant requests for your site's firewall. This is up to you and your agent to experiment with.
        // Some sites (DataDome, Akamai) are strict on flow: you might need additional requests here.
    }
}
