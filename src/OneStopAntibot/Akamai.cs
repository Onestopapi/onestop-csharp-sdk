using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace OneStop;

internal static class AkamaiSecCpt
{
    private const string HexDigits = "0123456789abcdef";

    public static string Build(AkamaiSecCptCall call)
    {
        var tilde = call.Cookie.IndexOf('~');
        if (tilde < 0)
            throw new OneStopException("akamai: sec_cpt cookie must contain \"~\"");
        if (call.Token == "" || call.Nonce == "" || call.Timestamp <= 0)
            throw new OneStopException("akamai: token, nonce and a positive timestamp are required");
        if (call.Difficulty <= 0 || call.Count <= 0)
            throw new OneStopException("akamai: difficulty and count must be positive");
        var seed = call.Cookie[..tilde] + call.Timestamp.ToString(CultureInfo.InvariantCulture) + call.Nonce;
        var answers = new string[call.Count];
        for (var i = 0; i < answers.Length; i++)
        {
            var target = call.Difficulty + i;
            var prefix = seed + target.ToString(CultureInfo.InvariantCulture);
            while (true)
            {
                var answer = Candidate(Random.Shared.NextInt64(1L << 53) / (double)(1L << 53));
                var remainder = 0L;
                foreach (var b in SHA256.HashData(Encoding.UTF8.GetBytes(prefix + answer)))
                    remainder = ((remainder << 8) | b) % target;
                if (remainder == 0)
                {
                    answers[i] = answer;
                    break;
                }
            }
        }
        return "{\"token\": \"" + call.Token + "\", \"answers\": [\"" + string.Join("\",\"", answers) + "\"]}";
    }

    /// <summary>Formats <paramref name="value"/> in [0, 1) the way the browser's <c>Number.prototype.toString(16)</c> does.</summary>
    private static string Candidate(double value)
    {
        var delta = Math.Max(double.Epsilon, 0.5 * (Math.BitIncrement(value) - value));
        var fraction = value;
        var digits = new List<int>();
        while (fraction >= delta)
        {
            fraction *= 16;
            delta *= 16;
            var digit = (int)fraction;
            fraction -= digit;
            digits.Add(digit);
            if ((fraction > 0.5 || (fraction == 0.5 && (digit & 1) == 1)) && fraction + delta > 1)
            {
                while (digits.Count > 0 && digits[^1] == 15)
                    digits.RemoveAt(digits.Count - 1);
                if (digits.Count == 0)
                    return "1";
                digits[^1]++;
                break;
            }
        }
        return digits.Count == 0 ? "0" : "0." + string.Concat(digits.Select(d => HexDigits[d]));
    }
}
