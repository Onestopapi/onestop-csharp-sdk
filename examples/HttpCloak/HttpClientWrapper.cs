// Header sets and order here come from one Chrome recording; copy yours from a browser recording of each request you make.
// Sends the examples' target requests with HttpCloak (NuGet): Chrome TLS and
// HTTP/2 fingerprint, one cookie jar. Also holds Chrome's headers for each kind
// of request.
using System.Net;
using System.Text;
using OneStop;

namespace Examples;

/// <summary><see cref="Url"/> is the final URL, after redirects.</summary>
public sealed record Response(int Status, string Url, string Body, byte[] Content, Dictionary<string, string[]> Headers)
{
    /// <summary>The first value of the named header, or "".</summary>
    public string Header(string name) =>
        Headers.FirstOrDefault(h => h.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value?.FirstOrDefault() ?? "";

    /// <summary>A URL found in the body (HTML-escaped, maybe relative) as an absolute URL.</summary>
    public Uri Resolve(string reference) => new(new Uri(Url), WebUtility.HtmlDecode(reference));
}

public sealed class HttpClientWrapper
{
    // Chrome 155 on Windows. Every header set in Headers is in Chrome's order; check them against your browser on your site.
    public const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/155.0.0.0 Safari/537.36";

    private readonly HttpCloak.Session session;

    /// <summary>A client that follows redirects; proxy is "" or a proxy URL. TLS-only mode: HttpCloak adds no headers of its own; HTTP/3 is off so every request uses the proxy's TCP tunnel.</summary>
    public HttpClientWrapper(string proxy)
    {
        session = new HttpCloak.Session(
            preset: HttpCloak.Presets.Chrome155Windows,
            proxy: proxy == "" ? null : proxy,
            timeout: 30,
            tlsOnly: true,
            disableHttp3: true,
            retry: 0,
            withoutConditionalCache: true);
    }

    /// <summary>The client for <c>GetIpAsync</c>, through the same proxy, so the API sees the IP the target sees.</summary>
    public static HttpClient IpClient(string proxy)
    {
        if (proxy == "")
        {
            return new HttpClient();
        }
        // WebProxy ignores credentials in the URL, so pass them separately.
        var uri = new Uri(proxy);
        var webProxy = new WebProxy(uri.GetLeftPart(UriPartial.Authority));
        if (uri.UserInfo != "")
        {
            var login = uri.UserInfo.Split(':', 2);
            webProxy.Credentials = new NetworkCredential(Uri.UnescapeDataString(login[0]), Uri.UnescapeDataString(login[1]));
        }
        return new HttpClient(new HttpClientHandler { Proxy = webProxy });
    }

    public Response Get(string url, Headers headers) => Do("GET", url, headers, []);

    public Response Post(string url, Headers headers, string body) => Do("POST", url, headers, Encoding.UTF8.GetBytes(body));

    public Response Post(string url, Headers headers, byte[] body) => Do("POST", url, headers, body);

    public Response Do(string method, string url, Headers headers, string body) => Do(method, url, headers, Encoding.UTF8.GetBytes(body));

    /// <summary>Sends one request with the headers in the given order, content-length first and cookie right after accept-language. Headers with an empty value are left out. A transport error throws.</summary>
    public Response Do(string method, string url, Headers headers, byte[] body)
    {
        var values = new Dictionary<string, string>();
        var order = new List<string> { "content-length" };
        foreach (var (name, value) in headers)
        {
            if (value == "")
            {
                continue;
            }
            values[name] = value;
            order.Add(name);
            if (name == "accept-language")
            {
                order.Add("cookie");
            }
        }
        var res = session.RequestBinary(method, url, body, values, headerOrder: order);
        Console.WriteLine($"{method} {url} {res.StatusCode}");
        return new Response(res.StatusCode, res.Url, res.Text, res.Content, res.Headers);
    }

    /// <summary>Turns redirect following on or off for later requests.</summary>
    public void FollowRedirects(bool follow) => session.SetFollowRedirects(follow);

