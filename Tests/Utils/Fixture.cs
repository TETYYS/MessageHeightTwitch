using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Tests
{
	public class Fixture
	{
		public MessageHeightTwitch.MessageHeightTwitch Mht { get; }
		public MessageHeightTwitch.MessageHeightTwitch TextOnlyMht { get; } = CreateTextMeasurer(null);

        public static MessageHeightTwitch.MessageHeightTwitch CreateTextMeasurer(string? FfzEmoji) => CreateMeasurer(false, FfzEmoji);

        public static MessageHeightTwitch.MessageHeightTwitch CreateCapturedEmojiMeasurer(IEnumerable<string> Emojis) => CreateMeasurer(false, CapturedEmojis: Emojis);

        public Fixture() => Mht = CreateMeasurer(true);

        private static readonly MessageHeightTwitch.MessageHeightTwitch PlainTextMht = CreateTextMeasurer(null);

        // A measurer with the emotes and emoji images the browser rendered in the case's message.
        internal static (MessageHeightTwitch.MessageHeightTwitch Measurer, Dictionary<string, string>? Twitch) ForCase(TextMeasurementCase Test)
        {
            if (Test.Emotes != null) {
                var catalog = new CapturedEmoteCatalog(Test.Emotes);
                return (catalog.CreateMeasurer(Test.FfzEmojis), catalog.TwitchEmotes);
            }
            return (Test.FfzEmojis != null ? CreateCapturedEmojiMeasurer(Test.FfzEmojis) : PlainTextMht, null);
        }

		private static string FixturePath(string Name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", Name);

		private static string FromCodepoints(string Sequence)
		{
			var result = new StringBuilder();
			foreach (var codepoint in Sequence.Split('-'))
				result.Append(char.ConvertFromUtf32(int.Parse(codepoint, NumberStyles.HexNumber, CultureInfo.InvariantCulture)));
			return result.ToString();
		}

		private static HashSet<string> ReadFfzEmojis()
		{
			using var json = JsonDocument.Parse(File.ReadAllText(FixturePath("ffz-emoji-v2-.json")));
			var result = new HashSet<string>();
			foreach (var entry in json.RootElement.GetProperty("e").EnumerateArray()) {
				if (entry.GetArrayLength() < 5)
					continue;
				result.Add(FromCodepoints(entry[4].GetString()!));
				if (entry.GetArrayLength() >= 8 && entry[7].ValueKind != JsonValueKind.Number) {
					foreach (var alternate in entry[7].EnumerateArray()) {
						if (alternate.GetArrayLength() > 0)
							result.Add(FromCodepoints(alternate[0].GetString()!));
					}
				}
			}
			return result;
		}

        internal static MessageHeightTwitch.MessageHeightTwitch CreateMeasurer(bool Catalog, string? FfzEmoji = null, IEnumerable<string>? CapturedEmojis = null)
        {
            HashSet<string> emojis;
            if (CapturedEmojis != null)
                emojis = new HashSet<string>(CapturedEmojis, StringComparer.Ordinal);
            else if (Catalog)
                emojis = ReadFfzEmojis();
            else if (FfzEmoji == null) {
                emojis = new HashSet<string>(StringComparer.Ordinal);
            } else
                emojis = new HashSet<string>([FfzEmoji], StringComparer.Ordinal);

            var supported = emojis.GetAlternateLookup<ReadOnlySpan<char>>();
            var bttv = new MessageHeightTwitch.Emotes.BTTVEmoteProvider();
            return new(
                (ReadOnlySpan<char> name, out SizeF size) => { size = default; return false; },
                (ReadOnlySpan<char> name, out SizeF size) => { size = new(38, 40); return Catalog && name.SequenceEqual("NaM"); },
                (ReadOnlySpan<char> name, out SizeF size) => { size = default; return false; },
                (name, url) => name switch {
                    "vadiChad" => new(28, 28),
                    "Kappa" => new(25, 28),
                    ">(" => new(24, 18),
                    _ => throw new ArgumentException("Unexpected Twitch emote")
                },
                name => Catalog && bttv.IsEmojiSupported(name), supported.Contains);
        }

		public static readonly Dictionary<string, string> TwitchEmotes = new() {
			{ "vadiChad", "x" },
			{ ">(", "x" },
			{ "Kappa", "x" }
		};
	}

}
