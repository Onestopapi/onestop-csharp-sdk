// Cloudflare precursor: the page loads a /scripts/precursor/ script that
// makes up to five small POSTs. Each reply is {"i": seconds, "n": done}:
// n true stops; the API turns a long i into its sleep.
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using OneStop;

namespace Examples;

public static class CloudflarePrecursor
{
    private const string apiKey = "YOUR_API_KEY";
    private const string identifier = "YOUR_IDENTIFIER"; // your identifier or account name
    private const string pageUrl = "https://www.example.com/";
    private const string acceptLanguage = "en-US,en;q=0.9";

    private const string proxy = ""; // your proxy, in the site's country

    // The precursor script's path wherever the page writes it: a script tag or an
    // inline loader that adds one, quoted either way.
    private static readonly Regex PrecursorScript = new(@"(?:https://[A-Za-z0-9.-]+)?/cdn-cgi/challenge-platform/scripts/precursor/[A-Za-z0-9._/-]+");

    // PrecursorReply is a precursor POST's JSON reply: n is true when done.
    private sealed class PrecursorReply
    {
        [JsonPropertyName("n")] public bool N { get; set; }
    }

    public static async Task Run()
    {
        var hc = new HttpClientWrapper(proxy);
        var (client, ip) = await NewClient();
        var (page, first) = GetChallengePage(hc);
        var session = NewSession(client, ip, page.Url);
        var result = await RunChallengeLoop(hc, session, page, first);
        SiteFirewall(hc, pageUrl);
        Console.WriteLine("PASS " + result);
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

    // Returns the page and the first call: the page and the precursor script.
    private static (Response Page, CloudflareCall First) GetChallengePage(HttpClientWrapper hc)
    {
        var page = hc.Get(pageUrl, Headers.Navigate(acceptLanguage, ""));
        var match = PrecursorScript.Match(page.Body);
        if (page.Status != 200 || !match.Success)
        {
            throw new Exception($"FAIL page returned {page.Status} without a precursor script");
        }
        var script = hc.Get(page.Resolve(match.Value).AbsoluteUri, Headers.Script(acceptLanguage, "same-origin", page.Url));
        if (script.Status != 200)
        {
            throw new Exception($"FAIL precursor script returned {script.Status}");
        }
        return (page, new CloudflareCall { Html = page.Body, Script = script.Body, ScriptUrl = script.Url });
    }

    // Sends each step's POSTs; every later call sends the previous context and
    // the target's reply.
    private static async Task<string> RunChallengeLoop(HttpClientWrapper hc, Session session, Response page, CloudflareCall call)
    {
        var origin = new Uri(page.Url).GetLeftPart(UriPartial.Authority);
        for (var posts = 1; posts <= 5; posts++)
        {
            var step = await session.CloudflarePrecursorAsync(call);
            call = new CloudflareCall { Context = step.Context };
            foreach (var r in step.Requests)
            {
                var res = hc.Do(r.Method, r.Url, Headers.Post(acceptLanguage, "text/plain;charset=UTF-8", origin, page.Url).With(r.Headers), r.Body);
                if (res.Status < 200 || res.Status > 299 || !res.Body.StartsWith('{'))
                {
                    throw new Exception($"FAIL precursor POST {posts} returned {res.Status}: {res.Body}");
                }
                var reply = JsonSerializer.Deserialize<PrecursorReply>(res.Body)!;
                if (reply.N)
                {
                    return "precursor POSTs 2xx until n:true";
                }
                call.Response = JsonSerializer.Deserialize<JsonElement>(res.Body);
            }
        }
        return "5 precursor POSTs 2xx";
    }

    private static void SiteFirewall(HttpClientWrapper hc, string pageUrl)
    {
        // Insert the relevant requests for your site's firewall. This is up to you and your agent to experiment with.
        // Some sites (DataDome, Akamai) are strict on flow: you might need additional requests here.
    }
}