    /// <summary>The first of the named cookies stored for the URL's host, or "".</summary>
    public string Cookie(string url, params string[] names)
    {
        var host = new Uri(url).Host;
        var cookies = session.GetCookies();
        foreach (var name in names)
        {
            foreach (var c in cookies)
            {
                var domain = c.Domain.TrimStart('.');
                if (c.Name == name && (host == domain || host.EndsWith("." + domain)))
                {
                    return c.Value;
                }
            }
        }
        return "";
    }

    /// <summary>Stores a cookie for the domain and its subdomains.</summary>
    public void SetCookie(string domain, string name, string value) => session.SetCookie(name, value, domain: domain, path: "/");
}

/// <summary>Headers are sent in this order.</summary>
public sealed class Headers : List<(string Name, string Value)>
{
    private const string SecChUa = "\"Google Chrome\";v=\"155\", \"Chromium\";v=\"155\", \"Not(A:Brand\";v=\"24\"";
    private const string SecChUaFull = "\"Google Chrome\";v=\"155.0.8059.39\", \"Chromium\";v=\"155.0.8059.39\", \"Not(A:Brand\";v=\"24.0.0.0\"";
    private const string SecChUaVersion = "\"155.0.8059.39\"";
    private const string Platform = "\"Windows\"";
    private const string AcceptDocument = "text/html,application/xhtml+xml,application/xml;q=0.9,image/jxl,image/avif,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.7";
    private const string AcceptImage = "image/jxl,image/avif,image/webp,image/apng,image/svg+xml,image/*,*/*;q=0.8";
    private const string AcceptEncoding = "gzip, deflate, br, zstd";

    /// <summary>A top-level page load: referer "" for a typed URL, else a same-origin link.</summary>
    public static Headers Navigate(string acceptLanguage, string referer) => new()
    {
        ("sec-ch-ua", SecChUa),
        ("sec-ch-ua-mobile", "?0"),
        ("sec-ch-ua-platform", Platform),
        ("upgrade-insecure-requests", "1"),
        ("user-agent", HttpClientWrapper.UserAgent),
        ("accept", AcceptDocument),
        ("sec-fetch-site", referer == "" ? "none" : "same-origin"),
        ("sec-fetch-mode", "navigate"),
        ("sec-fetch-user", "?1"),
        ("sec-fetch-dest", "document"),
        ("referer", referer),
        ("accept-encoding", AcceptEncoding),
        ("accept-language", acceptLanguage),
        ("priority", "u=0, i"),
    };

    /// <summary>An iframe document load.</summary>
    public static Headers Frame(string acceptLanguage, string site, string referer) => new()
    {
        ("sec-ch-ua", SecChUa),
        ("sec-ch-ua-mobile", "?0"),
        ("sec-ch-ua-platform", Platform),
        ("upgrade-insecure-requests", "1"),
        ("user-agent", HttpClientWrapper.UserAgent),
        ("accept", AcceptDocument),
        ("sec-fetch-site", site),
        ("sec-fetch-mode", "navigate"),
        ("sec-fetch-dest", "iframe"),
        ("referer", referer),
        ("accept-encoding", AcceptEncoding),
        ("accept-language", acceptLanguage),
        ("priority", "u=0, i"),
    };

    /// <summary>A &lt;script src&gt; load.</summary>
    public static Headers Script(string acceptLanguage, string site, string referer) => new()
    {
        ("sec-ch-ua-platform", Platform),
        ("user-agent", HttpClientWrapper.UserAgent),
        ("sec-ch-ua", SecChUa),
        ("sec-ch-ua-mobile", "?0"),
        ("accept", "*/*"),
        ("sec-fetch-site", site),
        ("sec-fetch-mode", "no-cors"),
        ("sec-fetch-dest", "script"),
        ("referer", referer),
        ("accept-encoding", AcceptEncoding),
        ("accept-language", acceptLanguage),
        ("priority", "u=1"),
    };

