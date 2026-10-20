using System.Drawing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using MessageHeightTwitch.Measurement.EmoteRewriting;

namespace MessageHeightTwitch.Emotes
{
	/// <summary>
    /// Loads BTTV channel/global emotes and reports supported emoji.
    /// </summary>
	public partial class BTTVEmoteProvider : IEmoteProvider
	{
		internal readonly EmoteSet Channel = new();
		internal EmoteSet Global => Loading.Globals.Bttv;

		private readonly EmoteLoading Loading;
		internal BTTVEmoteProvider(EmoteLoading? Loading = null) => this.Loading = Loading ?? new EmoteLoading();

		private class BTTVEmote
		{
			public string? Id { get; set; }
			public string? Code { get; set; }
			public bool Modifier { get; set; }
		}

		private class BTTVResponse
		{
			public IEnumerable<BTTVEmote> ChannelEmotes { get; set; } = [];
			public IEnumerable<BTTVEmote> SharedEmotes { get; set; } = [];
		}

		[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
		[JsonSerializable(typeof(IEnumerable<BTTVEmote>))]
		[JsonSerializable(typeof(BTTVResponse))]
		[JsonSerializable(typeof(Dictionary<string, string>))]
		[JsonSerializable(typeof(List<string>))]
		private partial class BTTVJsonContext : JsonSerializerContext { }

		public bool TryGetEmote(ReadOnlySpan<char> Name, out SizeF Size)
		{
			var found = TryGet(Name, out var emote);
			Size = emote.Size;
			return found;
		}

		internal bool TryGetModifier(ReadOnlySpan<char> Name, out EmoteModifier Modifier)
		{
			Modifier = TryGet(Name, out var emote) ? emote.Modifier.GetValueOrDefault() : default;
			return emote.Modifier != null;
		}

		private bool TryGet(ReadOnlySpan<char> Name, out CatalogEmote Emote) => Channel.TryGet(Name, out Emote) || Global.TryGet(Name, out Emote);

		static readonly string BTTVUrlTemplate = "https://cdn.betterttv.net/emote/{{id}}/{{image}}";

		public Task Initialize(string ChannelId, CancellationToken Token){
            return Task.WhenAll(
                InitializeGlobal(Token), InitializeChannel(ChannelId, Token)
            );
        }

        internal Task InitializeGlobal(CancellationToken Token) =>
            LoadCatalog("https://api.betterttv.net/3/cached/emotes/global", false, Token);

        internal Task InitializeChannel(string ChannelId, CancellationToken Token) =>
            LoadCatalog("https://api.betterttv.net/3/cached/users/twitch/" + Uri.EscapeDataString(ChannelId), true, Token);

		private async Task LoadCatalog(string Url, bool FromChannel, CancellationToken Token)
		{
            var emotes = FromChannel ?
                await FetchChannel(Url, Token).ConfigureAwait(false) :
                await Loading.GlobalCatalog(Url, shared => Loading.FetchJson(Url, BTTVJsonContext.Default.IEnumerableBTTVEmote,
                    "Missing BetterTTV global emote list", shared), Token).ConfigureAwait(false);

			await Task.WhenAll(emotes.Select(emote => LoadEmote(emote, FromChannel, Token))).ConfigureAwait(false);
		}

		private async Task<IEnumerable<BTTVEmote>> FetchChannel(string Url, CancellationToken Token)
		{
			// A channel BetterTTV does not know has no emotes.
			var catalog = await Loading.FetchJson(Url, BTTVJsonContext.Default.BTTVResponse, "Missing BetterTTV channel emote list", Token, NotFound: new BTTVResponse()).ConfigureAwait(false);
			return (catalog.ChannelEmotes ?? throw new InvalidDataException("Missing BetterTTV channel emotes"))
				.Union(catalog.SharedEmotes ?? throw new InvalidDataException("Missing BetterTTV shared emotes"));
		}

		private async Task LoadEmote(BTTVEmote Emote, bool FromChannel, CancellationToken Token)
		{
			if (Emote.Code is not { } code || Emote.Id is not { } id)
                return;

			var url = BTTVUrlTemplate.Replace("{{id}}", id).Replace("{{image}}", "1x");

			var modifier = EmoteModifier.Known(code, MessageHeightTwitch.EMOTE_PROVIDER.BTTV);
			var entry = new CatalogEmote(new SizeF(28, 28), Emote.Modifier || modifier != null ? modifier ?? new() : null, IsPlaceholder: true);
			if (FromChannel)
				Channel.AddOrReplacePlaceholder(code, entry);
			else
				Global.TryAdd(code, entry);

			var size = await Loading.ImageSize(url, Token).ConfigureAwait(false);
			entry = entry with { Size = size, IsPlaceholder = false };
			if (FromChannel)
				Channel.AddOrReplacePlaceholder(code, entry);
			else
				Global.Set(code, entry);
		}

		private static readonly HashSet<string> SupportedEmojis =
			ReadSupportedEmojis(typeof(BTTVEmoteProvider).Assembly.GetManifestResourceStream("MessageHeightTwitch.BTTVSupportedEmojis.json"));

		internal static HashSet<string> ReadSupportedEmojis(Stream? Catalog)
		{
			using var stream = Catalog ?? throw new InvalidDataException("Missing BetterTTV emoji catalog");
			var emojis = JsonSerializer.Deserialize(stream, BTTVJsonContext.Default.ListString) ?? throw new InvalidDataException("Invalid BetterTTV emoji catalog");
			return new HashSet<string>(emojis, StringComparer.Ordinal);
		}

        public bool IsEmojiSupported(ReadOnlySpan<char> Emoji)
        {
            if (!SupportedEmojis.TryGetAlternateLookup<ReadOnlySpan<char>>(out var lookup))
                throw new InvalidOperationException("BetterTTV emoji set does not support span lookup.");
            return lookup.Contains(Emoji);
        }
	}
}
