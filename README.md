# OneStop C# SDK

C# client for the [OneStop Antibot](https://onestopantibot.com) API. The SDK calls `/generate` and returns typed results; your application fetches the target pages and scripts and sends the generated payloads.

## Install

```sh
git clone https://github.com/Onestopapi/onestop-csharp-sdk.git
dotnet add reference onestop-csharp-sdk/src/OneStopAntibot/OneStopAntibot.csproj
```

Targets .NET 8+.

## Quick start

```csharp
var client = new OneStopClient(new OneStopOptions
{
    ApiKey = "YOUR_API_KEY",
    Identifier = "", // your identifier or account name
});
var ip = await client.GetIpAsync(); // set OneStopOptions.IpHttpClient to your proxy so this is the proxy's IP
var session = client.Session(new SessionOptions
{
    Site = pageUrl, Referer = pageUrl, UserAgent = userAgent, AcceptLanguage = "en-US,en;q=0.9", Ip = ip,
});
var res = await session.AkamaiSensorAsync(new AkamaiSensorCall { Abck = abck, Bmsz = bmsz, Script = script, ScriptUrl = scriptUrl });
// POST res.Payload as sensor_data; send res.Context on the next call.
```

See [examples/](examples) for full flows per product and TLS client.

Documentation: [onestopantibot.com/dashboard/docs](https://onestopantibot.com/dashboard/docs)
