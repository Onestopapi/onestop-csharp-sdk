// SBSD block: POST one mode f payload to the block script with ?t=<token>
// (bm_sc 2~ clears the block), load the page again, then send the passive
// SBSD payloads (mode f, then t) for the real page's script.
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Web;
using OneStop;

namespace Examples;

public static class Sbsd
{
    private const string apiKey = "YOUR_API_KEY";
    private const string identifier = "YOUR_IDENTIFIER"; // your identifier or account name
    private const string pageUrl = "https://www.example.com/";
    private const string acceptLanguage = "de-DE,de;q=0.9";

    private const string proxy = ""; // your proxy, in the site's country

    private static readonly Regex SbsdScript = new(@"<script[^>]*src=""([^""]*\?v=[^""]*)""");
    private static readonly Regex BlockToken = new(@"\{""cpr_chlge"":""true"",""t"":""([^""]*)""");
    private static readonly JsonSerializerOptions Json = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    // SbsdBody is the JSON body of an SBSD POST.
    private sealed record SbsdBody([property: JsonPropertyName("body")] string Body);

    public static async Task Run()
    {
        var hc = new HttpClientWrapper(proxy);
        var (client, ip) = await NewClient();
        var page = GetBlockPage(hc);
        var (blockUrl, token) = FindBlockScript(page);
        var session = NewSession(client, ip, page.Url);
        await SolveBlock(hc, session, page, blockUrl, token);
        var (loaded, sbsdUrl) = ReloadPage(hc, page);
        await SolvePassive(hc, session, loaded, sbsdUrl);
        SiteFirewall(hc, pageUrl);
        Console.WriteLine("PASS SBSD block cleared (bm_sc 2~), real page loaded, passive f+t 200");
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

    private static Response GetBlockPage(HttpClientWrapper hc) => hc.Get(pageUrl, Headers.Navigate(acceptLanguage, ""));

    private static (Uri BlockUrl, string Token) FindBlockScript(Response page)
    {
        var match = SbsdScript.Match(page.Body);
        if (!match.Success)
        {
            throw new Exception("FAIL not a block page");
        }
        var blockUrl = page.Resolve(match.Groups[1].Value);
        // The block token is the script URL's t= value, else the page's
        // {"cpr_chlge":"true","t":"<token>"}.
        var token = HttpUtility.ParseQueryString(blockUrl.Query)["t"] ?? "";
        var m = BlockToken.Match(page.Body);
        if (token == "" && m.Success)
        {
            token = m.Groups[1].Value;
        }
        if (token == "")
        {
            throw new Exception("FAIL not a block page");
        }
        return (blockUrl, token);
    }

    private static async Task SolveBlock(HttpClientWrapper hc, Session session, Response page, Uri blockUrl, string token)
    {
        var origin = blockUrl.GetLeftPart(UriPartial.Authority);
        var block = hc.Get(blockUrl.AbsoluteUri, Headers.Script(acceptLanguage, "same-origin", page.Url));
        var res = await session.SbsdPayloadAsync(new SbsdCall
        {
            Uuid = HttpUtility.ParseQueryString(blockUrl.Query)["v"] ?? "", // v= value of the script URL
            SbsdCookie = hc.Cookie(page.Url, "bm_so", "sbsd_o"), // cookie set by the page
            Script = block.Body,
            Mode = "f",
        });
        var body = JsonSerializer.Serialize(new SbsdBody(res.Payload), Json);
        var post = hc.Post(origin + blockUrl.AbsolutePath + "?t=" + token, Headers.Post(acceptLanguage, "application/json", origin, page.Url), body);
        if (post.Status != 200)
        {
            throw new Exception($"FAIL SBSD block POST returned {post.Status}");
        }
        // bm_sc 2~ passed, 3~ failed.
        var verdict = hc.Cookie(page.Url, "bm_sc");
        if (!verdict.StartsWith("2~"))
        {
            throw new Exception($"FAIL SBSD block verdict bm_sc={verdict}");
        }
    }

    private static (Response Loaded, Uri SbsdUrl) ReloadPage(HttpClientWrapper hc, Response page)
    {
        var loaded = hc.Get(page.Url, Headers.Navigate(acceptLanguage, ""));
        if (loaded.Status != 200)
        {
            throw new Exception($"FAIL page re-GET returned {loaded.Status}");
        }
        var match = SbsdScript.Match(loaded.Body);
        if (!match.Success)
        {
            throw new Exception("FAIL real page has no SBSD script");
        }
        var sbsdUrl = loaded.Resolve(match.Groups[1].Value);
        if ((HttpUtility.ParseQueryString(sbsdUrl.Query)["t"] ?? "") != "")
        {
            throw new Exception("FAIL page re-GET is still a block page");
        }
        return (loaded, sbsdUrl);
    }

    private static async Task SolvePassive(HttpClientWrapper hc, Session session, Response page, Uri sbsdUrl)
    {
        var origin = sbsdUrl.GetLeftPart(UriPartial.Authority);
        var sbsd = hc.Get(sbsdUrl.AbsoluteUri, Headers.Script(acceptLanguage, "same-origin", page.Url).NoCache());
        // uuid is the v= value of the new script URL; mode t sends f's context.
        var call = new SbsdCall { Uuid = HttpUtility.ParseQueryString(sbsdUrl.Query)["v"] ?? "", Script = sbsd.Body };
        foreach (var mode in new[] { "f", "t" })
        {
            call.Mode = mode;
            call.SbsdCookie = hc.Cookie(page.Url, "bm_so", "sbsd_o");
            var res = await session.SbsdPayloadAsync(call);
            var body = JsonSerializer.Serialize(new SbsdBody(res.Payload), Json);
            var post = hc.Post(origin + sbsdUrl.AbsolutePath, Headers.Post(acceptLanguage, "application/json", origin, page.Url).NoCache(), body);
            if (post.Status != 200)
            {
                throw new Exception($"FAIL SBSD {mode} POST returned {post.Status}");
            }
            call = new SbsdCall { Uuid = call.Uuid, Context = res.Context };
        }
    }

    private static void SiteFirewall(HttpClientWrapper hc, string pageUrl)
    {
        // Insert the relevant requests for your site's firewall. This is up to you and your agent to experiment with.
        // Some sites (DataDome, Akamai) are strict on flow: you might need additional requests here.
    }
}