    /// <summary>A same-origin fetch from the page; a GET fetch has no origin.</summary>
    public static Headers Post(string acceptLanguage, string contentType, string origin, string referer) => new()
    {
        ("sec-ch-ua-platform", Platform),
        ("user-agent", HttpClientWrapper.UserAgent),
        ("sec-ch-ua", SecChUa),
        ("content-type", contentType),
        ("sec-ch-ua-mobile", "?0"),
        ("accept", "*/*"),
        ("origin", origin),
        ("sec-fetch-site", "same-origin"),
        ("sec-fetch-mode", "cors"),
        ("sec-fetch-dest", "empty"),
        ("referer", referer),
        ("accept-encoding", AcceptEncoding),
        ("accept-language", acceptLanguage),
        ("priority", "u=1, i"),
    };

    /// <summary>An &lt;img&gt; load; with an origin it is a CORS image.</summary>
    public static Headers Image(string acceptLanguage, string origin, string site, string referer) => new()
    {
        ("origin", origin),
        ("sec-ch-ua-platform", Platform),
        ("user-agent", HttpClientWrapper.UserAgent),
        ("sec-ch-ua", SecChUa),
        ("sec-ch-ua-mobile", "?0"),
        ("accept", AcceptImage),
        ("sec-fetch-site", site),
        ("sec-fetch-mode", origin == "" ? "no-cors" : "cors"),
        ("sec-fetch-dest", "image"),
        ("referer", referer),
        ("accept-encoding", AcceptEncoding),
        ("accept-language", acceptLanguage),
        ("priority", "i"),
    };

    /// <summary>The mfc fetch; version is x-kpsdk-v from the p.js result.</summary>
    public static Headers KasadaMfc(string acceptLanguage, string version, string referer) => new()
    {
        ("x-kpsdk-v", version),
        ("sec-ch-ua-platform", Platform),
        ("user-agent", HttpClientWrapper.UserAgent),
        ("x-kpsdk-h", "01"),
        ("sec-ch-ua", SecChUa),
        ("sec-ch-ua-mobile", "?0"),
        ("accept", "*/*"),
        ("sec-fetch-site", "same-origin"),
        ("sec-fetch-mode", "cors"),
        ("sec-fetch-dest", "empty"),
        ("referer", referer),
        ("accept-encoding", AcceptEncoding),
        ("accept-language", acceptLanguage),
        ("priority", "u=1, i"),
    };

    /// <summary>The /tl POST with the x-kpsdk-* headers from the API.</summary>
    public static Headers KasadaTl(string acceptLanguage, IReadOnlyDictionary<string, string> kasada, string origin, string referer) => new Headers
    {
        ("x-kpsdk-ct", kasada["x-kpsdk-ct"]),
        ("sec-ch-ua-platform", Platform),
        ("x-kpsdk-dt", kasada["x-kpsdk-dt"]),
        ("sec-ch-ua", SecChUa),
        ("x-kpsdk-im", kasada["x-kpsdk-im"]),
        ("sec-ch-ua-mobile", "?0"),
        ("x-kpsdk-v", kasada["x-kpsdk-v"]),
        ("user-agent", HttpClientWrapper.UserAgent),
        ("content-type", "application/octet-stream"),
        ("accept", "*/*"),
        ("origin", origin),
        ("sec-fetch-site", "same-origin"),
        ("sec-fetch-mode", "cors"),
        ("sec-fetch-dest", "empty"),
        ("referer", referer),
        ("accept-encoding", AcceptEncoding),
        ("accept-language", acceptLanguage),
        ("priority", "u=1, i"),
    }.With(kasada);

    /// <summary>The reese84 script, which the challenge page adds as an async script; Chrome sends no priority header for it.</summary>
    public static Headers ImpervaScript(string acceptLanguage, string referer) => new()
    {
        ("sec-ch-ua-platform", Platform),
        ("user-agent", HttpClientWrapper.UserAgent),
        ("sec-ch-ua", SecChUa),
        ("sec-ch-ua-mobile", "?0"),
        ("accept", "*/*"),
        ("sec-fetch-site", "same-origin"),
        ("sec-fetch-mode", "no-cors"),
        ("sec-fetch-dest", "script"),
        ("referer", referer),
        ("accept-encoding", AcceptEncoding),
        ("accept-language", acceptLanguage),
    };

