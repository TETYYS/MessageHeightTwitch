using MessageHeightTwitch.Measurement.InlineCollection;
using MessageHeightTwitch.Measurement.Shared;

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;

using static MessageHeightTwitch.MessageHeightTwitch;

namespace MessageHeightTwitch.Measurement.EmoteRewriting;

[Flags]
public enum EmoteModifierFlags
{
    None = 0,
    Hidden = 1 << 0,
    FlipX = 1 << 1,
    FlipY = 1 << 2,
    GrowX = 1 << 3,
    Slide = 1 << 4,
    Appear = 1 << 5,
    Leave = 1 << 6,
    Rotate = 1 << 7,
    Rotate90 = 1 << 8,
    Greyscale = 1 << 9,
    Sepia = 1 << 10,
    Rainbow = 1 << 11,
    HyperRed = 1 << 12,
    Shake = 1 << 13,
    Cursed = 1 << 14,
    Jam = 1 << 15,
    Bounce = 1 << 16,
    NoSpace = 1 << 17
}

/*
 * FFZ tokenizers.jsx AddonEmotes and render(): modifiers consume their names and
 * separators, then reserve the largest base/overlay box. Effects are combined
 * as flags, so repeated stretching does not multiply the width repeatedly.
 */

public readonly record struct EmoteModifier(EmoteModifierFlags Flags = EmoteModifierFlags.None, bool Prefix = false)
{
    /// <summary>Known defaults for callback providers without catalog metadata.</summary>
    internal static EmoteModifier? Known(ReadOnlySpan<char> Name, EMOTE_PROVIDER Provider) => Provider switch {
        EMOTE_PROVIDER.BTTV => Name switch {
            "w!" => new(EmoteModifierFlags.Hidden | EmoteModifierFlags.GrowX, true),
            "z!" => new(EmoteModifierFlags.Hidden | EmoteModifierFlags.NoSpace, true),
            "h!" => new(EmoteModifierFlags.Hidden | EmoteModifierFlags.FlipX, true),
            "v!" => new(EmoteModifierFlags.Hidden | EmoteModifierFlags.FlipY, true),
            "c!" => new(EmoteModifierFlags.Hidden | EmoteModifierFlags.Cursed, true),
            "l!" => new(EmoteModifierFlags.Hidden | EmoteModifierFlags.Rotate90, true),
            "r!" => new(EmoteModifierFlags.Hidden, true),

            // FFZ's BTTV addon treats p!/s! as visible suffix overlays.
            "p!" or "s!" or "cvMask" or "cvHazmat" or "SoSnowy" or "IceCold"
                or "TopHat" or "SantaHat" or "ReinDeer" or "CandyCane" => new(),
            _ => null
        },
        EMOTE_PROVIDER.FFZ => Name switch {
            "ffzW" => new(EmoteModifierFlags.Hidden | EmoteModifierFlags.GrowX),
            "ffzX" => new(EmoteModifierFlags.Hidden | EmoteModifierFlags.FlipX),
            "ffzY" => new(EmoteModifierFlags.Hidden | EmoteModifierFlags.FlipY),
            "ffzCursed" => new(EmoteModifierFlags.Hidden | EmoteModifierFlags.Cursed),
            "ffzHyper" => new(EmoteModifierFlags.Hidden | EmoteModifierFlags.HyperRed | EmoteModifierFlags.Shake),
            "ffzRainbow" => new(EmoteModifierFlags.Hidden | EmoteModifierFlags.Rainbow),
            "ffzBounce" => new(EmoteModifierFlags.Hidden | EmoteModifierFlags.Bounce),
            "ffzJam" => new(EmoteModifierFlags.Hidden | EmoteModifierFlags.Jam),
            "ffzSlide" => new(EmoteModifierFlags.Hidden | EmoteModifierFlags.Slide),
            "ffzLeave" => new(EmoteModifierFlags.Hidden | EmoteModifierFlags.Leave),
            "ffzArrive" => new(EmoteModifierFlags.Hidden | EmoteModifierFlags.Appear),
            "ffzSpin" => new(EmoteModifierFlags.Hidden | EmoteModifierFlags.Rotate),
            _ => null
        },
        _ => null
    };
}

/// <summary>
/// A resolved box and its source extent; FFZ emoji use a direct image rather than an atomic wrapper.
/// </summary>
internal readonly record struct EmoteLayout(int Length, SizeF Size, bool InlineImage = false);

