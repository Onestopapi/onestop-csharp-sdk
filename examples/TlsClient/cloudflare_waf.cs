// Cloudflare WAF: the page answers 403 with the managed challenge. Load the
// challenge script, then send the requests each step returns until the form
// POST that sets cf_clearance.
using System.Buffers.Binary;
using System.Text.RegularExpressions;
using OneStop;

namespace Examples;

public static class CloudflareWaf
{
    private const string apiKey = "YOUR_API_KEY";
    private const string identifier = ""; // your identifier or account name
    private const string pageUrl = "https://www.example.com/login";
    private const string acceptLanguage = "en-US,en;q=0.9";

    private const string proxy = ""; // your proxy, in the site's country

    private static readonly Regex ChallengeScript = new(@"src = '(/cdn-cgi/challenge-platform/[^']+)'");
    private static readonly Regex ChallengeToken = new(@"__cf_chl_rt_tk=([^""&]+)");

    public static async Task Run()
    {
        var hc = new HttpClientWrapper(proxy);
        var (client, ip) = await NewClient();
        var session = NewSession(client, ip, pageUrl);
        var first = GetChallengePage(hc);
        await RunChallengeLoop(hc, session, first);
        SiteFirewall(hc, pageUrl);
        Console.WriteLine("PASS cf_clearance set");
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

    // Returns the first call: the challenge page and its script.
    private static CloudflareCall GetChallengePage(HttpClientWrapper hc)
    {
        var page = hc.Get(pageUrl, Headers.Navigate(acceptLanguage, ""));
        // The challenge page has the script path (src = '...') and the
        // __cf_chl_rt_tk token.
        var scriptMatch = ChallengeScript.Match(page.Body);
        var tokenMatch = ChallengeToken.Match(page.Body);
        if (!scriptMatch.Success || !tokenMatch.Success)
        {
            throw new Exception($"FAIL page returned {page.Status} without a Cloudflare challenge");
        }
        var scriptUrl = page.Resolve(scriptMatch.Groups[1].Value).AbsoluteUri;
        var script = hc.Get(scriptUrl, Headers.ChallengeScript(acceptLanguage, pageUrl + "?__cf_chl_rt_tk=" + tokenMatch.Groups[1].Value));
        return new CloudflareCall { Html = page.Body, Script = script.Body, ScriptUrl = scriptUrl };
    }

    // Sends each step's requests; each reply goes back on the next call, by its
    // purpose. The form POST sets cf_clearance.
    private static async Task RunChallengeLoop(HttpClientWrapper hc, Session session, CloudflareCall call)
    {
        var step = await session.CloudflareWafAsync(call);
        var frameUrl = "";
        while (step.Token == "")
        {
            var next = new CloudflareCall { Context = step.Context };
            foreach (var r in step.Requests)
            {
                if (r.Purpose == "form")
                {
                    hc.FollowRedirects(false);
                }
                var res = hc.Do(r.Method, r.Url, Headers.Cloudflare(r, acceptLanguage, pageUrl, frameUrl), r.Body);
                switch (r.Purpose)
                {
                    case "challenge":
                        if (res.Status != 200)
                        {
                            throw new Exception($"FAIL challenge request returned {res.Status}");
                        }
                        next.Response = res.Body;
                        break;
                    case "iframe":
                        frameUrl = res.Url;
                        next.Html = res.Body;
                        next.IframeUrl = res.Url;
                        next.BytecodeRayId = res.Header("cf-ray");
                        break;
                    case "image":
                        // A PNG's width and height are the big-endian ints at bytes 16 and 20.
                        if (res.Content.Length < 24 || !res.Content.AsSpan(1, 3).SequenceEqual("PNG"u8))
                        {
                            throw new Exception($"FAIL challenge image returned {res.Status} without a PNG");
                        }
                        next.ImageWidth = BinaryPrimitives.ReadInt32BigEndian(res.Content.AsSpan(16));
                        next.ImageHeight = BinaryPrimitives.ReadInt32BigEndian(res.Content.AsSpan(20));
                        break;
                    case "pat":
                        next.PatStatus = res.Status; // a 4xx is normal
                        break;
                    case "form":
                        if (hc.Cookie(pageUrl, "cf_clearance") == "")
                        {
                            throw new Exception($"FAIL form POST returned {res.Status} without cf_clearance");
                        }
                        return;
                }
            }
            step = await session.CloudflareWafAsync(next);
        }
        throw new Exception("FAIL the challenge ended without a form POST");
    }

    private static void SiteFirewall(HttpClientWrapper hc, string pageUrl)
    {
        // Insert the relevant requests for your site's firewall. This is up to you and your agent to experiment with.
        // Some sites (DataDome, Akamai) are strict on flow: you might need additional requests here.
    }
}
