using System.Text.RegularExpressions;

internal static partial class TwitchFonts
{
    private const string TwitchUrl = "https://www.twitch.tv/";
    // Twitch serves its stylesheets only to browser user agents.
    private const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/141.0.0.0 Safari/537.36";
    // Web font families in the chat font stack, and the weights chat renders.
    private static readonly string[] Families = ["Noto Sans Arabic", "Inter", "Roobert"];
    private static readonly int[] Weights = [HarfBuzzInstancer.RegularWeight, HarfBuzzInstancer.BoldWeight];
    // src formats Chrome loads; it uses the first supported entry.
    private static readonly HashSet<string> SupportedFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        "woff2", "woff", "truetype", "opentype", "woff2-variations", "woff-variations", "truetype-variations", "opentype-variations",
    };
    // URL filename words that describe a file's encoding rather than its unicode-range subset.
    private static readonly HashSet<string> NonSubsetWords = new(StringComparer.OrdinalIgnoreCase) { "variable", "wghtonly", "normal", "italic" };

    private sealed record FontFace(string Family, int MinWeight, int MaxWeight, string Style, Uri Url, string? UnicodeRange);

    internal static async Task<List<ManifestEntry>> Download(string? Stylesheet, string Output)
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        var faces = new List<FontFace>();
        foreach (var (css, baseUrl) in await LoadStylesheets(http, Stylesheet))
            faces.AddRange(ParseFontFaces(css, baseUrl));
        Directory.CreateDirectory(Output);
        var downloads = new Dictionary<Uri, Task<byte[]>>();
        var entries = new List<ManifestEntry>();
        foreach (var family in Families)
            foreach (var weight in Weights)
            {
                var matches = faces
                    .Where(face => face.Family == family && face.Style == "normal" && face.MinWeight <= weight && weight <= face.MaxWeight)
                    .ToList();
                if (matches.Count == 0)
                    throw new InvalidDataException($"Twitch CSS has no {family} {weight} font face.");
                foreach (var face in matches)
                {
                    // Inter is split into unicode-range subsets; name each by its subset.
                    var file = matches.Count == 1
                        ? $"{string.Concat(family.Split(' '))}-{ManifestEntry.WeightName(weight)}.ttf"
                        : $"{string.Concat(family.Split(' '))}-{weight}-{SubsetName(face)}.ttf";
                    if (!downloads.TryGetValue(face.Url, out var download))
                        downloads[face.Url] = download = http.GetByteArrayAsync(face.Url);
                    // Blink keeps the variable face and applies wght when it
                    // shapes. Static instancing rounds fractional GPOS deltas.
                    var font = FontFile.Decode(await download);
                    await File.WriteAllBytesAsync(Path.Combine(Output, file), font);
                    Console.WriteLine($"{face.Url} -> {file}");
                    entries.Add(new ManifestEntry(file, family, ManifestEntry.WeightName(weight), face.Url.ToString(), null, face.UnicodeRange, FontFile.Sha256(font)));
                }
            }
        WindowsFonts.WritePlatformMetrics(Output, entries);
        return entries;
    }

    private static async Task<List<(string Css, Uri? BaseUrl)>> LoadStylesheets(HttpClient Http, string? Stylesheet)
    {
        if (Stylesheet != null && !Stylesheet.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return [(await File.ReadAllTextAsync(Stylesheet), null)];
        var urls = new List<Uri>();
        if (Stylesheet != null)
            urls.Add(new Uri(Stylesheet));
        else
        {
            var page = await Http.GetStringAsync(TwitchUrl);
            urls.AddRange(LinkTag().Matches(page)
                .Where(link => link.Value.Contains("rel=\"stylesheet\"", StringComparison.OrdinalIgnoreCase))
                .Select(link => new Uri(new Uri(TwitchUrl), link.Groups["url"].Value))
                .Distinct());
            if (urls.Count == 0)
                throw new InvalidDataException($"{TwitchUrl} links no stylesheets.");
        }
        var sheets = new List<(string, Uri?)>();
        foreach (var url in urls)
        {
            Console.WriteLine($"Reading {url}");
            sheets.Add((await Http.GetStringAsync(url), url));
        }
        return sheets;
    }

    private static IEnumerable<FontFace> ParseFontFaces(string Css, Uri? BaseUrl)
    {
        foreach (Match rule in FontFaceRule().Matches(Css))
        {
            var descriptors = rule.Groups["body"].Value.Split(';')
                .Select(declaration => declaration.Split(':', 2))
                .Where(pair => pair.Length == 2)
                .ToDictionary(pair => pair[0].Trim().ToLowerInvariant(), pair => pair[1].Trim());
            if (!descriptors.TryGetValue("font-family", out var family) || !descriptors.TryGetValue("src", out var src))
                continue;
            var url = SrcEntry().Matches(src)
                .Where(entry => !entry.Groups["format"].Success || SupportedFormats.Contains(entry.Groups["format"].Value))
                .Select(entry => entry.Groups["url"].Value)
                .FirstOrDefault();
            if (url == null)
                continue;
            var (minWeight, maxWeight) = ParseWeight(descriptors.GetValueOrDefault("font-weight", "normal"));
            yield return new FontFace(family.Trim('"', '\''), minWeight, maxWeight, descriptors.GetValueOrDefault("font-style", "normal"),
                BaseUrl == null ? new Uri(url) : new Uri(BaseUrl, url), descriptors.GetValueOrDefault("unicode-range"));
        }
    }

    private static (int, int) ParseWeight(string Value)
    {
        var weights = Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(weight => weight switch { "normal" => HarfBuzzInstancer.RegularWeight, "bold" => HarfBuzzInstancer.BoldWeight, _ => int.Parse(weight) })
            .ToArray();
        return (weights[0], weights[^1]);
    }

    /// <summary>"inter-latin-ext-variable-wghtOnly-normal-29df4aa934ecc9462070.woff2" -&gt; "latin-ext"</summary>
    private static string SubsetName(FontFace Face)
    {
        var familyWords = Face.Family.Split(' ');
        var words = ContentHash().Replace(Path.GetFileNameWithoutExtension(Face.Url.AbsolutePath), "").Split('-')
            .Where(word => !NonSubsetWords.Contains(word) && !word.All(char.IsAsciiDigit)
                && !familyWords.Contains(word, StringComparer.OrdinalIgnoreCase));
        var subset = string.Join('-', words).ToLowerInvariant();
        return subset.Length > 0 ? subset : throw new InvalidDataException($"Cannot name the unicode-range subset of {Face.Url}.");
    }

    [GeneratedRegex("""<link[^>]*\shref="(?<url>[^"]+)"[^>]*>""")]
    private static partial Regex LinkTag();

    [GeneratedRegex(@"@font-face\s*\{(?<body>[^}]*)\}")]
    private static partial Regex FontFaceRule();

    [GeneratedRegex("""url\(\s*(?<q>["']?)(?<url>[^"')]+)\k<q>\s*\)(\s*format\(\s*(?<fq>["']?)(?<format>[^"')]+)\k<fq>\s*\))?""")]
    private static partial Regex SrcEntry();

    [GeneratedRegex("-[0-9a-f]{12,}$")]
    private static partial Regex ContentHash();
}
