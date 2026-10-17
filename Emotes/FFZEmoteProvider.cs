using System;
using System.Drawing;
using System.Globalization;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using MessageHeightTwitch.Measurement.EmoteRewriting;

namespace MessageHeightTwitch.Emotes
{
	/// <summary>Loads FFZ room/global emotes, sender sets and emoji support.</summary>
	/// <remarks>
	/// Cache usage: one room EmoteSet per provider; global emotes, emoji names and sender-set mappings are
	/// shared through GlobalEmotes. Room/global emotes merge by name without eviction; emoji and sender
	/// mappings are replaced on load. Storage depends on catalogs; request caches belong to EmoteLoading.
	/// </remarks>
	public partial class FFZEmoteProvider : IEmoteProvider
	{
		internal readonly EmoteSet Room = new();
		internal EmoteSet Global => Globals.Ffz;
		private GlobalEmotes Globals => Loading.Globals;
		private readonly EmoteLoading Loading;
		internal FFZEmoteProvider(EmoteLoading? Loading = null) => this.Loading = Loading ?? new EmoteLoading();

		/// <summary>FFZ emote dimensions and modifier metadata.</summary>
		/// <remarks>
		/// Cache usage: no independent cache; response data is temporary during loading and its size depends
		/// on the response. Retained emote entries belong to EmoteSet.
		/// </remarks>
		class FFZEmoticon
		{
			public int Height { get; set; }
			public int Id { get; set; }
			public bool Modifier { get; set; }
			public EmoteModifierFlags ModifierFlags { get; set; }
			public bool ModifierPrefix { get; set; }
			public string? Name { get; set; }
			public int Width { get; set; }
		}

		/// <summary>The emotes in an FFZ response set.</summary>
		/// <remarks>
		/// Cache usage: no independent cache; response data is temporary during loading and its size depends
		/// on the response. Retained emote entries belong to EmoteSet.
		/// </remarks>
		class FFZSet
		{
			public List<FFZEmoticon> Emoticons { get; set; } = new();
		}

		/// <summary>FFZ catalog sets and user grants.</summary>
		/// <remarks>
		/// Cache usage: no independent cache; response data is temporary during loading and its size depends
		/// on the response. Retained emote entries belong to EmoteSet.
		/// </remarks>
		class FFZResponse
		{
			public Dictionary<string, FFZSet> Sets { get; set; } = new();
			// Global response only: the sets every user has, and the sets granted to listed logins.
			public int[]? DefaultSets { get; set; }
			public Dictionary<string, string[]>? Users { get; set; }
		}

		/// <summary>FFZ supported-emoji response data.</summary>
		/// <remarks>
		/// Cache usage: no independent cache; response data is temporary during loading and its size depends
		/// on the response. Retained emote entries belong to EmoteSet.
		/// </remarks>
		class FFZEmojiResponse
		{
			public object[][] E { get; set; } = [];
		}

		/// <summary>Generated JSON metadata for the provider's catalog responses.</summary>
		/// <remarks>
		/// Cache usage: one generated default context caches serializer metadata for two root types and their
		/// member types for the process lifetime. Size depends on the models; emote payloads are not retained here.
		/// </remarks>
		[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
		[JsonSerializable(typeof(FFZResponse))]
		[JsonSerializable(typeof(FFZEmojiResponse))]
		private partial class FFZJsonContext : JsonSerializerContext { }

		public bool TryGetEmote(ReadOnlySpan<char> Name, out SizeF Size)
		{
			var found = TryGet(Name, out var emote);
			Size = emote.Size;
			return found;
		}

		/// <summary>
		/// The modifier behavior of the emote a sender with <paramref name="SenderSets"/> uses for this name, if it
		/// is a modifier. FFZ checks the sender's own sets first, then the room's, then the global ones.
		/// </summary>
		internal bool TryGetModifier(ReadOnlySpan<char> Name, EmoteSet[]? SenderSets, out EmoteModifier Modifier)
		{
			var found = false;
			CatalogEmote emote = default;
			foreach (var set in SenderSets ?? [])
				if (found = set.TryGet(Name, out emote))
					break;
			if (!found)
				TryGet(Name, out emote);
			Modifier = emote.Modifier.GetValueOrDefault();
			return emote.Modifier != null;
		}

		/// <summary>The FFZ sets granted to the sender with this login, such as Subwoofer Emote Effects; null without any.</summary>
		internal EmoteSet[]? UserSetsFor(ReadOnlySpan<char> Login) => Globals.FfzUserSetsFor(Login);

		private bool TryGet(ReadOnlySpan<char> Name, out CatalogEmote Emote) => Room.TryGet(Name, out Emote) || Global.TryGet(Name, out Emote);

        public Task Initialize(string Channel, CancellationToken Token){
            return Task.WhenAll(InitializeGlobal(Token), InitializeChannel(Channel, Token));
        }

        internal Task InitializeGlobal(CancellationToken Token) => Task.WhenAll(
            LoadSet("https://api.frankerfacez.com/v1/set/global", false, Token), LoadEmojis(Token));

        internal Task InitializeChannel(string Channel, CancellationToken Token) =>
            LoadSet("https://api.frankerfacez.com/v1/room/" + Uri.EscapeDataString(Channel), true, Token);

        private async Task LoadSet(string Url, bool Channel, CancellationToken Token)
        {
            // Channels initializing together share one request for the global sets.
            var json = Channel ?
                await FetchSets(Url, true, Token).ConfigureAwait(false) :
                await Loading.GlobalCatalog(Url, shared => FetchSets(Url, false, shared), Token).ConfigureAwait(false);
            if (Channel) {
                // The room keeps the first of its duplicates.
                foreach (var set in json.Sets.Values)
                    Add(set, Room.TryAdd);
                return;
            }

            /*
             * Like FFZ's loadGlobalSets: every user has the default sets, and listed logins have their granted sets
             */
            var defaults = (json.DefaultSets ?? throw new InvalidDataException("Missing FFZ default sets"))
                .Select(id => id.ToString(CultureInfo.InvariantCulture)).ToHashSet(StringComparer.Ordinal);
            var byLogin = new Dictionary<string, List<EmoteSet>>(StringComparer.OrdinalIgnoreCase);
            foreach (var (id, set) in json.Sets) {
                if (defaults.Contains(id)) {
                    // A later default set replaces an earlier one.
                    Add(set, Global.Set);
                } else if (json.Users?.GetValueOrDefault(id) is { } logins) {
                    var granted = new EmoteSet();
                    Add(set, granted.TryAdd);
                    foreach (var login in logins)
                        (byLogin.TryGetValue(login, out var sets) ? sets : byLogin[login] = []).Add(granted);
                }
            }
            Globals.SetFfzUserSets(byLogin.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray(), StringComparer.OrdinalIgnoreCase));
        }

