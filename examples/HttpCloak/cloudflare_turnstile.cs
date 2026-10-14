// Cloudflare Turnstile: load the widget's api.js and its iframe, then send
// the requests each step returns until the API returns the token.
using System.Buffers.Binary;
using OneStop;

namespace Examples;

public static class CloudflareTurnstile
{
    private const string apiKey = "YOUR_API_KEY";
    private const string identifier = ""; // your identifier or account name
    private const string pageUrl = "https://www.example.com/event";
    private const string siteKey = "YOUR_TURNSTILE_SITE_KEY"; // from the page's turnstile.render options
    private const string action = "YOUR_TURNSTILE_ACTION";
    private const string cData = "YOUR_TURNSTILE_CDATA";
    private const string acceptLanguage = "en-US,en;q=0.9";

    private const string proxy = ""; // your proxy, in the site's country

    public static async Task Run()
    {
        var hc = new HttpClientWrapper(proxy);
        var (client, ip) = await NewClient();
        var session = NewSession(client, ip, pageUrl);
        var first = GetChallengePage(hc);
        var token = await RunChallengeLoop(hc, session, first);
        SiteFirewall(hc, pageUrl, token);
        Console.WriteLine("PASS Turnstile token returned");
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

    // Returns the first call: the widget iframe, its final URL and its cf-ray
    // header.
    private static CloudflareCall GetChallengePage(HttpClientWrapper hc)
    {
        var page = new Uri(pageUrl);
        var api = hc.Get("https://challenges.cloudflare.com/turnstile/v0/api.js", Headers.Script(acceptLanguage, "cross-site", page.GetLeftPart(UriPartial.Authority) + "/").StorageAccess());
        // api.js redirects to /turnstile/v0/<mode>/<version>/api.js.
        var segments = new Uri(api.Url).AbsolutePath.Split('/');
        if (segments.Length != 6)
        {
            throw new Exception($"FAIL api.js resolved to {api.Url}");
        }
        var mode = segments[3];
        // rch is a random 5-character render id.
        var rch = new string(Enumerable.Range(0, 5).Select(_ => "abcdefghijklmnopqrstuvwxyz0123456789"[Random.Shared.Next(36)]).ToArray());
        var frame = hc.Get("https://challenges.cloudflare.com/cdn-cgi/challenge-platform/h/" + mode + "/turnstile/f/av0/rch/" + rch + "/" + siteKey + "/light/fbE/new/normal?lang=auto",
            Headers.Frame(acceptLanguage, "cross-site", "").StorageAccess());
        if (frame.Status != 200)
        {
            throw new Exception($"FAIL widget iframe returned {frame.Status}");
        }
        return new CloudflareCall
        {
            Html = frame.Body,
            ScriptUrl = api.Url,
            IframeUrl = frame.Url,
            BytecodeRayId = frame.Header("cf-ray"),
            Action = action,
            CData = cData,
        };
    }

    // Sends each step's requests; each reply goes back on the next call, by its
    // purpose, until the API returns the token.
    private static async Task<string> RunChallengeLoop(HttpClientWrapper hc, Session session, CloudflareCall call)
    {
        var frameUrl = call.IframeUrl;
        var step = await session.CloudflareTurnstileAsync(call);
        while (step.Token == "")
        {
            var next = new CloudflareCall { Context = step.Context, Action = action, CData = cData };
            foreach (var r in step.Requests)
            {
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
                }
            }
            step = await session.CloudflareTurnstileAsync(next);
        }
        return step.Token;
    }

    private static void SiteFirewall(HttpClientWrapper hc, string pageUrl, string token)
    {
        // Insert the relevant requests for your site's firewall. This is up to you and your agent to experiment with.
        // Some sites (DataDome, Akamai) are strict on flow: you might need additional requests here.
    }
}