internal struct MessageToken
{
    internal Range Text;
    internal EmoteLayout? Emote;
    internal EmoteModifier? Modifier;
    internal EmoteModifierFlags Flags; // all attached modifiers' flags
    internal SizeF Size;               // the emote's box with its visible modifiers stacked on top
    internal SizeF TokenSize;          // the size of the emote's FFZ token
    internal bool Modified;            // whether a modifier is attached

    /// <summary>
    /// An emote and its visible (non-Hidden) modifiers are drawn on top of each other, so together
    /// they take up the largest of their boxes. A modified emote is drawn at its token's size rather than its
    /// image's (FFZ's renderEmote styles it explicitly).
    /// </summary>
    internal void Attach(in MessageToken Modifier)
    {
        if (!Modified) {
            Modified = true;
            Size = TokenSize;
        }
        var flags = Modifier.Modifier!.Value.Flags;
        Flags |= flags;
        if ((flags & EmoteModifierFlags.Hidden) != 0)
            return;
        var overlay = Modifier.Emote!.Value.Size;
        Size = new(Math.Max(Size.Width, overlay.Width), Math.Max(Size.Height, overlay.Height));
    }
}

/// <summary>
/// Output of stage 1 (emote rewriting), -> stage 2 (<see cref="ChatParagraphBuilder"/>).
/// A message as FFZ (with its BTTV addon) renders it (<see cref="ModifierRewriter.Rewrite"/>): the text to measure,
/// and the emotes in it. It reads its rewriter's buffers, so it lives until the rewriter rewrites another message.
/// </summary>
internal readonly ref struct RewrittenMessage
{
    /// <summary>
    /// The text to measure: modifier names are merged into the emotes they modify.
    /// </summary>
    internal ReadOnlySpan<char> Text { get; }

    /// <summary>
    /// Every resolved inline object's final box, keyed by its offset in <see cref="Text"/>. Layout consumes these
    /// entries alongside the flat text, as Blink consumes InlineItems alongside InlineNodeData::text_content.
    /// </summary>
    internal Dictionary<int, EmoteLayout> Emotes { get; }

    internal RewrittenMessage(ReadOnlySpan<char> Text, Dictionary<int, EmoteLayout> Emotes)
    {
        this.Text = Text;
        this.Emotes = Emotes;
    }
}

/// <summary>
/// Stage 1 (emote rewriting): resolves emotes and rewrites their modifiers the way FFZ (with its BTTV addon)
/// renders them, before inline collection by <see cref="ChatParagraphBuilder"/>. Reuses its buffers between messages.
/// </summary>
/// <remarks>
/// Cache usage: per collection workspace, a 1,024-entry token list, a dictionary initially reserved for 1,024
/// emotes, and at least 1,024 rented chars (2 KiB of payload).
/// </remarks>
internal sealed class ModifierRewriter : IDisposable
{
    // Layout of FFZ modifiers.
    private const float FFZ_WIDE_SCALE = 2; // ffzW
    private const float FFZ_MAX_MODIFIED_WIDTH = 128;
    private const float FFZ_SPIN_MAX_WIDTH = 32; // ffzSpin

    /*
     * A token consumes at least one input rune (or a separator). The
     * 500-rune input limit therefore bounds the token count.
     */
    private readonly List<MessageToken> Tokens = new(1024);
    private readonly Dictionary<int, EmoteLayout> Emotes = new(1024);

    /*
     * Holds a copy of the message, which the tokens index into, and is then compacted
     * in place into the result.
     */
    private char[] Output = ArrayPool<char>.Shared.Rent(1024);
    private int TextLength;

    // The copy of the message, until it is compacted.
    private ReadOnlySpan<char> Text => Output.AsSpan(0, TextLength);

    public void Dispose() => ArrayPool<char>.Shared.Return(Output);

    /// <summary>
    /// Rewrites the message the way FFZ (with its BTTV addon) renders it. Modifier
    /// names are merged into the emote they modify and disappear from the text,
    /// and every remaining emote gets its final box.
    /// </summary>
    internal RewrittenMessage Rewrite(ReadOnlySpan<char> In, CalculateMessageHeightParams Parameters, EmoteResolver Resolver)
    {
        Tokens.Clear();
        Emotes.Clear();
        return new(RewriteText(In, Parameters, Resolver), Emotes);
    }

