using NeoSmart.Unicode;
using SixLabors.ImageSharp;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MessageHeightTwitch
{
	public class FFZEmoteProvider : IEmoteProvider
	{
		public Dictionary<string, SizeF> EmoteCache { get; } = new Dictionary<string, SizeF>();
		HashSet<string> AvailableEmojis = new HashSet<string>();
		private static readonly HttpClient Client = new HttpClient();

		class FFZEmoticon
		{
			public string css { get; set; }
			public int height { get; set; }
			public bool hidden { get; set; }
			public int id { get; set; }
			public string margins { get; set; }
			public bool modifier { get; set; }
			public string name { get; set; }
			public string offset { get; set; }
			public int width { get; set; }

			public FFZEmoticon() { }
		}

		class FFZSet
		{
			public int _type { get; set; }
			public string css { get; set; }
			public string description { get; set; }
			public List<FFZEmoticon> emoticons { get; set; }

			public FFZSet() { }
		}

		class FFZResponse
		{
			public Dictionary<string, FFZSet> sets { get; set; }

			public FFZResponse() { }
		}

		class FFZEmojiResponse
		{
			public int v { get; set; }
			public string[] c { get; set; }
			public object[][] e { get; set; }
		}

		public bool TryGetEmote(string Name, out SizeF Size) => EmoteCache.TryGetValue(Name, out Size);

		public async Task Initialize(string Channel, CancellationToken Token)
		{
			var rawJson = await Client.GetAsync("https://api.frankerfacez.com/v1/set/global", Token);
			var json = JsonSerializer.Deserialize<FFZResponse>(await rawJson.Content.ReadAsStringAsync());

			void addToList(string Name, int x, int y)
			{
				if (!EmoteCache.ContainsKey(Name)) {
					EmoteCache.Add(Name, new SizeF(x, y));
				}
			}

			json.sets.Values.First().emoticons.ForEach(x => addToList(x.name, x.width, x.height));

			// ---

			rawJson = await Client.GetAsync("https://api.frankerfacez.com/v1/room/" + Channel, Token);
			json = await JsonSerializer.DeserializeAsync<FFZResponse>(await rawJson.Content.ReadAsStreamAsync());

			json.sets.Values.First().emoticons.ForEach(x => addToList(x.name, x.width, x.height));

			// ---

			rawJson = await Client.GetAsync("https://cdn.frankerfacez.com/static/emoji/v2-.json");
			var rawEmojis = await JsonSerializer.DeserializeAsync<FFZEmojiResponse>(await rawJson.Content.ReadAsStreamAsync());

			foreach (var e in rawEmojis.e) {
				if (e.Length < 5)
					continue;

				AvailableEmojis.Add(
					new UnicodeSequence(
						(((JsonElement)e[4]).GetString()).Split('-').Select(x => new Codepoint(x))
					).AsString
				);
				if (e.Length >= 8 && (((JsonElement)e[7]).ValueKind != JsonValueKind.Number)) {
					var altCodepoints = ((JsonElement)e[7]).EnumerateArray();
					foreach (var alt in altCodepoints) {
						if (alt.GetArrayLength() < 1)
							continue;
						AvailableEmojis.Add(
							new UnicodeSequence(
								(alt.EnumerateArray().First().GetString()).Split('-').Select(x => new Codepoint(x))
							).AsString
						);
					}
				}
			}
		}

		public bool IsEmojiSupported(string Emoji) => AvailableEmojis.Contains(Emoji);
	}
}