    /// <summary>The reese84 POST; the script sets accept and content-type.</summary>
    public static Headers ImpervaPost(string acceptLanguage, string origin, string referer) => new()
    {
        ("sec-ch-ua-platform", Platform),
        ("user-agent", HttpClientWrapper.UserAgent),
        ("accept", "application/json; charset=utf-8"),
        ("sec-ch-ua", SecChUa),
        ("content-type", "text/plain; charset=utf-8"),
        ("sec-ch-ua-mobile", "?0"),
        ("origin", origin),
        ("sec-fetch-site", "same-origin"),
        ("sec-fetch-mode", "cors"),
        ("sec-fetch-dest", "empty"),
        ("referer", referer),
        ("accept-encoding", AcceptEncoding),
        ("accept-language", acceptLanguage),
        ("priority", "u=1, i"),
    };

    /// <summary>The Cloudflare challenge script. The challenge page asks for every client hint (Accept-CH), so the full set is sent.</summary>
    public static Headers ChallengeScript(string acceptLanguage, string referer) => new()
    {
        ("sec-ch-ua-full-version-list", SecChUaFull),
        ("sec-ch-ua-platform", Platform),
        ("sec-ch-ua", SecChUa),
        ("sec-ch-ua-bitness", "\"64\""),
        ("sec-ch-ua-model", "\"\""),
        ("sec-ch-ua-mobile", "?0"),
        ("sec-ch-ua-arch", "\"x86\""),
        ("sec-ch-ua-full-version", SecChUaVersion),
        ("user-agent", HttpClientWrapper.UserAgent),
        ("sec-ch-ua-platform-version", "\"19.0.0\""),
        ("accept", "*/*"),
        ("sec-fetch-site", "same-origin"),
        ("sec-fetch-mode", "no-cors"),
        ("sec-fetch-dest", "script"),
        ("referer", referer),
        ("accept-encoding", AcceptEncoding),
        ("accept-language", acceptLanguage),
    };