    /// <summary>
    /// The text of <see cref="Rewrite"/>, filling <see cref="Emotes"/> with the resolved inline objects.
    /// </summary>
    private ReadOnlySpan<char> RewriteText(ReadOnlySpan<char> In, CalculateMessageHeightParams Parameters, EmoteResolver Resolver)
    {
        PooledBuffers.Ensure(ref Output, In.Length);
        In.CopyTo(Output);
        TextLength = In.Length;

        Tokenize(Parameters, Resolver);

        if (Tokens.Exists(static token => token.Modifier != null)) {
            AttachSuffixModifiers();
            AttachPrefixModifiers();
            RemoveSpacesBeforeNoSpaceEmotes();
        }

        return WriteMessage();
    }

    /// <summary>
    /// Splits the message into emote tokens and runs of text, spaces included.
    /// </summary>
    private void Tokenize(CalculateMessageHeightParams Parameters, EmoteResolver Resolver)
    {
        foreach (var word in Text.Split(' ')) {
            var start = word.Start.Value;
            if (start != 0)
                AddText((start - 1)..start); // the space before this word

            var (emote, modifier, tokenSize) = ResolveEmote(word, Parameters, Resolver);
            if (emote != null)
                AddEmote(word, emote.Value, modifier, tokenSize);
            else
                AddWordSplitAtEmoji(word, Parameters, Resolver);
        }
    }

    /// <summary>
    /// Resolves the emote Text[Slice] names, with its size, modifier metadata and FFZ token size from one lookup.
    /// </summary>
    private (EmoteLayout? Emote, EmoteModifier? Modifier, SizeF TokenSize) ResolveEmote(Range Slice, CalculateMessageHeightParams Parameters, EmoteResolver Resolver)
    {
        var text = Text[Slice];
        var found = Resolver.TryResolveEmote(text, Parameters, out var entry, out var provider);
        var size = entry.Size;
        if (!found)
            return (null, null, default);

        if (provider == EMOTE_PROVIDER.SEVENTV && size.Width < 0) {
            var overlay = new SizeF(Math.Abs(size.Width), Math.Abs(size.Height));
            return (new(text.Length, overlay), new EmoteModifier(), overlay);
        }

        return (new(text.Length, size), entry.Modifier, entry.TokenSize ?? size);
    }

    /// <summary>
    /// Adds a word that is not an emote. Emoji tokens divide the text before addon-emote tokenization, as FFZ's
    /// Emoji tokenizer precedes AddonEmotes. An emoji left as text does not introduce a token boundary.
    /// </summary>
    private void AddWordSplitAtEmoji(Range Word, CalculateMessageHeightParams Parameters, EmoteResolver Resolver)
    {
        var (wordStart, wordEnd) = (Word.Start.Value, Word.End.Value);
        var position = wordStart;
        var search = 0;

        while (EmojiMatcher.Find(Text[Word], search, Resolver, Parameters.EmojiReplaceMode) is { } found) {
            var (emojiStart, emojiEnd) = (wordStart + found.Range.Start.Value, wordStart + found.Range.End.Value);
            search = found.Range.End.Value;
            var provider = found.Provider;

            AddPiece(position..emojiStart, Parameters, Resolver);
            var size = provider == EMOTE_PROVIDER.FFZ ? EmoteResolver.FfzEmojiSize : EmoteResolver.BttvEmojiSize;
            AddEmote(emojiStart..emojiEnd, new(emojiEnd - emojiStart, size, InlineImage: provider == EMOTE_PROVIDER.FFZ));
            position = emojiEnd;
        }

        if (position > wordStart)
            AddPiece(position..wordEnd, Parameters, Resolver);
        else if (wordStart < wordEnd)
            AddText(wordStart..wordEnd); // a word without emoji, already looked up as a whole
    }

    private void AddPiece(Range Range, CalculateMessageHeightParams Parameters, EmoteResolver Resolver)
    {
        if (Range.Start.Value == Range.End.Value)
            return;

        var (emote, modifier, tokenSize) = ResolveEmote(Range, Parameters, Resolver);
        if (emote != null)
            AddEmote(Range, emote.Value, modifier, tokenSize);
        else
            AddText(Range);
    }

    /// <summary>
    /// Adds an emote token, whose FFZ token has <paramref name="TokenSize"/>, or else the emote's size.
    /// </summary>
    private void AddEmote(Range Slice, EmoteLayout Emote, EmoteModifier? Modifier = null, SizeF? TokenSize = null)
    {
        Tokens.Add(new() {
            Text = Slice,
            Emote = Emote,
            Modifier = Modifier,
            Size = Emote.Size,
            TokenSize = TokenSize ?? Emote.Size
        });
    }

