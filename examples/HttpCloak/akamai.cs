// Akamai: load the homepage, send the SBSD payloads (mode f, then t), then
// POST sensor data until the sensor is accepted.
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Web;
using OneStop;

namespace Examples;

public static class Akamai
{
    private const string apiKey = "YOUR_API_KEY";
    private const string identifier = ""; // your identifier or account name
    private const string pageUrl = "https://www.example.com/";
    private const string acceptLanguage = "en-US,en;q=0.9";

    private const string proxy = ""; // your proxy, in the site's country

    private static readonly Regex SensorScript = new(@"<script type=""text/javascript""[^>]*src=""([A-Za-z0-9/_-]+)""></script>");
    private static readonly Regex SbsdScript = new(@"<script[^>]*src=""([^""]*\?v=[^""]*)""");
    private static readonly JsonSerializerOptions Json = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    // SbsdBody is the JSON body of an SBSD POST.
    private sealed record SbsdBody([property: JsonPropertyName("body")] string Body);

    // SensorBody is the JSON body of a sensor POST.
    private sealed record SensorBody([property: JsonPropertyName("sensor_data")] string SensorData);

    public static async Task Run()
    {
        var hc = new HttpClientWrapper(proxy);
        var (client, ip) = await NewClient();
        var page = GetHomePage(hc);
        var (sensorUrl, sbsdUrl) = FindScripts(page);
        var session = NewSession(client, ip, page.Url);
        var (sensor, sbsd) = GetScripts(hc, page, sensorUrl, sbsdUrl);
        await SolveSbsdPassive(hc, session, page, sbsdUrl, sbsd);
        await PostSensors(hc, session, page, sensorUrl, sensor);
        SiteFirewall(hc, pageUrl);
        Console.WriteLine("PASS sensor accepted");
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

    private static Response GetHomePage(HttpClientWrapper hc) => hc.Get(pageUrl, Headers.Navigate(acceptLanguage, "").NoCache());

    private static (Uri SensorUrl, Uri SbsdUrl) FindScripts(Response page)
    {
        var sensorMatch = SensorScript.Match(page.Body);
        var sbsdMatch = SbsdScript.Match(page.Body);
        if (page.Status != 200 || !sensorMatch.Success || !sbsdMatch.Success)
        {
            throw new Exception($"FAIL homepage returned {page.Status} without the sensor and SBSD scripts");
        }
        return (page.Resolve(sensorMatch.Groups[1].Value), page.Resolve(sbsdMatch.Groups[1].Value));
    }

    private static (string Sensor, string Sbsd) GetScripts(HttpClientWrapper hc, Response page, Uri sensorUrl, Uri sbsdUrl)
    {
        var sensor = hc.Get(sensorUrl.AbsoluteUri, Headers.Script(acceptLanguage, "same-origin", page.Url).NoCache()).Body;
        var sbsd = hc.Get(sbsdUrl.AbsoluteUri, Headers.Script(acceptLanguage, "same-origin", page.Url).NoCache()).Body;
        return (sensor, sbsd);
    }

    private static async Task SolveSbsdPassive(HttpClientWrapper hc, Session session, Response page, Uri sbsdUrl, string script)
    {
        var origin = new Uri(page.Url).GetLeftPart(UriPartial.Authority);
        // uuid is the v= value of the SBSD script URL; mode t sends f's context.
        var call = new SbsdCall { Uuid = HttpUtility.ParseQueryString(sbsdUrl.Query)["v"] ?? "", Script = script };
        foreach (var mode in new[] { "f", "t" })
        {
            call.Mode = mode;
            call.SbsdCookie = hc.Cookie(page.Url, "bm_so", "sbsd_o"); // cookie set by the page
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

    private static async Task PostSensors(HttpClientWrapper hc, Session session, Response page, Uri sensorUrl, string script)
    {
        var origin = sensorUrl.GetLeftPart(UriPartial.Authority);
        // The first sensor call sends the script, every later call the previous
        // context instead. _abck and bm_sz are cookies set by the site.
        var call = new AkamaiSensorCall { Script = script, ScriptUrl = sensorUrl.AbsoluteUri };
        for (var n = 1; ; n++)
        {
            call.Abck = hc.Cookie(page.Url, "_abck");
            call.Bmsz = hc.Cookie(page.Url, "bm_sz");
            var res = await session.AkamaiSensorAsync(call);
            var body = JsonSerializer.Serialize(new SensorBody(res.Payload), Json);
            var post = hc.Post(sensorUrl.AbsoluteUri, Headers.Post(acceptLanguage, "text/plain;charset=UTF-8", origin, page.Url).NoCache(), body);
            if (post.Status != 200 && post.Status != 201)
            {
                throw new Exception($"FAIL sensor POST returned {post.Status}");
            }
            // Accepted: _abck contains ~0~ or the reply is the 17-byte ack.
            if (hc.Cookie(page.Url, "_abck").Contains("~0~") || post.Content.Length == 17)
            {
                return;
            }
            if (n == 8)
            {
                throw new Exception("FAIL sensor not accepted after 8 POSTs");
            }
            call = new AkamaiSensorCall { ScriptUrl = sensorUrl.AbsoluteUri, Context = res.Context };
        }
    }

    private static void SiteFirewall(HttpClientWrapper hc, string pageUrl)
    {
        // Insert the relevant requests for your site's firewall. This is up to you and your agent to experiment with.
        // Some sites (DataDome, Akamai) are strict on flow: you might need additional requests here.
        // If a later request resets _abck, run one more sensor call with the last context plus one POST.
    }
}
