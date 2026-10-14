namespace OneStop;

/// <summary>The browser identity sent with every challenge call. Create it with <see cref="OneStopClient.Session"/>.</summary>
public sealed class Session
{
    private readonly OneStopClient _client;

    internal Session(OneStopClient client, SessionOptions identity)
    {
        _client = client;
        Site = identity.Site ?? "";
        Referer = identity.Referer ?? "";
        UserAgent = identity.UserAgent ?? "";
        AcceptLanguage = identity.AcceptLanguage ?? "";
        Ip = identity.Ip ?? "";
    }

    public string Site { get; }
    public string Referer { get; }
    public string UserAgent { get; }
    public string AcceptLanguage { get; }
    public string Ip { get; }

    public Task<AkamaiResponse> AkamaiSensorAsync(AkamaiSensorCall call, CancellationToken cancellationToken = default) =>
        Generate<AkamaiResponse>("Akamai", "akamai", new AkamaiWire
        {
            Referer = Wire.Omit(Referer),
            Site = Wire.Omit(Site),
            Abck = call.Abck,
            Bmsz = call.Bmsz,
            UserAgent = Wire.Omit(UserAgent),
            AcceptLanguage = Wire.Omit(AcceptLanguage),
            Ip = Wire.Omit(Ip),
            Script = Wire.Omit(call.Script),
            ScriptUrl = call.ScriptUrl,
            Context = Wire.Omit(call.Context),
        }, cancellationToken);

    public Task<SbsdResponse> SbsdPayloadAsync(SbsdCall call, CancellationToken cancellationToken = default) =>
        Generate<SbsdResponse>("Sbsd", "sbsd", new SbsdWire
        {
            Referer = Wire.Omit(Referer),
            Site = Wire.Omit(Site),
            UserAgent = Wire.Omit(UserAgent),
            AcceptLanguage = Wire.Omit(AcceptLanguage),
            Ip = Wire.Omit(Ip),
            Uuid = call.Uuid,
            SbsdCookie = call.SbsdCookie,
            Script = Wire.Omit(call.Script),
            Mode = call.Mode,
            Context = Wire.Omit(call.Context),
        }, cancellationToken);

    public Task<AkamaiResponse> PixelAsync(PixelCall call, CancellationToken cancellationToken = default) =>
        Generate<AkamaiResponse>("Akamai", "pixel", new PixelWire
        {
            Site = Wire.Omit(Site),
            UserAgent = Wire.Omit(UserAgent),
            Referer = Wire.Omit(Referer),
            AcceptLanguage = Wire.Omit(AcceptLanguage),
            Ip = Wire.Omit(Ip),
            Html = call.Html,
            Script = call.Script,
            Context = Wire.Omit(call.Context),
        }, cancellationToken);

    /// <summary>Generates an Akamai BMP (mobile SDK) payload.</summary>
    public Task<AkamaiResponse> BmpAsync(BmpCall call, CancellationToken cancellationToken = default) =>
        Generate<AkamaiResponse>("Akamai", "bmp", new BmpWire
        {
            BundleId = call.BundleId,
            Version = call.Version,
            Device = call.Device,
            Nonce = Wire.Omit(call.Nonce),
            Checksum = Wire.Omit(call.Checksum),
            ServerSideSignal = Wire.Omit(call.ServerSideSignal),
            Token = Wire.Omit(call.Token),
            Difficulty = call.Difficulty,
            IosVersion = Wire.Omit(call.IosVersion),
            Uuid = Wire.Omit(call.Uuid),
            RealValues = call.RealValues is { Count: > 0 } ? new SortedDictionary<string, string>(call.RealValues, StringComparer.Ordinal) : null,
            Script = Wire.Omit(call.Script),
        }, cancellationToken);

    public Task<DatadomeResponse> DatadomeTagsAsync(DatadomeTagsCall call, CancellationToken cancellationToken = default) =>
        Generate<DatadomeResponse>("DatadomeTags", "datadomeTags", new DatadomeTagsWire
        {
            Site = Wire.Omit(Site),
            UserAgent = Wire.Omit(UserAgent),
            AcceptLanguage = Wire.Omit(AcceptLanguage),
            Ip = Wire.Omit(Ip),
            Referer = Wire.Omit(Referer),
            Event = call.Event,
            SiteKey = call.SiteKey,
            Version = call.Version,
            Cookie = call.Cookie,
            Context = Wire.Omit(call.Context),
        }, cancellationToken);

    public Task<DatadomeResponse> DatadomeInterstitialAsync(DatadomeInterstitialCall call, CancellationToken cancellationToken = default) =>
        Generate<DatadomeResponse>("DatadomeInterstitial", "datadomeInterstitial", new DatadomeInterstitialWire
        {
            Site = Wire.Omit(Site),
            UserAgent = Wire.Omit(UserAgent),
            AcceptLanguage = Wire.Omit(AcceptLanguage),
            Ip = Wire.Omit(Ip),
            Referer = Wire.Omit(Referer),
            Script = call.Script,
            Html = call.Html,
            PageResponse = call.PageResponse,
            Context = Wire.Omit(call.Context),
        }, cancellationToken);