    /// <summary>
    /// Appends text, which always directly follows the last token, merging it into a preceding text token
    /// </summary>
    private void AddText(Range Range)
    {
        if (Tokens.Count > 0 && Tokens[^1].Emote == null)
            CollectionsMarshal.AsSpan(Tokens)[^1].Text = Tokens[^1].Text.Start..Range.End;
        else
            Tokens.Add(new() { Text = Range });
    }

    /// <summary>
    /// Whether the token is plain text consisting of exactly one space character. Such a token is
    /// the separator between two words, which modifier attachment may skip over ("NaM ffzW") or
    /// remove (before an emote with z!). A token with a space plus other text does not count.
    /// </summary>
    private bool SingleSpace(in MessageToken Token) => Token.Emote == null && Text[Token.Text] is " ";

    /// <summary>
    /// Attaches each suffix modifier ("NaM ffzW", 7TV zero-width overlays) to the emote before it,
    /// dropping one space in between.
    /// </summary>
    private void AttachSuffixModifiers()
    {
        for (var x = 0;x < Tokens.Count;x++) {
            if (Tokens[x].Modifier is not { Prefix: false })
                continue;

            var target = x - 1;
            if (target >= 0 && SingleSpace(Tokens[target]))
                target--;
            if (target < 0 || Tokens[target].Emote == null)
                continue;

            CollectionsMarshal.AsSpan(Tokens)[target].Attach(Tokens[x]);
            Tokens.RemoveRange(target + 1, x - target);
            x = target;
        }
    }

    /// <summary>
    /// Attaches runs of prefix modifiers ("w! h! NaM") to the emote that follows them, allowing one
    /// space after each
    /// </summary>
    private void AttachPrefixModifiers()
    {
        var target = -1;
        var spaceSkipped = false; // a single space separates Tokens[i] from the target's run
        for (var x = Tokens.Count - 1;x >= 0;x--) {
            var token = Tokens[x];

            if (token.Emote != null && token.Modifier == null) {
                target = x;
                spaceSkipped = false;
            } else if (target >= 0 && token.Modifier is { Prefix: true }) {
                CollectionsMarshal.AsSpan(Tokens)[target].Attach(token);

                var removed = spaceSkipped ? 2 : 1;
                Tokens.RemoveRange(x, removed);

                target -= removed;
                spaceSkipped = false;
            } else if (target >= 0 && !spaceSkipped && SingleSpace(token)) {
                spaceSkipped = true;
            } else {
                target = -1;
                spaceSkipped = false;
            }
        }
    }

    /// <summary>
    /// Removes the space before an emote modified with z!, as FFZ does.
    /// </summary>
    private void RemoveSpacesBeforeNoSpaceEmotes()
    {
        for (var x = Tokens.Count - 1;x > 0;x--)
            if ((Tokens[x].Flags & EmoteModifierFlags.NoSpace) != 0 && SingleSpace(Tokens[x - 1]))
                Tokens.RemoveAt(x - 1);
    }

    /// <summary>
    /// Compacts the remaining tokens in place into the text to measure, recording each emote's final
    /// box under its offset in that text. The flat text and these entries are handed to layout together.
    /// </summary>
    private ReadOnlySpan<char> WriteMessage()
    {
        var buffer = Output.AsSpan();
        var written = 0;
        foreach (ref readonly var token in CollectionsMarshal.AsSpan(Tokens)) {
            if (token.Emote != null)
                Emotes.Add(written, FfzLayout(token));

            var source = buffer[token.Text];
            source.CopyTo(buffer[written..]);
            written += source.Length;
        }
        return buffer[..written];
    }

    private static EmoteLayout FfzLayout(in MessageToken Token)
    {
        var emote = Token.Emote!.Value;
        var size = Token.Size;
        if (Token.Flags != EmoteModifierFlags.None) {
            // Modified emotes reserve at least their painted width, from their token's.
            var painted = (Token.Flags & EmoteModifierFlags.GrowX) != 0 ? Token.TokenSize.Width * FFZ_WIDE_SCALE : Token.TokenSize.Width;
            painted = Math.Min(painted, FFZ_MAX_MODIFIED_WIDTH);

            if ((Token.Flags & EmoteModifierFlags.Rotate) != 0)
                painted = Math.Min(painted, FFZ_SPIN_MAX_WIDTH);

            size.Width = Math.Max(size.Width, painted);
        }
        return emote with { Size = size };
    }
}
