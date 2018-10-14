using NeoSmart.Unicode;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SixLabors.Primitives;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
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
			public string css;
			public int height;
			public bool hidden;
			public int id;
			public string margins;
			public bool modifier;
			public string name;
			public string offset;
			public int width;
		}

		class FFZSet
		{
			public int _type;
			public string css;
			public string description;
			public List<FFZEmoticon> emoticons;
		}

		class FFZResponse
		{
			public Dictionary<int, FFZSet> sets;
		}

		class FFZEmojiResponse
		{
			public int v;
			public string[] c;
			public object[][] e;
		}

		public bool TryGetEmote(string Name, out SizeF Size) => EmoteCache.TryGetValue(Name, out Size);

		public async Task Initialize(string Channel)
		{
			var rawJson = await Client.GetAsync("https://api.frankerfacez.com/v1/set/global");
			var json = JsonConvert.DeserializeObject<FFZResponse>(await rawJson.Content.ReadAsStringAsync());

			void addToList(string Name, int x, int y)
			{
				EmoteCache.Add(Name, new SizeF(x, y));
			}

			json.sets.Values.First().emoticons.ForEach(x => addToList(x.name, x.width, x.height));

			// ---

			rawJson = await Client.GetAsync("https://api.frankerfacez.com/v1/room/" + Channel);
			json = JsonConvert.DeserializeObject<FFZResponse>(await rawJson.Content.ReadAsStringAsync());

			json.sets.Values.First().emoticons.ForEach(x => addToList(x.name, x.width, x.height));

			// ---

			rawJson = await Client.GetAsync("https://cdn.frankerfacez.com/static/emoji/v2-.json");
			var rawEmojis = JsonConvert.DeserializeObject<FFZEmojiResponse>(await rawJson.Content.ReadAsStringAsync());

			foreach (var e in rawEmojis.e) {
				if (e.Length < 5)
					continue;

				AvailableEmojis.Add(
					new UnicodeSequence(
						((string)e[4]).Split('-').Select(x => new Codepoint(x))
					).AsString
				);
				if (e.Length >= 8 && (e[7] is JArray)) {
					var altCodepoints = (JArray)e[7];
					foreach (JArray alt in altCodepoints) {
						if (alt.Count < 1)
							continue;
						AvailableEmojis.Add(
							new UnicodeSequence(
								((string)alt[0]).Split('-').Select(x => new Codepoint(x))
							).AsString
						);
					}
				}
			}
		}

		public bool IsEmojiSupported(string Emoji)
		{
			return AvailableEmojis.Contains(Emoji);
		}
	}
}
