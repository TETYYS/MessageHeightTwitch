using System.Text.Json;
using System.Text.Json.Serialization;

const string Usage = """
    Usage:
      FontConverter [--output DIR]                     Copy Windows fonts and download Twitch fonts
      FontConverter [--output DIR] windows [FONTDIR]   Copy referenced Windows fonts (default: %WINDIR%\Fonts)
      FontConverter [--output DIR] twitch [CSS]        Download Twitch fonts; CSS is a stylesheet URL or file
                                                       (default: stylesheets linked from twitch.tv)
      FontConverter convert INPUT OUTPUT [--weight N]  Unpack WOFF/WOFF2 and instance a variable font at weight N
    DIR defaults to the repository's Fonts directory.
    """;

try
{
    var arguments = args.ToList();
    var output = TakeOption(arguments, "--output");
    var command = arguments.Count > 0 ? arguments[0] : "all";
    var rest = arguments.Skip(1).ToList();
    switch (command)
    {
        case "all" when rest.Count == 0:
            output ??= DefaultOutput();
            WriteManifest(output, [.. WindowsFonts.Copy(DefaultWindowsFonts(), output), .. await TwitchFonts.Download(null, output)], Merge: false);
            break;
        case "windows" when rest.Count <= 1:
            output ??= DefaultOutput();
            WriteManifest(output, WindowsFonts.Copy(rest.Count == 1 ? rest[0] : DefaultWindowsFonts(), output), Merge: true);
            break;
        case "twitch" when rest.Count <= 1:
            output ??= DefaultOutput();
            WriteManifest(output, await TwitchFonts.Download(rest.Count == 1 ? rest[0] : null, output), Merge: true);
            break;
        case "convert" when output == null:
            var weight = TakeOption(rest, "--weight");
            if (rest.Count != 2)
                goto default;
            Convert(rest[0], rest[1], weight == null ? null : int.Parse(weight));
            break;
        default:
            Console.Error.WriteLine(Usage);
            return 2;
    }
    return 0;
}
catch (Exception exception) when (exception is IOException or InvalidDataException or HttpRequestException or FormatException
    or UnauthorizedAccessException or PlatformNotSupportedException or ArgumentException or System.Runtime.InteropServices.COMException)
{
    Console.Error.WriteLine(exception.Message);
    return 1;
}

static string? TakeOption(List<string> Arguments, string Name)
{
    var index = Arguments.IndexOf(Name);
    if (index < 0)
        return null;
    if (index + 1 >= Arguments.Count)
        throw new ArgumentException($"{Name} requires a value.");
    var value = Arguments[index + 1];
    Arguments.RemoveRange(index, 2);
    return value;
}

static void Convert(string Input, string Output, int? Weight)
{
    Input = Path.GetFullPath(Input);
    Output = Path.GetFullPath(Output);
    if (string.Equals(Input, Output, StringComparison.OrdinalIgnoreCase))
        throw new ArgumentException("Input and output paths must differ.");
    var converted = FontFile.Prepare(File.ReadAllBytes(Input), Weight ?? HarfBuzzInstancer.RegularWeight);
    File.WriteAllBytes(Output, converted);
    Console.WriteLine($"{Path.GetFileName(Input)} -> {Output} ({converted.Length} bytes)");
}

static string DefaultWindowsFonts() =>
    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts");

static string DefaultOutput()
{
    foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        for (var directory = new DirectoryInfo(start); directory != null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Fonts.cs")) && Directory.Exists(Path.Combine(directory.FullName, "Fonts")))
                return Path.Combine(directory.FullName, "Fonts");
    throw new DirectoryNotFoundException("Run from the repository or pass --output.");
}

/*
 * Fonts/manifest.json records where every generated file came from
 */
static void WriteManifest(string Output, IReadOnlyList<ManifestEntry> Entries, bool Merge)
{
    var path = Path.Combine(Output, "manifest.json");
    var manifest = Merge && File.Exists(path)
        ? JsonSerializer.Deserialize<List<ManifestEntry>>(File.ReadAllText(path), ManifestEntry.Json) ?? []
        : [];
    foreach (var entry in Entries)
    {
        var index = manifest.FindIndex(existing => existing.File == entry.File);
        if (index >= 0)
            manifest[index] = entry;
        else
            manifest.Add(entry);
    }
    File.WriteAllText(path, JsonSerializer.Serialize(manifest, ManifestEntry.Json) + "\n");
    Console.WriteLine($"Wrote {Entries.Count} font files to {Output}");
}

internal sealed record ManifestEntry(string File, string Family, string Weight, string Source, int? CollectionIndex, string? UnicodeRange, string Sha256)
{
    internal static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    internal static string WeightName(int Weight) => Weight >= HarfBuzzInstancer.BoldWeight ? "Bold" : "Regular";
}

internal static class FontFile
{
    /// <summary>Produces a static SFNT instance for the explicit convert command.</summary>
    internal static byte[] Prepare(byte[] Data, int Weight)
    {
        Data = Decode(Data);
        return Sfnt.HasTable(Data, "fvar") ? HarfBuzzInstancer.Instance(Data, Weight) : Data;
    }

    internal static byte[] Decode(byte[] Data) => Sfnt.IsSfnt(Data) ? Data : DirectWrite.Unpack(Data);

    internal static string Sha256(byte[] Data) => System.Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Data));
}
