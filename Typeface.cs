using NativeFont = HarfBuzzSharp.Font;

using HarfBuzzSharp;

namespace MessageHeightTwitch;

/// <summary>
/// A font file and its effective platform style, including a variable font's selected weight.
/// </summary>
/// <remarks>
public sealed class Typeface
{
    // hb-style.h
    private static readonly Tag Weight = new('w', 'g', 'h', 't');
    private static readonly Tag Italic = new('i', 't', 'a', 'l');

    /*
     * DirectWrite prefers the en-US Windows name, then any Windows name with the ID. Every bundled font
     * has an en-US name, which HarfBuzz returns.
     */
    private static readonly Language English = new("en-US");

    internal Blob Blob { get; }
    public string FamilyName { get; }
    public int FontWeight { get; }
    public bool IsItalic { get; }
    internal int[]? PlatformAdvances { get; }
    internal bool HasWeightAxis { get; }

    private Typeface(Blob Blob)
    {
        this.Blob = Blob;
        using var face = new Face(Blob, 0);
        using var font = new NativeFont(face);
        FamilyName = OpenType.Name(face, OpenTypeNameId.TypographicFamily, English) ?? OpenType.Name(face, OpenTypeNameId.FontFamily, English) ?? "";
        // OS/2 usWeightClass and fsSelection; none of the bundled fonts sets the oblique bit without the italic one
        FontWeight = (int)OpenType.Style(font, Weight);
        IsItalic = OpenType.Style(font, Italic) != 0;
        HasWeightAxis = face.TryFindVariationAxis(Weight, out _);
    }

    internal Typeface(Typeface File, int Weight, bool Italic, int[]? Advances)
    {
        Blob = File.Blob;
        FamilyName = File.FamilyName;
        FontWeight = Weight;
        IsItalic = Italic;
        PlatformAdvances = Advances;
        HasWeightAxis = File.HasWeightAxis;
    }

    /// <summary>Maps the font file into memory.</summary>
    public static Typeface FromFile(string Path) => new(Blob.FromFile(Path));

    internal bool HasGlyph(int Codepoint) => FontCache.GetShaper(this).HasGlyph(Codepoint);

    public override string ToString() => FamilyName;
}