    /// <summary>One request of a Cloudflare step, by its Purpose, with the request's own headers (cf-chl, the form's origin and referer, and the like) set on top. frameUrl is the challenge iframe's URL.</summary>
    public static Headers Cloudflare(CloudflareHttpRequest r, string acceptLanguage, string pageUrl, string frameUrl)
    {
        var page = new Uri(pageUrl);
        var target = new Uri(r.Url);
        var h = r.Purpose switch
        {
            "form" => new Headers
            {
                ("sec-ch-ua", SecChUa),
                ("sec-ch-ua-mobile", "?0"),
                ("sec-ch-ua-full-version", SecChUaVersion),
                ("sec-ch-ua-arch", "\"x86\""),
                ("sec-ch-ua-full-version-list", SecChUaFull),
                ("sec-ch-ua-platform", Platform),
                ("sec-ch-ua-platform-version", "\"19.0.0\""),
                ("sec-ch-ua-bitness", "\"64\""),
                ("sec-ch-ua-model", "\"\""),
                ("origin", ""),
                ("content-type", "application/x-www-form-urlencoded"),
                ("upgrade-insecure-requests", "1"),
                ("user-agent", HttpClientWrapper.UserAgent),
                ("referer", ""),
            },
            "iframe" => Frame(acceptLanguage, "cross-site", "").StorageAccess(),
            "image" => Image(acceptLanguage, "", "same-origin", frameUrl).StorageAccess(),
            "pat" => new Headers
            {
                ("cache-control", "max-age=0"),
                ("sec-ch-ua-platform", Platform),
                ("user-agent", HttpClientWrapper.UserAgent),
                ("sec-ch-ua", SecChUa),
                ("sec-ch-ua-mobile", "?0"),
                ("accept", "*/*"),
                ("sec-fetch-site", "same-origin"),
                ("sec-fetch-mode", "cors"),
                ("sec-fetch-dest", "empty"),
                ("sec-fetch-storage-access", "active"),
                ("referer", frameUrl),
                ("accept-encoding", AcceptEncoding),
                ("accept-language", acceptLanguage),
                ("priority", "u=1, i"),
            },
            _ when target.Authority == page.Authority => new Headers // challenge POST from the page
            {
                ("sec-ch-ua-full-version-list", SecChUaFull),
                ("sec-ch-ua-platform", Platform),
                ("sec-ch-ua", SecChUa),
                ("sec-ch-ua-bitness", "\"64\""),
                ("cf-chl-ra", "0"),
                ("sec-ch-ua-mobile", "?0"),
                ("sec-ch-ua-model", "\"\""),
                ("sec-ch-ua-arch", "\"x86\""),
                ("sec-ch-ua-full-version", SecChUaVersion),
                ("user-agent", HttpClientWrapper.UserAgent),
                ("content-type", "text/plain;charset=UTF-8"),
                ("cf-chl", r.Headers.GetValueOrDefault("cf-chl", "")),
                ("sec-ch-ua-platform-version", "\"19.0.0\""),
                ("accept", "*/*"),
                ("origin", page.GetLeftPart(UriPartial.Authority)),
                ("sec-fetch-site", "same-origin"),
                ("sec-fetch-mode", "cors"),
                ("sec-fetch-dest", "empty"),
                ("referer", pageUrl),
                ("accept-encoding", AcceptEncoding),
                ("accept-language", acceptLanguage),
                ("priority", "u=1, i"),
            },
            _ => new Headers // challenge POST from the challenge iframe
            {
                ("sec-ch-ua-platform", Platform),
                ("user-agent", HttpClientWrapper.UserAgent),
                ("sec-ch-ua", SecChUa),
                ("content-type", "text/plain;charset=UTF-8"),
                ("cf-chl", r.Headers.GetValueOrDefault("cf-chl", "")),
                ("cf-chl-ra", "0"),
                ("sec-ch-ua-mobile", "?0"),
                ("accept", "*/*"),
                ("origin", target.GetLeftPart(UriPartial.Authority)),
                ("sec-fetch-site", "same-origin"),
                ("sec-fetch-mode", "cors"),
                ("sec-fetch-dest", "empty"),
                ("sec-fetch-storage-access", "active"),
                ("referer", frameUrl),
                ("accept-encoding", AcceptEncoding),
                ("accept-language", acceptLanguage),
                ("priority", "u=1, i"),
            },
        };
        return h.With(r.Headers);
    }

    /// <summary>Puts pragma and cache-control (no-cache) first.</summary>
    public Headers NoCache()
    {
        var output = new Headers { ("pragma", "no-cache"), ("cache-control", "no-cache") };
        output.AddRange(this);
        return output;
    }

    /// <summary>Adds sec-fetch-storage-access after sec-fetch-dest, as Chrome sends it for requests to a cross-site frame's host.</summary>
    public Headers StorageAccess()
    {
        var output = new Headers();
        output.AddRange(this);
        output.Insert(FindIndex(p => p.Name == "sec-fetch-dest") + 1, ("sec-fetch-storage-access", "active"));
        return output;
    }

    /// <summary>Sets each extra header: a name already present keeps its place, new names follow in name order.</summary>
    public Headers With(IReadOnlyDictionary<string, string> extra)
    {
        var output = new Headers();
        output.AddRange(this);
        foreach (var name in extra.Keys.Order(StringComparer.Ordinal))
        {
            var lower = name.ToLowerInvariant();
            var i = output.FindIndex(p => p.Name == lower);
            if (i < 0)
            {
                output.Add((lower, extra[name]));
            }
            else
            {
                output[i] = (lower, extra[name]);
            }
        }
        return output;
    }
}
