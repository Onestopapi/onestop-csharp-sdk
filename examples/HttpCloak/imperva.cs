// Imperva: the page answers with the reese84 challenge page, which adds the
// challenge script. Load the script, POST the generated body to the script's
// path with ?d=<host>, store the returned token as the reese84 cookie and
// load the page again.
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using OneStop;

namespace Examples;

public static class Imperva
{
    private const string apiKey = "YOUR_API_KEY";
    private const string identifier = "YOUR_IDENTIFIER"; // your identifier or account name
    private const string pageUrl = "https://www.example.com/";
    private const string acceptLanguage = "en-GB,en;q=0.9";

    private const string proxy = ""; // your proxy, in the site's country

    private static readonly Regex ChallengeScript = new(@"scriptElement\.src\s*=\s*""([^""]+)""");

    // TokenReply is the reese84 POST's JSON reply.
    private sealed class TokenReply
    {
        [JsonPropertyName("token")] public string Token { get; set; } = "";
        [JsonPropertyName("cookieDomain")] public string CookieDomain { get; set; } = "";
    }

    public static async Task Run()
    {
        var hc = new HttpClientWrapper(proxy);
        var (client, ip) = await NewClient();
        var page = GetChallengePage(hc);
        var scriptUrl = FindScript(page);
        var session = NewSession(client, ip, page.Url);
        var script = GetScript(hc, page, scriptUrl);
        await SolveChallenge(hc, session, page, scriptUrl, script);
        ReloadPage(hc, page);
        SiteFirewall(hc, pageUrl);
        Console.WriteLine("PASS reese84 cookie set, page re-GET 200");
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

    // Returns the script the challenge page adds with
    // scriptElement.src = "...".
    private static Uri FindScript(Response page)
    {
        var match = ChallengeScript.Match(page.Body);
        if (!match.Success)
        {
            throw new Exception($"FAIL page returned {page.Status} without an Imperva challenge");
        }
        return page.Resolve(match.Groups[1].Value);
    }

    private static string GetScript(HttpClientWrapper hc, Response page, Uri scriptUrl)
    {
        var res = hc.Get(scriptUrl.AbsoluteUri, Headers.ImpervaScript(acceptLanguage, page.Url));
        if (res.Status != 200)
        {
            throw new Exception($"FAIL script returned {res.Status}");
        }
        return res.Body;
    }

    private static async Task SolveChallenge(HttpClientWrapper hc, Session session, Response page, Uri scriptUrl, string script)
    {
        var res = await session.ImpervaAsync(scriptUrl.AbsoluteUri, script);
        // res.Payload is the POST body; it goes to the script's path with ?d=<host>.
        var origin = scriptUrl.GetLeftPart(UriPartial.Authority);
        var post = hc.Post(origin + scriptUrl.AbsolutePath + "?d=" + scriptUrl.Host, Headers.ImpervaPost(acceptLanguage, origin, page.Url), res.Payload);
        var reply = post.Body.StartsWith('{') ? JsonSerializer.Deserialize<TokenReply>(post.Body)! : new TokenReply();
        if (reply.Token == "")
        {
            throw new Exception($"FAIL reese84 POST returned {post.Status}: {post.Body}");
        }
        // The script stores the token as the reese84 cookie on cookieDomain.
        hc.SetCookie(reply.CookieDomain, "reese84", reply.Token);
    }

    private static void ReloadPage(HttpClientWrapper hc, Response page)
    {
        var loaded = hc.Get(page.Url, Headers.Navigate(acceptLanguage, ""));
        if (loaded.Status != 200)
        {
            throw new Exception($"FAIL page re-GET returned {loaded.Status}");
        }
        if (ChallengeScript.IsMatch(loaded.Body))
        {
            throw new Exception("FAIL page re-GET is still the challenge page");
        }
    }

    private static void SiteFirewall(HttpClientWrapper hc, string pageUrl)
    {
        // Insert the relevant requests for your site's firewall. This is up to you and your agent to experiment with.
        // Some sites (DataDome, Akamai) are strict on flow: you might need additional requests here.
    }
}
