using System.Text.Json.Serialization;

namespace OneStop;

/// <summary>Send <see cref="Script"/> on the first call and <see cref="Context"/>, without <see cref="Script"/>, on every later call.</summary>
public sealed class AkamaiSensorCall
{
    public string Abck { get; set; } = "";
    public string Bmsz { get; set; } = "";
    public string Script { get; set; } = "";
    public string ScriptUrl { get; set; } = "";
    public string Context { get; set; } = "";
}

/// <summary><see cref="Mode"/> is <c>f</c> (fingerprint, with <see cref="Script"/>) or <c>t</c> (event, with <see cref="Context"/>).</summary>
public sealed class SbsdCall
{
    public string Uuid { get; set; } = "";
    public string SbsdCookie { get; set; } = "";
    public string Script { get; set; } = "";
    public string Mode { get; set; } = "";
    public string Context { get; set; } = "";
}

/// <summary>The raw page HTML and Pixel script.</summary>
public sealed class PixelCall
{
    public string Html { get; set; } = "";
    public string Script { get; set; } = "";
    public string Context { get; set; } = "";
}

/// <summary>Android/iOS SDK inputs; no browser identity is sent.</summary>
public sealed class BmpCall
{
    public string BundleId { get; set; } = "";
    public string Version { get; set; } = "";
    public string Device { get; set; } = "";
    public string Nonce { get; set; } = "";
    public string Checksum { get; set; } = "";
    public string ServerSideSignal { get; set; } = "";
    public string Token { get; set; } = "";
    public int? Difficulty { get; set; }
    public string IosVersion { get; set; } = "";
    public string Uuid { get; set; } = "";
    public IDictionary<string, string>? RealValues { get; set; }
    public string Script { get; set; } = "";
}

/// <summary><see cref="Event"/> is <c>ch</c> or <c>le</c>.</summary>
public sealed class DatadomeTagsCall
{
    public string Event { get; set; } = "";
    public string SiteKey { get; set; } = "";
    public string Version { get; set; } = "";
    public string Cookie { get; set; } = "";
    public string Context { get; set; } = "";
}

public sealed class DatadomeInterstitialCall
{
    public string Script { get; set; } = "";
    public string Html { get; set; } = "";
    public string PageResponse { get; set; } = "";
    public string Context { get; set; } = "";
}

/// <summary><see cref="BackgroundImage"/> and <see cref="PuzzlePiece"/> are base64.</summary>
public sealed class DatadomeSliderCall
{
    public string Script { get; set; } = "";
    public string Html { get; set; } = "";
    public string PageResponse { get; set; } = "";
    public string BackgroundImage { get; set; } = "";
    public string PuzzlePiece { get; set; } = "";
    public string Context { get; set; } = "";
}

/// <summary>Inputs for the Cloudflare methods.</summary>
public sealed class CloudflareCall
{
    public string Html { get; set; } = "";
    public string Script { get; set; } = "";
    public string ScriptUrl { get; set; } = "";
    /// <summary>A string, <see cref="System.Text.Json.JsonElement"/>, JSON node or any serializable object, sent as is.</summary>
    public object? Response { get; set; }
    public string Context { get; set; } = "";
    public string RayId { get; set; } = "";
    public string BytecodeRayId { get; set; } = "";
    public string IframeUrl { get; set; } = "";
    public int? ImageHeight { get; set; }
    public int? ImageWidth { get; set; }
    public int? PatStatus { get; set; }
    public string Action { get; set; } = "";
    public string CData { get; set; } = "";
}

/// <summary>Inputs for <see cref="OneStopClient.AkamaiSecCptPayload"/>: the <c>sec_cpt</c> cookie value and the challenge JSON's fields.</summary>
public sealed class AkamaiSecCptCall
{
    public string Cookie { get; set; } = "";
    public string Token { get; set; } = "";
    public string Nonce { get; set; } = "";
    public long Timestamp { get; set; }
    public int Difficulty { get; set; }
    public int Count { get; set; }
}

