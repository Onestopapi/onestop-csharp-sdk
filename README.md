# OneStop C# SDK

C# client for the [OneStop Antibot](https://onestopantibot.com) API. The SDK calls `/generate` and returns typed results; your application fetches the target pages and scripts and sends the generated payloads.

Products: Akamai (sensor, SBSD, Pixel, sec-cpt), DataDome (tags, interstitial, slider), Cloudflare (WAF, Turnstile, JSD, precursor), Kasada (p.js, /tl, CD header), Imperva (reese84, utmvc).

## Install

```sh
dotnet add package OneStopAntibot
```

Requires .NET 8+. Package: [NuGet](https://www.nuget.org/packages/OneStopAntibot).

## Quick start

```csharp
var client = new OneStopClient(new OneStopOptions
{
    ApiKey = "YOUR_API_KEY",
    Identifier = "YOUR_IDENTIFIER", // your identifier or account name
});
var ip = await client.GetIpAsync(); // set OneStopOptions.IpHttpClient to your proxy so this is the proxy's IP
var session = client.Session(new SessionOptions
{
    Site = pageUrl, Referer = pageUrl, UserAgent = userAgent, AcceptLanguage = "en-US,en;q=0.9", Ip = ip,
});
var res = await session.AkamaiSensorAsync(new AkamaiSensorCall { Abck = abck, Bmsz = bmsz, Script = script, ScriptUrl = scriptUrl });
// POST res.Payload as sensor_data; send res.Context on the next call.
```

## Header order

Copy each request's header names, values and order from a browser recording (DevTools, powhttp, Charles) of that request, and set the order explicitly in your TLS client; the examples' headers only show how:

```csharp
// TlsClient
var request = new RequestBuilder().WithUrl(url).WithMethod(HttpMethod.Get)
    .WithHeader("user-agent", userAgent).WithHeader("accept", "*/*")
    .WithHeaderOrder(["user-agent", "accept"]);

// HttpCloak
session.RequestBinary("GET", url, [], new Dictionary<string, string> { ["user-agent"] = userAgent, ["accept"] = "*/*" },
    headerOrder: ["user-agent", "accept"]);
```

See [examples/](https://github.com/Onestopapi/onestop-csharp-sdk/tree/main/examples) for full flows per product and TLS client.

Documentation: [onestopantibot.com/dashboard/docs](https://onestopantibot.com/dashboard/docs)
