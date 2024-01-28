using SixLabors.ImageSharp;
using System.Collections.Generic;
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
			internal class SevenTVEmoticonData
			{
				internal class SevenTVEmoticonHost
				{
					internal class SevenTVEmoticonFile
					{
						public int width { get; set; }
						public int height { get; set; }
					}
					
					public List<SevenTVEmoticonFile> files { get; set; }
				}
				
				public SevenTVEmoticonHost host { get; set; }
			}
			
			public string name { get; set; }
			public int flags { get; set; }
			public SevenTVEmoticonData data { get; set; }
		}

		private class SevenTVGlobalEmotes
		{
			public List<SevenTVEmoticon> emotes { get; set; }
		}
		
		private class SevenTVUserEmotes
		{
			internal class SevenTVEmoteSet
			{
				public List<SevenTVEmoticon> emotes { get; set; }
			}
			
			public SevenTVEmoteSet emote_set { get; set; }
		}
		
		public bool TryGetEmote(string Name, out SizeF Size) => EmoteCache.TryGetValue(Name, out Size);

		public async Task Initialize(string ChannelID, CancellationToken Token)
		{
			var rawJson = await Client.GetAsync("https://7tv.io/v3/emote-sets/global", Token);
			var emotes = JsonSerializer.Deserialize<SevenTVGlobalEmotes>(await rawJson.Content.ReadAsStringAsync()).emotes;

			void addToList(SevenTVEmoticon emote)
			{
				if (EmoteCache.ContainsKey(emote.name))
					return;

				const int zeroWidth = 1 << 0;
				var imageSize = new SizeF(emote.data.host.files[0].width, emote.data.host.files[0].height);
				if ((emote.flags & zeroWidth) == zeroWidth) {
					EmoteCache.Add(emote.name, imageSize * -1);
				} else {
					EmoteCache.Add(emote.name, imageSize);
				}
			}

			foreach (var emote in emotes) {
				addToList(emote);
			}

			rawJson = await Client.GetAsync("https://7tv.io/v3/users/twitch/" + ChannelID, Token);
			emotes = (await JsonSerializer.DeserializeAsync<SevenTVUserEmotes>(await rawJson.Content.ReadAsStreamAsync(), cancellationToken: Token)).emote_set.emotes;

			foreach (var emote in emotes) {
				addToList(emote);
			}

		}

		public bool IsEmojiSupported(string Emoji) => false;
	}
}