    /// <summary>Returns the check request's query string as <see cref="DatadomeResponse.Payload"/>.</summary>
    public Task<DatadomeResponse> DatadomeSliderAsync(DatadomeSliderCall call, CancellationToken cancellationToken = default) =>
        Generate<DatadomeResponse>("DatadomeSlider", "datadomeSlider", new DatadomeSliderWire
        {
            Site = Wire.Omit(Site),
            UserAgent = Wire.Omit(UserAgent),
            AcceptLanguage = Wire.Omit(AcceptLanguage),
            Ip = Wire.Omit(Ip),
            Referer = Wire.Omit(Referer),
            Script = call.Script,
            Html = call.Html,
            PageResponse = call.PageResponse,
            BackgroundImage = call.BackgroundImage,
            PuzzlePiece = call.PuzzlePiece,
            Context = Wire.Omit(call.Context),
        }, cancellationToken);

    /// <summary>Runs one Cloudflare step; the API detects the challenge type.</summary>
    public Task<CloudflareResponse> CloudflareStepAsync(CloudflareCall call, CancellationToken cancellationToken = default) =>
        Cloudflare("Cloudflare", call, cancellationToken);

    public Task<CloudflareResponse> CloudflareTurnstileAsync(CloudflareCall call, CancellationToken cancellationToken = default) =>
        Cloudflare("CloudflareTurnstile", call, cancellationToken);

    public Task<CloudflareResponse> CloudflareWafAsync(CloudflareCall call, CancellationToken cancellationToken = default) =>
        Cloudflare("CloudflareWaf", call, cancellationToken);

    public Task<CloudflareResponse> CloudflareJsdAsync(CloudflareCall call, CancellationToken cancellationToken = default) =>
        Cloudflare("CloudflareJsd", call, cancellationToken);

    public Task<CloudflareResponse> CloudflarePrecursorAsync(CloudflareCall call, CancellationToken cancellationToken = default) =>
        Cloudflare("CloudflarePrecursor", call, cancellationToken);

    /// <summary>Extracts the version and CD inputs from raw p.js source.</summary>
    public Task<KasadaPScriptResponse> KasadaPScriptAsync(string script, CancellationToken cancellationToken = default) =>
        Generate<KasadaPScriptResponse>("Kasada", "kasadaPscript", new KasadaPScriptWire
        {
            Referer = Referer,
            Site = Site,
            UserAgent = UserAgent,
            AcceptLanguage = AcceptLanguage,
            Ip = Ip,
            Script = script,
        }, cancellationToken);

    /// <summary>Generates the TL body from the ips.js URL and raw source.</summary>
    public Task<KasadaResponse> KasadaTlAsync(string scriptUrl, string script, CancellationToken cancellationToken = default) =>
        Generate<KasadaResponse>("Kasada", "kasada", new KasadaTlWire
        {
            Referer = Wire.Omit(Referer),
            Site = Wire.Omit(Site),
            UserAgent = Wire.Omit(UserAgent),
            AcceptLanguage = Wire.Omit(AcceptLanguage),
            Ip = Wire.Omit(Ip),
            IpsUrl = scriptUrl,
            Script = script,
        }, cancellationToken);

    private Task<CloudflareResponse> Cloudflare(string mode, CloudflareCall call, CancellationToken cancellationToken) =>
        Generate<CloudflareResponse>(mode, "cloudflare", new CloudflareWire
        {
            Site = Wire.Omit(Site),
            UserAgent = Wire.Omit(UserAgent),
            Referer = Wire.Omit(Referer),
            AcceptLanguage = Wire.Omit(AcceptLanguage),
            Ip = Wire.Omit(Ip),
            Html = Wire.Omit(call.Html),
            Script = Wire.Omit(call.Script),
            ScriptUrl = Wire.Omit(call.ScriptUrl),
            Response = call.Response,
            Context = Wire.Omit(call.Context),
            RayId = Wire.Omit(call.RayId),
            BytecodeRayId = Wire.Omit(call.BytecodeRayId),
            IframeUrl = Wire.Omit(call.IframeUrl),
            ImageHeight = call.ImageHeight,
            ImageWidth = call.ImageWidth,
            PatStatus = call.PatStatus,
            Action = Wire.Omit(call.Action),
            CData = Wire.Omit(call.CData),
        }, cancellationToken);

    private Task<T> Generate<T>(string mode, string provider, object request, CancellationToken cancellationToken)
        where T : OneStopResponse, new() =>
        _client.GenerateAsync<T>(mode, provider, request, cancellationToken);
}
