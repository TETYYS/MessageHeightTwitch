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
	public class SevenTVEmoteProvider : IEmoteProvider
	{
		public Dictionary<string, SizeF> EmoteCache { get; } = new Dictionary<string, SizeF>();
		private static readonly HttpClient Client = new HttpClient();

		class SevenTVEmoticon
		{
			public string name { get; set; }
			public int visibility { get; set; }
			public int[] width { get; set; }
			public int[] height { get; set; }

			public SevenTVEmoticon() { }
		}

		public bool TryGetEmote(string Name, out SizeF Size) => EmoteCache.TryGetValue(Name, out Size);

		public async Task Initialize(string ChannelID, CancellationToken Token)
		{
			var rawJson = await Client.GetAsync("https://api.7tv.app/v2/emotes/global", Token);
			var json = JsonSerializer.Deserialize<List<SevenTVEmoticon>>(await rawJson.Content.ReadAsStringAsync());

			void addToList(SevenTVEmoticon emote)
			{
				if (EmoteCache.ContainsKey(emote.name))
					return;

				const int zw = 1 << 7;
				if ((emote.visibility & zw) == zw) {
					EmoteCache.Add(emote.name, new SizeF(-emote.width[0], -emote.height[0]));
				} else {
					EmoteCache.Add(emote.name, new SizeF(emote.width[0], emote.height[0]));
				}
			}

			foreach (var emote in json) {
				addToList(emote);
			}

			rawJson = await Client.GetAsync("https://api.7tv.app/v2/users/" + ChannelID + "/emotes", Token);
			json = await JsonSerializer.DeserializeAsync<List<SevenTVEmoticon>>(await rawJson.Content.ReadAsStreamAsync());

			foreach (var emote in json) {
				addToList(emote);
			}

		}

		public bool IsEmojiSupported(string Emoji) => false;
	}
}