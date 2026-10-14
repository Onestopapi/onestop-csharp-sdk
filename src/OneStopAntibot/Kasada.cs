using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OneStop;

internal static class KasadaCd
{
    public static string Build(KasadaCdCall call)
    {
        if (call.St <= 0 || call.Rst < call.St)
            throw new OneStopException("kasada: require positive ST and RST >= ST");
        if (call.Difficulty <= 0 || call.SubchallengeCount <= 0)
            throw new OneStopException("kasada: difficulty and subchallenge count must be positive");
        var platform = new List<byte>(Encoding.UTF8.GetBytes("tp-v2-input"));
        if (!call.Mobile)
        {
            var ct = Encoding.UTF8.GetBytes(call.Ct);
            if (ct.Length < 16)
                throw new OneStopException("kasada: desktop CT must contain at least 16 bytes");
            platform.AddRange(ct[..16]);
        }
        var id = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        var workTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - Random.Shared.Next(10, 51);
        var tail = $", {workTime.ToString(CultureInfo.InvariantCulture)}, {id}";
        if (call.CryptoCustomerKey != "")
            tail += ", " + call.CryptoCustomerKey;
        if (call.SeedSuffix != "")
            tail += ", " + call.SeedSuffix;
        platform.AddRange(Encoding.UTF8.GetBytes(tail));

        var hash = Sha256Hex(platform.ToArray());
        var answers = new long[call.SubchallengeCount];
        var target = (double)call.Difficulty / call.SubchallengeCount;
        for (var i = 0; i < answers.Length; i++)
        {
            for (long nonce = 1; ; nonce++)
            {
                var next = Sha256Hex(Encoding.UTF8.GetBytes($"{nonce.ToString(CultureInfo.InvariantCulture)}, {hash}"));
                var prefix = ulong.Parse(next.AsSpan(0, 13), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
                if (0x10000000000000 / (double)(prefix + 1) >= target)
                {
                    answers[i] = nonce;
                    hash = next;
                    break;
                }
            }
        }
        var duration = Math.Round((0.5 + Random.Shared.NextDouble() * 1.5) * 10, MidpointRounding.AwayFromZero) / 10;
        var d = call.Rst - call.St;

        return call.Mobile
            ? JsonSerializer.Serialize(new CdMobile(workTime, call.Rst, duration, id, call.St, answers, d), Wire.Json)
            : JsonSerializer.Serialize(new CdDesktop(workTime, id, answers, duration, d, call.St, call.Rst), Wire.Json);
    }

    private static string Sha256Hex(byte[] value) => Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();
}

// The x-kpsdk-cd header JSON; parameter order is the wire order.
internal sealed record CdMobile(long WorkTime, long Rst, double Duration, string Id, long St, long[] Answers, long D);

internal sealed record CdDesktop(long WorkTime, string Id, long[] Answers, double Duration, long D, long St, long Rst);
