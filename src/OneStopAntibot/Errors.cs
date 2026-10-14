namespace OneStop;

/// <summary>
/// An API, transport or local failure. For API failures <see cref="Exception.Message"/> is the API's message and
/// <see cref="ToString"/> reads <c>onestop: HTTP &lt;status&gt; (&lt;requestId&gt;): &lt;message&gt;</c>. Transport and local
/// errors have <see cref="StatusCode"/> 0 and the full reason in <see cref="Exception.Message"/>.
/// </summary>
public sealed class OneStopException : Exception
{
    public OneStopException(string message, int statusCode = 0, string requestId = "", Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        RequestId = requestId;
    }

    /// <summary>0 for transport and local errors.</summary>
    public int StatusCode { get; }

    public string RequestId { get; }

    public override string ToString() => StatusCode == 0 ? Message
        : RequestId != "" ? $"onestop: HTTP {StatusCode} ({RequestId}): {Message}"
        : $"onestop: HTTP {StatusCode}: {Message}";

    internal static OneStopException Api(int statusCode, string requestId, string message) => new(message, statusCode, requestId);
}
