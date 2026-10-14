using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using ZstdSharp;

namespace OneStop;

/// <summary>API connection settings. Browser identity belongs in <see cref="SessionOptions"/>.</summary>
public sealed class OneStopOptions
{
    public string ApiKey { get; set; } = "";

    /// <summary>Sent as the <c>User-Agent</c> on API requests.</summary>
    public string Identifier { get; set; } = "";

    /// <summary>Optional, sent as <c>x-sub-key</c>.</summary>
    public string? SubKey { get; set; }

    /// <summary>Defaults to <see cref="OneStopClient.DefaultEndpoint"/>.</summary>
    public string? Endpoint { get; set; }

    /// <summary><c>zstd</c> (default), <c>gzip</c> or <c>identity</c>.</summary>
    public string? Compression { get; set; } = "zstd";

    /// <summary>Whole-request deadline for every call. Zero or negative means 60 seconds.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>The API transport. The default ignores proxy environment variables.</summary>
    public HttpClient? HttpClient { get; set; }

    /// <summary>Used by <see cref="OneStopClient.GetIpAsync"/>, usually your target proxy. Defaults to <see cref="HttpClient"/>.</summary>
    public HttpClient? IpHttpClient { get; set; }

    /// <summary>Merged into every session created by this client.</summary>
    public SessionOptions? Defaults { get; set; }

    /// <summary>Sends every API call through this proxy URL, such as <c>http://127.0.0.1:8888</c>, on the SDK's own client (<see cref="HttpClient"/> is not used) and accepts its certificate. <see cref="OneStopClient.GetIpAsync"/> is unaffected.</summary>
    public string? DebugProxy { get; set; }
}

public sealed class SessionOptions
{
    /// <summary>Absolute page URL.</summary>
    public string? Site { get; set; }
    public string? Referer { get; set; }
    public string? UserAgent { get; set; }
    public string? AcceptLanguage { get; set; }
    public string? Ip { get; set; }
}

/// <summary>Sends API requests. Reuse one client across concurrent calls.</summary>
public sealed class OneStopClient
{
    public const string DefaultEndpoint = "https://onestopantibot.com/generate";

    private const string IpEndpoint = "https://ip.onestopantibot.com/";
    private const int ZstdLevel = 7;

    private static readonly HttpClient DefaultHttp = new(new SocketsHttpHandler
    {
        UseProxy = false,
        RequestHeaderEncodingSelector = (_, _) => Encoding.UTF8,
    })
    {
        Timeout = System.Threading.Timeout.InfiniteTimeSpan,
    };

    private readonly string _apiKey;
    private readonly string _identifier;
    private readonly string _subKey;
    private readonly string _endpoint;
    private readonly string _compression;
    private readonly TimeSpan _timeout;
    private readonly HttpClient _http;
    private readonly HttpClient _ipHttp;
    private readonly SessionOptions _defaults;

