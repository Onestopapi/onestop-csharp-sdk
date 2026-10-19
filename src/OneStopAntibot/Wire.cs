using System.Buffers;
using System.Collections;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OneStop;

internal static class Wire
{
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = WireEncoder.Instance,
        Converters =
        {
            new NullAsEmptyString(), new NullAsDefault<bool>(), new NullAsDefault<int>(),
            new NullAsEmpty<IReadOnlyDictionary<string, string>, Dictionary<string, string>>(),
            new NullAsEmpty<IReadOnlyList<CloudflareHttpRequest>, List<CloudflareHttpRequest>>(),
        },
    };

    public static string? Omit(string value) => value is "" ? null : value;

    // Maps are encoded with their keys in Go's order (UTF-8 bytes, not UTF-16 ordinal) at every depth.
    public static object? Sorted(object? value) => value switch
    {
        IDictionary map => new SortedDictionary<string, object?>(map.Keys.Cast<string>().ToDictionary(key => key, key => Sorted(map[key])), KeyOrder),
        IEnumerable<object?> list => list.Select(Sorted).ToList(),
        _ => value,
    };

    private static readonly Comparer<string> KeyOrder =
        Comparer<string>.Create((a, b) => Encoding.UTF8.GetBytes(a).AsSpan().SequenceCompareTo(Encoding.UTF8.GetBytes(b)));

    // Escapes only control characters, " and \, and U+2028 and U+2029; <, > and & stay as they are.
    private sealed class WireEncoder : JavaScriptEncoder
    {
        public static readonly WireEncoder Instance = new();

        public override int MaxOutputCharactersPerInputCharacter => 6;

        public override bool WillEncode(int unicodeScalar) => unicodeScalar < 0x20 || unicodeScalar is '"' or '\\' or 0x2028 or 0x2029;

        public override unsafe int FindFirstCharacterToEncode(char* text, int textLength)
        {
            var span = new ReadOnlySpan<char>(text, textLength);
            for (var i = 0; i < span.Length;)
            {
                if (Rune.DecodeFromUtf16(span[i..], out var rune, out var consumed) != OperationStatus.Done || WillEncode(rune.Value))
                    return i;
                i += consumed;
            }
            return -1;
        }

        public override unsafe bool TryEncodeUnicodeScalar(int unicodeScalar, char* buffer, int bufferLength, out int numberOfCharactersWritten)
        {
            var text = unicodeScalar switch
            {
                '"' => "\\\"",
                '\\' => "\\\\",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                '\b' => "\\b",
                '\f' => "\\f",
                _ when WillEncode(unicodeScalar) => $"\\u{unicodeScalar:x4}",
                _ => char.ConvertFromUtf32(unicodeScalar),
            };
            numberOfCharactersWritten = 0;
            if (text.Length > bufferLength)
                return false;
            text.AsSpan().CopyTo(new Span<char>(buffer, bufferLength));
            numberOfCharactersWritten = text.Length;
            return true;
        }
    }

    // A JSON null leaves the empty value in place.
    private sealed class NullAsEmptyString : JsonConverter<string>
    {
        public override bool HandleNull => true;

        public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.TokenType == JsonTokenType.Null ? "" : reader.GetString()!;

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value);
    }

    private sealed class NullAsDefault<T> : JsonConverter<T> where T : struct
    {
        private static readonly JsonConverter<T> Inner = (JsonConverter<T>)JsonSerializerOptions.Default.GetConverter(typeof(T));

        public override bool HandleNull => true;

        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.TokenType == JsonTokenType.Null ? default : Inner.Read(ref reader, typeToConvert, options);

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
            Inner.Write(writer, value, options);
    }

    private sealed class NullAsEmpty<T, TConcrete> : JsonConverter<T> where TConcrete : T, new()
    {
        public override bool HandleNull => true;

        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.TokenType == JsonTokenType.Null ? new TConcrete() : JsonSerializer.Deserialize<TConcrete>(ref reader, options)!;

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
            JsonSerializer.Serialize(writer, value, value!.GetType(), options);
    }
}

internal sealed class Envelope
{
    public bool Success { get; set; }
    public string Error { get; set; } = "";
}

internal sealed class AkamaiWire
{
    public string? Referer { get; init; }
    public string? Site { get; init; }
    public string Abck { get; init; } = "";
    public string Bmsz { get; init; } = "";
    public string? UserAgent { get; init; }
    public string? AcceptLanguage { get; init; }
    public string? Ip { get; init; }
    public string? Script { get; init; }
    public string ScriptUrl { get; init; } = "";
    public string? Context { get; init; }
}

