using MessageHeightTwitch.Measurement.EmoteRewriting;
using MessageHeightTwitch.Measurement.Shared;

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace MessageHeightTwitch.Measurement.InlineCollection;

/// <summary>
/// Stage 2 (inline collection): collects names, separator and rewritten body into canonical text and inline items
/// for <see cref="ParagraphShaper"/>. Its scratch is independent of shaping and line breaking.
/// </summary>
internal ref struct ChatParagraphBuilder
{
    internal const string Separator = ": ";
    private const char OBJECT_REPLACEMENT = InlineItemsData.OBJECT_REPLACEMENT;
    private static readonly Comparison<(int Start, EmoteLayout Image)> ObjectStartComparison = static (a, b) => a.Start.CompareTo(b.Start);
    private static readonly Comparer<(int Start, EmoteLayout Image)> ObjectStartComparer = Comparer<(int Start, EmoteLayout Image)>.Create(ObjectStartComparison);
    private readonly RewrittenMessage Message;
    private readonly ReadOnlySpan<char> Input;
    private readonly ReadOnlySpan<char> AuthorName;
    private readonly PARAGRAPH_REGION AuthorNameRegion;
    private readonly ReadOnlySpan<char> LoginSuffix;
    private readonly CollectionWorkspace Workspace;
    private readonly InlineItemsData Paragraph;
    private readonly bool IsAction;
    private readonly int ParagraphLength { get => Paragraph.Length; set => Paragraph.Length = value; }
    private readonly int BodyStart { get => Paragraph.BodyStart; set => Paragraph.BodyStart = value; }

    internal ChatParagraphBuilder(RewrittenMessage Message, ReadOnlySpan<char> AuthorName, PARAGRAPH_REGION AuthorNameRegion, ReadOnlySpan<char> LoginSuffix, CollectionWorkspace Workspace, InlineItemsData Paragraph, bool IsAction = false)
    {
        this.Message = Message;
        Input = Message.Text;
        this.AuthorName = AuthorName;
        this.AuthorNameRegion = AuthorNameRegion;
        this.LoginSuffix = LoginSuffix;
        this.Workspace = Workspace;
        this.Paragraph = Paragraph;
        this.IsAction = IsAction;
    }

    /// <summary>
    /// Collects names as text and tokenized body items, collapsing CSS whitespace.
    /// </summary>
    internal void Build()
    {
        Paragraph.Clear();
        Paragraph.Ascii = PrintableAscii(Input) && PrintableAscii(AuthorName) && PrintableAscii(LoginSuffix);
        FindMessageItems();

        var capacity = AuthorName.Length + LoginSuffix.Length + Separator.Length + Input.Length + 1;
        PooledBuffers.Ensure(ref Paragraph.TextBuffer, capacity);
        PooledBuffers.Ensure(ref Paragraph.SourceOffsets, capacity);

        ParagraphLength = 0;
        var previousSpace = true;

        AppendText(AuthorName, 0, AuthorName.Length, true, true, AuthorNameRegion, ref previousSpace);
        AppendText(LoginSuffix, 0, LoginSuffix.Length, false, true, PARAGRAPH_REGION.LOGIN, ref previousSpace);
        var separator = IsAction ? " " : Separator;
        AppendText(separator, 0, separator.Length, false, false, PARAGRAPH_REGION.SEPARATOR, ref previousSpace);
        BodyStart = ParagraphLength;

        for (int unit = 0, image = 0; unit < Workspace.Items.Count || image < Workspace.Objects.Count;) {
            if (image < Workspace.Objects.Count && (unit == Workspace.Items.Count || Workspace.Objects[image].Start < Workspace.Items[unit].Start)) {
                var (source, box) = Workspace.Objects[image++];
                AppendImage(source, box);
                previousSpace = false;
            } else {
                var item = Workspace.Items[unit++];
                AppendText(Input, item.Start, item.End, item.Bold, false, PARAGRAPH_REGION.BODY, ref previousSpace);
            }
        }

        Paragraph.SourceOffsets[ParagraphLength] = Input.Length;
    }

    private void AppendText(ReadOnlySpan<char> Source, int Start, int End, bool Bold, bool BreakAll, PARAGRAPH_REGION Region, ref bool PreviousSpace)
    {
        var italic = IsAction && Region == PARAGRAPH_REGION.BODY;
        var start = ParagraphLength;
        for (var at = Start; at < End; at++) {
            var ch = Source[at];
            var space = ch is ' ' or '\t' or '\r' or '\n';

            if (!space || !PreviousSpace) {
                Paragraph.TextBuffer[ParagraphLength] = space ? ' ' : ch;
                Paragraph.SourceOffsets[ParagraphLength++] = Region == PARAGRAPH_REGION.BODY ? at : -1;
            }

            PreviousSpace = space;
        }

        if (start == ParagraphLength)
            return;

        var items = Paragraph.Items;
        if (items.Count > 0 && items[^1].Image == null && items[^1].End == start && items[^1].Bold == Bold && items[^1].Italic == italic && items[^1].BreakAll == BreakAll && items[^1].Region == Region)
            items[^1] = items[^1] with { End = ParagraphLength };
        else
            items.Add(new(start, ParagraphLength, Bold, BreakAll, Region) { Italic = italic });
    }

    private void AppendImage(int Source, EmoteLayout Image)
    {
        var start = ParagraphLength++;
        Paragraph.TextBuffer[start] = OBJECT_REPLACEMENT;
        Paragraph.SourceOffsets[start] = Source;
        Paragraph.Items.Add(new(start, ParagraphLength, false, false, PARAGRAPH_REGION.BODY) { Image = Image });
    }

    /// <summary>
    /// Collects resolved images and regular or bold text items before bidi segmentation and shaping. Mentions
    /// follow FFZ's space-delimited tokens; neither their style nor their extent depends on a line's available width.
    /// </summary>
    private readonly void FindMessageItems()
    {
        var objects = Workspace.Objects;
        objects.Clear();
        Workspace.Items.Clear();

        foreach (var (start, image) in Message.Emotes)
            objects.Add((start, image));
        objects.Sort(ObjectStartComparison);

        // Mentions belong to FFZ's space-delimited tokens, independently of shaping and line breaking.
        foreach (var token in Input.Split(' ')) {
            var (start, end) = (token.Start.Value, token.End.Value);
            foreach (var (objectStart, image) in ObjectsIn(start, end)) {
                AddMentionText(start, objectStart);
                start = objectStart + image.Length;
            }
            AddMentionText(start, end);
            if (end < Input.Length)
                AddItemText(end, end + 1, Bold: false);
        }
    }


    /// <summary>
    /// Collects a text piece between spaces or images, recognizing its own mention.
    /// </summary>
    private readonly void AddMentionText(int Start, int End)
    {
        if (Start == End)
            return;

        if (FindMention(Input[Start..End]) is { } mention) {
            var from = Start + mention.Start.Value;
            var to = Start + mention.End.Value;

            if (from > Start)
                AddItemText(Start, from, Bold: false);

            AddItemText(from, to, Bold: true);
            if (to < End)
                AddItemText(to, End, Bold: false);
        } else
            AddItemText(Start, End, Bold: false);
    }

    /// <summary>
    /// Adds message text to the item it continues, or starts an item with it after an inline object or a change of weight.
    /// </summary>
    private readonly void AddItemText(int Start, int End, bool Bold)
    {
        var items = Workspace.Items;
        if (items.Count > 0 && items[^1].End == Start && items[^1].Bold == Bold)
            items[^1] = (items[^1].Start, End, Bold);
        else
            items.Add((Start, End, Bold));
    }

    /// <summary>
    /// The message's inline objects (<see cref="FindMessageItems"/>) that start in [<paramref name="Start"/>, <paramref name="End"/>).
    /// </summary>
    private readonly ReadOnlySpan<(int Start, EmoteLayout Image)> ObjectsIn(int Start, int End)
    {
        var objects = CollectionsMarshal.AsSpan(Workspace.Objects);

        var first = Workspace.Objects.BinarySearch((Start, default), ObjectStartComparer);
        if (first < 0)
            first = ~first;
        var last = first;

        while (last < objects.Length && objects[last].Start < End)
            last++;
        return objects[first..last];
    }


    // The characters a mention may follow at the start of a piece, which stay outside it.
    private const string MENTION_LEADING = "'\"*([{<\\/";

    /// <summary>
    /// The mention in <paramref name="Piece"/>, a space-separated piece of a text token, if FFZ's mention tokenizer
    /// (chat/tokenizers.jsx, Mentions with MENTION_REGEX) finds one: "@" and a name, after any of the characters
    /// <see cref="MENTION_LEADING"/>. The name is ASCII word characters, '-' and any character outside ASCII, backed
    /// off from its longest form until JavaScript's \b (between an ASCII word character and anything else) or the
    /// piece's end follows it: "@user-," mentions "@user".
    /// </summary>
    private static Range? FindMention(ReadOnlySpan<char> Piece)
    {
        var at = 0;
        while (at < Piece.Length && MENTION_LEADING.Contains(Piece[at]))
            at++;

        if (at >= Piece.Length || Piece[at] != '@')
            return null;

        var end = at + 1;
        while (end < Piece.Length && (Piece[end] > 0x7F || Piece[end] == '-' || IsWordCharacter(Piece[end])))
            end++;

        for (;end > at + 1;end--)
            if (end == Piece.Length || IsWordCharacter(Piece[end - 1]) != IsWordCharacter(Piece[end]))
                return at..end;

        return null;

        // JavaScript's \w without the u flag.
        static bool IsWordCharacter(char Character) => char.IsAsciiLetterOrDigit(Character) || Character == '_';
    }


    private static bool PrintableAscii(ReadOnlySpan<char> Text)
    {
        foreach (var c in Text)
            if (c is < ' ' or > '~')
                return false;
        return true;
    }
}
