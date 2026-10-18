using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

using Icu;

namespace MessageHeightTwitch.Measurement.EmoteRewriting;

/// <summary>
/// Finds provider-supported emoji candidates in the text's Unicode sequences and graphemes.
/// </summary>
internal static class EmojiMatcher
{
    /// <summary>
    /// One UTF-16 prefix in the shared emoji trie.
    /// </summary>
    private sealed class Node
    {
        public readonly Dictionary<char, Node> Children = new();
        public bool IsEmoji;
    }

    private static readonly Node Root = BuildTrie();

    private static Node BuildTrie()
    {
        if (Version.Parse(Wrapper.UnicodeVersion).Major < 17)
            throw new NotSupportedException("Emoji matching requires ICU with Unicode 17 data (ICU 78 or newer).");

        var root = new Node();

        foreach (var emoji in UnicodeSet.ToCharacters("[:RGI_Emoji:]")) {
            Add(root, emoji);
            AddNonFullyQualifiedVariants(root, emoji);
        }

        /*
         * Keep the original recognizer's single-character text emoji (for example ©)
         * while excluding bare digits and keycap bases. Provider support decides
         * whether a lone regional indicator is an image (FFZ supports these).
         */
        foreach (var emoji in UnicodeSet.ToCharacters("[:Emoji:]")) {
            var rune = Rune.GetRuneAt(emoji, 0);
            if (rune.Value > '\u007F')
                Add(root, emoji);
        }

        return root;
    }

    private static void Add(Node Root, string Emoji)
    {
        var node = Root;
        foreach (var character in Emoji)
            node = CollectionsMarshal.GetValueRefOrAddDefault(node.Children, character, out _) ??= new Node();
        node.IsEmoji = true;
    }

    private static void AddNonFullyQualifiedVariants(Node Root, string Emoji)
    {
        /* 
         * Unicode emoji-test.txt classifies these forms as minimally qualified or
         * unqualified emoji; omitting U+FE0F means they are no longer RGI.
         */
        for (var index = 0; index < Emoji.Length; index++) {
            if (Emoji[index] != '\uFE0F')
                continue;

            var variant = Emoji.Remove(index, 1);
            Add(Root, variant);
            AddNonFullyQualifiedVariants(Root, variant);
        }
    }

    internal readonly record struct Match(Range Range, MessageHeightTwitch.EMOTE_PROVIDER Provider);

    public static Match? Find(ReadOnlySpan<char> Text, int StartIndex, EmoteResolver Resolver, MessageHeightTwitch.EMOTE_PROVIDER ReplaceMode)
    {
        if (ReplaceMode == MessageHeightTwitch.EMOTE_PROVIDER.NONE)
            return null;

        var ffzJoined = ReplaceMode == MessageHeightTwitch.EMOTE_PROVIDER.FFZ && Text.Contains('\u200D');
        var graphemeStart = 0;
        var graphemeEnd = 0;
        for (var index = StartIndex; index < Text.Length; index++) {
            var node = Root;
            var length = 0;
            var provider = MessageHeightTwitch.EMOTE_PROVIDER.NONE;
            var joinedEnd = index;

            /*
             * The installed FFZ tokenizer patch recognizes a complete pictographic
             * joined grapheme, including non-RGI sequences. A following mark makes
             * that grapheme ineligible and leaves its components to FFZ's tokenizer.
             */
            if (ffzJoined) {
                while (graphemeEnd <= index) {
                    graphemeStart = graphemeEnd;
                    graphemeEnd += StringInfo.GetNextTextElementLength(Text[graphemeEnd..]);
                }
                if (graphemeStart == index && IsJoinedGrapheme(Text[index..graphemeEnd])) {
                    joinedEnd = graphemeEnd;
                    if (Resolver.FFZEmoji(Text[index..joinedEnd])) {
                        length = joinedEnd - index;
                        provider = MessageHeightTwitch.EMOTE_PROVIDER.FFZ;
                    }
                }
            }

            for (var end = index; end < Text.Length && node.Children.TryGetValue(Text[end], out node); end++) {
                if (!node.IsEmoji || end - index + 1 <= length)
                    continue;
                var candidate = Text[index..(end + 1)];

                var supported = Resolver.ResolveEmojiProvider(candidate, ReplaceMode);
                if (supported == MessageHeightTwitch.EMOTE_PROVIDER.NONE || supported == MessageHeightTwitch.EMOTE_PROVIDER.FFZ && candidate.Contains('\u200D') && end + 1 != joinedEnd)
                    continue;

                length = end - index + 1;
                provider = supported;
            }

            if (length != 0)
                return new(index..(index + length), provider);
        }

        return null;
    }

    /// <summary>
    /// EP VS16? modifier? followed by one or more ZWJ + EP VS16? modifier? components.
    /// </summary>
    private static bool IsJoinedGrapheme(ReadOnlySpan<char> Text)
    {
        var joined = false;
        for (var at = 0; at < Text.Length;) {
            if (Rune.DecodeFromUtf16(Text[at..], out var pictograph, out var count) != System.Buffers.OperationStatus.Done || Character.GetIntPropertyValue(pictograph.Value, Character.UProperty.EXTENDED_PICTOGRAPHIC) == 0)
                return false;

            at += count;

            if (at < Text.Length && Text[at] == '\uFE0F')
                at++;
            if (at < Text.Length && Rune.DecodeFromUtf16(Text[at..], out var modifier, out var consumed) == System.Buffers.OperationStatus.Done && Character.GetIntPropertyValue(modifier.Value, Character.UProperty.EMOJI_MODIFIER) != 0)
                at += consumed;
            if (at == Text.Length)
                return joined;
            if (Text[at++] != '\u200D' || at == Text.Length)
                return false;
            joined = true;
        }
        return false;
    }
}