    /// <summary>Requires an API key and identifier.</summary>
    public OneStopClient(OneStopOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey) || string.IsNullOrWhiteSpace(options.Identifier))
            throw new OneStopException("onestop: APIKey and Identifier are required");
        _compression = string.IsNullOrEmpty(options.Compression) ? "zstd" : options.Compression;
        if (_compression is not ("zstd" or "gzip" or "identity"))
            throw new OneStopException("onestop: compression must be zstd, gzip or identity");
        _apiKey = options.ApiKey;
        _identifier = options.Identifier;
        _subKey = options.SubKey ?? "";
        _endpoint = string.IsNullOrEmpty(options.Endpoint) ? DefaultEndpoint : options.Endpoint;
        _timeout = options.Timeout > TimeSpan.Zero ? options.Timeout : TimeSpan.FromSeconds(60);
        _http = options.HttpClient ?? DefaultHttp;
        _ipHttp = options.IpHttpClient ?? _http;
        _defaults = options.Defaults ?? new SessionOptions();
        if (!string.IsNullOrEmpty(options.DebugProxy))
        {
            _http = new HttpClient(new SocketsHttpHandler
            {
                Proxy = DebugWebProxy(options.DebugProxy),
                SslOptions = { RemoteCertificateValidationCallback = (_, _, _, _) => true },
                RequestHeaderEncodingSelector = (_, _) => Encoding.UTF8,
            })
            {
                Timeout = System.Threading.Timeout.InfiniteTimeSpan,
            };
        }
    }

    /// <summary>Returns the public IP seen by <c>ip.onestopantibot.com</c>, using <see cref="OneStopOptions.IpHttpClient"/>.</summary>
    public async Task<string> GetIpAsync(CancellationToken cancellationToken = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_timeout);
        using var request = new HttpRequestMessage { Method = HttpMethod.Get };
        SetAuthHeaders(request);
        using var response = await SendAsync(_ipHttp, request, IpEndpoint, cancellationToken, deadline.Token).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.OK)
            throw OneStopException.Api((int)response.StatusCode, "", "IP lookup failed");
        try
        {
            var body = await response.Content.ReadAsStringAsync(deadline.Token).ConfigureAwait(false);
            return body.Trim();
        }
        catch (Exception e) when (!cancellationToken.IsCancellationRequested)
        {
            throw Failure("read response", e);
        }
    }

    /// <summary>Returns the client defaults merged with <paramref name="session"/>; non-empty session values win.</summary>
    public Session Session(SessionOptions session) => new(this, new SessionOptions
    {
        Site = NonEmpty(session.Site, _defaults.Site),
        Referer = NonEmpty(session.Referer, _defaults.Referer),
        UserAgent = NonEmpty(session.UserAgent, _defaults.UserAgent),
        AcceptLanguage = NonEmpty(session.AcceptLanguage, _defaults.AcceptLanguage),
        Ip = NonEmpty(session.Ip, _defaults.Ip),
    });

    /// <summary>Computes the <c>x-kpsdk-cd</c> header locally. No client or API key is needed.</summary>
    public static string KasadaCdHeader(KasadaCdCall call) => KasadaCd.Build(call);

    /// <summary>Solves the Akamai sec-cpt crypto challenge locally; returns the body to POST to <c>/_sec/verify?provider=&lt;provider&gt;</c> (crypto or adaptive). No client or API key is needed.</summary>
    public static string AkamaiSecCptPayload(AkamaiSecCptCall call) => AkamaiSecCpt.Build(call);

    internal async Task<T> GenerateAsync<T>(string mode, string provider, object request, CancellationToken cancellationToken)
        where T : OneStopResponse, new()
    {
        var json = new MemoryStream();
        try
        {
            JsonSerializer.Serialize(json, new Dictionary<string, object> { [provider + "Request"] = request }, Wire.Json);
        }
        catch (Exception e)
        {
            throw new OneStopException($"onestop: encode request: {e.Message}", 0, "", e);
        }
        json.WriteByte((byte)'\n');

        using var message = new HttpRequestMessage { Method = HttpMethod.Post, Content = new ByteArrayContent(Compress(json.ToArray())) };
        message.Content.Headers.TryAddWithoutValidation("Content-Type", "application/json; charset=utf-8");
        if (_compression != "identity")
            message.Content.Headers.TryAddWithoutValidation("Content-Encoding", _compression);
        message.Headers.TryAddWithoutValidation("Accept-Encoding", "zstd, gzip");
        message.Headers.TryAddWithoutValidation("x-mode", mode);
        SetAuthHeaders(message);

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_timeout);
        using var response = await SendAsync(_http, message, _endpoint, cancellationToken, deadline.Token).ConfigureAwait(false);
        var status = (int)response.StatusCode;
        var requestId = response.Headers.TryGetValues("RequestID", out var ids) ? ids.First() : "";
        var body = await ReadBodyAsync(response, cancellationToken, deadline.Token).ConfigureAwait(false);

        Envelope envelope;
        T? result = null;
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            envelope = root.Deserialize<Envelope>(Wire.Json) ?? new Envelope();
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(provider + "Response", out var value))
                result = value.Deserialize<T>(Wire.Json);
        }
        catch (JsonException)
        {
            throw OneStopException.Api(status, requestId, "invalid JSON response");
        }
        if (status != 200 || !envelope.Success || envelope.Error != "")
            throw OneStopException.Api(status, requestId, envelope.Error != "" ? envelope.Error : "request failed");
        result ??= new T();
        result.RequestId = requestId;
        if (result.Sleep > 0)
            await Task.Delay(result.Sleep, cancellationToken).ConfigureAwait(false);
        return result;
    }

    private byte[] Compress(byte[] data)
    {
        if (_compression == "zstd")
        {
            using var compressor = new Compressor(ZstdLevel);
            return compressor.Wrap(data).ToArray();
        }
        if (_compression == "gzip")
        {
            var buffer = new MemoryStream();
            using (var gzip = new GZipStream(buffer, CompressionLevel.Optimal))
                gzip.Write(data);
            return buffer.ToArray();
        }
        return data;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpClient http, HttpRequestMessage request, string url,
        CancellationToken cancellationToken, CancellationToken deadline)
    {
        try
        {
            request.RequestUri = new Uri(url);
            return await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline).ConfigureAwait(false);
        }
        catch (Exception e) when (!cancellationToken.IsCancellationRequested)
        {
            throw Failure("request", e);
        }
    }

    private async Task<byte[]> ReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken, CancellationToken deadline)
    {
        var raw = response.Content.Headers.TryGetValues("Content-Encoding", out var values) ? string.Join(", ", values) : "";
        var encoding = raw.Trim().ToLowerInvariant();
        if (encoding is not ("" or "identity" or "gzip" or "zstd"))
            throw new OneStopException($"onestop: unsupported response compression \"{raw}\"");
        byte[] body;
        try
        {
            body = await response.Content.ReadAsByteArrayAsync(deadline).ConfigureAwait(false);
        }
        catch (Exception e) when (!cancellationToken.IsCancellationRequested)
        {
            throw Failure("read response", e);
        }
        if (encoding is "" or "identity")
            return body;
        try
        {
            using Stream decoder = encoding == "gzip"
                ? new GZipStream(new MemoryStream(body), CompressionMode.Decompress)
                : new DecompressionStream(new MemoryStream(body));
            var output = new MemoryStream();
            decoder.CopyTo(output);
            return output.ToArray();
        }
        catch (Exception e)
        {
            throw new OneStopException($"onestop: decompress response: {e.Message}", 0, "", e);
        }
    }

    private static WebProxy DebugWebProxy(string url)
    {
        try
        {
            return new WebProxy(url);
        }
        catch (UriFormatException e)
        {
            throw new OneStopException($"onestop: request: {e.Message}", 0, "", e);
        }
    }

    private OneStopException Failure(string stage, Exception e)
    {
        var cause = e is OperationCanceledException ? $"timeout after {_timeout.TotalSeconds}s" : e.Message;
        return new OneStopException($"onestop: {stage}: {cause}", 0, "", e);
    }

    private void SetAuthHeaders(HttpRequestMessage request)
    {
        request.Headers.TryAddWithoutValidation("x-key", _apiKey);
        if (_subKey != "")
            request.Headers.TryAddWithoutValidation("x-sub-key", _subKey);
        request.Headers.TryAddWithoutValidation("User-Agent", _identifier);
    }

    private static string? NonEmpty(string? value, string? fallback) => string.IsNullOrEmpty(value) ? fallback : value;
}