        private static void Add(FFZSet Set, Action<string, CatalogEmote> Add)
        {
            foreach (var emote in Set.Emoticons) {
                if (emote.Name is null)
                    continue;

                EmoteModifier? modifier = emote.Modifier || emote.Id is 59847 or 70852 or 70854 or 147049 or 147011 or 70864 or 147038
                    ? new EmoteModifier(emote.ModifierFlags, emote.ModifierPrefix) : null;
                Add(emote.Name, new CatalogEmote(new SizeF(emote.Width, emote.Height), modifier));
            }
        }

        private Task<FFZResponse> FetchSets(string Url, bool Channel, CancellationToken Token)
        {
            return Loading.FetchJson(Url, FFZJsonContext.Default.FFZResponse, "Missing FFZ emote sets", Token, NotFound: Channel ? new FFZResponse() : null);
        }

        private async Task LoadEmojis(CancellationToken Token)
        {
            const string url = "https://cdn.frankerfacez.com/static/emoji/v2-.json";
            Globals.FfzEmojis = await Loading.GlobalCatalog(url, shared => FetchEmojis(url, shared), Token).ConfigureAwait(false);
        }

        private async Task<HashSet<string>> FetchEmojis(string Url, CancellationToken Token)
        {
            var catalog = await Loading.FetchJson(Url, FFZJsonContext.Default.FFZEmojiResponse,
                "Missing FFZ emoji catalog", Token).ConfigureAwait(false);
            var emojis = new HashSet<string>(StringComparer.Ordinal);

            foreach (var e in catalog.E) {
                if (e.Length < 5)
                    continue;

                if (((JsonElement)e[4]).GetString() is { } sequence)
                    emojis.Add(FromCodepoints(sequence));

                if (e.Length >= 8 && ((JsonElement)e[7]).ValueKind != JsonValueKind.Number)
                    foreach (var alt in ((JsonElement)e[7]).EnumerateArray())
                        if (alt.GetArrayLength() > 0 && alt[0].GetString() is { } alternate)
                            emojis.Add(FromCodepoints(alternate));
            }

            return emojis;
        }

		/// <summary>
        /// Decodes FFZ's dash-separated hex code points, e.g. "1f468-200d-1f469"
        /// </summary>
		private static string FromCodepoints(string Sequence)
		{
			var result = new StringBuilder();
			foreach (var codepoint in Sequence.Split('-'))
				result.Append(new Rune(int.Parse(codepoint, NumberStyles.HexNumber, CultureInfo.InvariantCulture)).ToString());
			return result.ToString();
		}

		public bool IsEmojiSupported(ReadOnlySpan<char> Emoji)
		{
			if (!Globals.FfzEmojis.TryGetAlternateLookup<ReadOnlySpan<char>>(out var lookup))
				throw new InvalidOperationException("FFZ emoji set does not support span lookup.");
			return lookup.Contains(Emoji);
		}
	}
}