/// <summary><see cref="CryptoCustomerKey"/>, <see cref="SeedSuffix"/>, <see cref="Difficulty"/> and <see cref="SubchallengeCount"/> come from <see cref="Session.KasadaPScriptAsync"/>.</summary>
public sealed class KasadaCdCall
{
    public long St { get; set; }
    public long Rst { get; set; }
    public bool Mobile { get; set; }
    public string Ct { get; set; } = "";
    public string CryptoCustomerKey { get; set; } = "";
    public string SeedSuffix { get; set; } = "";
    public int Difficulty { get; set; }
    public int SubchallengeCount { get; set; }
}

/// <summary>One <c>incap_ses_*</c> cookie of the page host, with its exact value, for <see cref="Session.ImpervaUtmvcAsync"/>.</summary>
public sealed class ImpervaSessionCookie
{
    public string Name { get; set; } = "";
    public string Value { get; set; } = "";
}

public abstract class OneStopResponse
{
    /// <summary>Identifies the call for support.</summary>
    [JsonIgnore]
    public string RequestId { get; internal set; } = "";

    /// <summary>Wait in milliseconds the API asked for; the SDK has already waited it before returning.</summary>
    public int Sleep { get; init; }
}

/// <summary>Returned by Akamai sensor, Pixel and BMP. For Pixel, <see cref="Payload"/> is the complete form body.</summary>
public sealed class AkamaiResponse : OneStopResponse
{
    public string Payload { get; init; } = "";
    public string Context { get; init; } = "";
}

public sealed class SbsdResponse : OneStopResponse
{
    public string Payload { get; init; } = "";
    public string Context { get; init; } = "";
}

/// <summary>Returned by the DataDome methods. <see cref="Payload"/> is already encoded.</summary>
public sealed class DatadomeResponse : OneStopResponse
{
    public string Payload { get; init; } = "";
    public string Context { get; init; } = "";
}

/// <summary>Values extracted from p.js; the crypto fields feed <see cref="OneStopClient.KasadaCdHeader"/>.</summary>
public sealed class KasadaPScriptResponse : OneStopResponse
{
    public string Version { get; init; } = "";
    public IReadOnlyDictionary<string, string> Headers { get; init; } = new Dictionary<string, string>();
    public int Difficulty { get; init; }
    public int SubchallengeCount { get; init; }
    public string SeedSuffix { get; init; } = "";
    public string CryptoCustomerKey { get; init; } = "";
    public int CacheLifetimeSeconds { get; init; }

    /// <summary>Builds the desktop <c>x-kpsdk-cd</c> header from the /tl response's <c>x-kpsdk-ct</c> value and <c>x-kpsdk-st</c> header (ms). Use <see cref="OneStopClient.KasadaCdHeader"/> for mobile.</summary>
    public string CdHeader(string ct, long st) => OneStopClient.KasadaCdHeader(new KasadaCdCall
    {
        St = st,
        Rst = Math.Max(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), st),
        Ct = ct,
        CryptoCustomerKey = CryptoCustomerKey,
        SeedSuffix = SeedSuffix,
        Difficulty = Difficulty,
        SubchallengeCount = SubchallengeCount,
    });
}

/// <summary>The base64 TL body and the headers to send with it.</summary>
public sealed class KasadaResponse : OneStopResponse
{
    public string Payload { get; init; } = "";
    public IReadOnlyDictionary<string, string> Headers { get; init; } = new Dictionary<string, string>();
}

/// <summary>The reese84 POST body (JSON text), or the <c>___utmvc</c> cookie value; send it as is.</summary>
public sealed class ImpervaResponse : OneStopResponse
{
    public string Payload { get; init; } = "";
}

/// <summary>The target requests for one challenge step.</summary>
public sealed class CloudflareResponse : OneStopResponse
{
    public IReadOnlyList<CloudflareHttpRequest> Requests { get; init; } = [];
    public string Token { get; init; } = "";
    public string Context { get; init; } = "";
}

/// <summary>One target request; <see cref="Purpose"/> is <c>challenge</c>, <c>iframe</c>, <c>image</c>, <c>pat</c> or <c>form</c>.</summary>
public sealed class CloudflareHttpRequest
{
    public string Purpose { get; init; } = "";
    public string Method { get; init; } = "";
    public string Url { get; init; } = "";
    public IReadOnlyDictionary<string, string> Headers { get; init; } = new Dictionary<string, string>();
    public string Body { get; init; } = "";
}
