using Examples;

Func<Task>? example = args.FirstOrDefault() switch
{
    "akamai" => Akamai.Run,
    "sbsd" => Sbsd.Run,
    "datadome_interstitial" => DatadomeInterstitial.Run,
    "datadome_slider" => DatadomeSlider.Run,
    "cloudflare_waf" => CloudflareWaf.Run,
    "cloudflare_precursor" => CloudflarePrecursor.Run,
    "cloudflare_turnstile" => CloudflareTurnstile.Run,
    "kasada" => Kasada.Run,
    "imperva" => Imperva.Run,
    _ => null,
};
if (example == null)
{
    Console.Error.WriteLine("usage: dotnet run -- <example>, e.g. akamai");
    return 2;
}
try
{
    await example();
    return 0;
}
catch (Exception e)
{
    var text = e is OneStop.OneStopException ? e.ToString() : e.Message;
    Console.Error.WriteLine(text.StartsWith("FAIL ") ? text : "FAIL " + text);
    return 1;
}