internal sealed class SbsdWire
{
    public string? Referer { get; init; }
    public string? Site { get; init; }
    public string? UserAgent { get; init; }
    public string? AcceptLanguage { get; init; }
    public string? Ip { get; init; }
    public string Uuid { get; init; } = "";
    public string SbsdCookie { get; init; } = "";
    public string? Script { get; init; }
    public string Mode { get; init; } = "";
    public string? Context { get; init; }
}

internal sealed class PixelWire
{
    public string? Site { get; init; }
    public string? UserAgent { get; init; }
    public string? Referer { get; init; }
    public string? AcceptLanguage { get; init; }
    public string? Ip { get; init; }
    public string Html { get; init; } = "";
    public string Script { get; init; } = "";
    public string? Context { get; init; }
}

internal sealed class BmpWire
{
    public string BundleId { get; init; } = "";
    public string Version { get; init; } = "";
    public string Device { get; init; } = "";
    public string? Nonce { get; init; }
    public string? Checksum { get; init; }
    public string? ServerSideSignal { get; init; }
    public string? Token { get; init; }
    public int? Difficulty { get; init; }
    public string? IosVersion { get; init; }
    public string? Uuid { get; init; }
    public IDictionary<string, string>? RealValues { get; init; }
    public string? Script { get; init; }
}

internal sealed class DatadomeTagsWire
{
    public string? Site { get; init; }
    public string? UserAgent { get; init; }
    public string? AcceptLanguage { get; init; }
    public string? Ip { get; init; }
    public string? Referer { get; init; }
    public string Event { get; init; } = "";
    public string SiteKey { get; init; } = "";
    public string Version { get; init; } = "";
    public string Cookie { get; init; } = "";
    public string? Context { get; init; }
}

internal sealed class DatadomeInterstitialWire
{
    public string? Site { get; init; }
    public string? UserAgent { get; init; }
    public string? AcceptLanguage { get; init; }
    public string? Ip { get; init; }
    public string? Referer { get; init; }
    public string Script { get; init; } = "";
    public string Html { get; init; } = "";
    public string PageResponse { get; init; } = "";
    public string? Context { get; init; }
}

internal sealed class DatadomeSliderWire
{
    public string? Site { get; init; }
    public string? UserAgent { get; init; }
    public string? AcceptLanguage { get; init; }
    public string? Ip { get; init; }
    public string? Referer { get; init; }
    public string Script { get; init; } = "";
    public string Html { get; init; } = "";
    public string PageResponse { get; init; } = "";
    public string BackgroundImage { get; init; } = "";
    public string PuzzlePiece { get; init; } = "";
    public string? Context { get; init; }
}

internal sealed class CloudflareWire
{
    public string? Site { get; init; }
    public string? UserAgent { get; init; }
    public string? Referer { get; init; }
    public string? AcceptLanguage { get; init; }
    public string? Ip { get; init; }
    public string? Html { get; init; }
    public string? Script { get; init; }
    public string? ScriptUrl { get; init; }
    public object? Response { get; init; }
    public string? Context { get; init; }
    public string? RayId { get; init; }
    public string? BytecodeRayId { get; init; }
    public string? IframeUrl { get; init; }
    public int? ImageHeight { get; init; }
    public int? ImageWidth { get; init; }
    public int? PatStatus { get; init; }
    public string? Action { get; init; }
    public string? CData { get; init; }
}

internal sealed class KasadaPScriptWire
{
    public string Referer { get; init; } = "";
    public string Site { get; init; } = "";
    public string UserAgent { get; init; } = "";
    public string AcceptLanguage { get; init; } = "";
    public string Ip { get; init; } = "";
    public string Script { get; init; } = "";
}

internal sealed class KasadaTlWire
{
    public string? Referer { get; init; }
    public string? Site { get; init; }
    public string? UserAgent { get; init; }
    public string? AcceptLanguage { get; init; }
    public string? Ip { get; init; }
    public string IpsUrl { get; init; } = "";
    public string Script { get; init; } = "";
}

internal sealed class ImpervaWire
{
    public string? Site { get; init; }
    public string? UserAgent { get; init; }
    public string? AcceptLanguage { get; init; }
    public string? Ip { get; init; }
    public string? Referer { get; init; }
    public string ScriptUrl { get; init; } = "";
    public string Script { get; init; } = "";
}

internal sealed class ImpervaUtmvcWire
{
    public string? Site { get; init; }
    public string? UserAgent { get; init; }
    public string? AcceptLanguage { get; init; }
    public string? Ip { get; init; }
    public string? Referer { get; init; }
    public string ScriptUrl { get; init; } = "";
    public string Script { get; init; } = "";
    // Present selects utmvc; absent selects reese84.
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public IReadOnlyList<ImpervaSessionCookie> IncapSessionCookies { get; init; } = [];
}
