using Newtonsoft.Json;
using SixLabors.ImageSharp;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace MessageHeightTwitch
{
	public class BTTVEmoteProvider : IEmoteProvider
	{
		public Dictionary<string, SizeF> EmoteCache { get; } = new Dictionary<string, SizeF>();

		private static readonly HttpClient Client = new HttpClient();

		private class BTTVEmote
		{
			public string id;
			public string code;
			public string channel;
			public string imageType;
		}

		private class BTTVResponse
		{
			public int status;
			public string urlTemplate;
			public List<BTTVEmote> emotes;
		}

		private class EmojilibEmoji
		{
			public string[] keywords;
			public string @char;
			public bool fitzpatrick_scale;
			public string category;
		}

		public bool TryGetEmote(string Name, out SizeF Size) => EmoteCache.TryGetValue(Name, out Size);

		public async Task Initialize(string Channel)
		{
			var emoteFetches = new List<Task>();

			var rawJson = await Client.GetAsync("https://api.betterttv.net/2/emotes");
			var json = JsonConvert.DeserializeObject<BTTVResponse>(await rawJson.Content.ReadAsStringAsync());

			if (json.status != 200)
				throw new Exception("BTTV returned " + json.status + " as a status code");

			void addToList(string Name, Stream EmoteStream)
			{
				var img = Image.Load(EmoteStream);
				lock (EmoteCache) {
					EmoteCache.Add(Name, new SizeF(img.Width, img.Height));
				}
			}

			void fetchAllForList(List<BTTVEmote> Emotes)
			{
				foreach (var emote in Emotes) {
					emoteFetches.Add(
						Client.GetStreamAsync(
							"https:" + json.urlTemplate.Replace("{{id}}", emote.id).Replace("{{image}}", "1x")
						).ContinueWith(
							(res) => addToList(emote.code, res.Result), TaskContinuationOptions.OnlyOnRanToCompletion
						)
					);
				}
			}

			fetchAllForList(json.emotes);

			rawJson = await Client.GetAsync("https://api.betterttv.net/2/channels/" + Channel);
			json = JsonConvert.DeserializeObject<BTTVResponse>(await rawJson.Content.ReadAsStringAsync());

			if (json.status != 200)
				throw new Exception("BTTV returned " + json.status + " as a status code (2)");

			fetchAllForList(json.emotes);

			rawJson = await Client.GetAsync("https://raw.githubusercontent.com/muan/emojilib/master/emojis.json");
			var emojilibJson = JsonConvert.DeserializeObject<Dictionary<string, EmojilibEmoji>>(await rawJson.Content.ReadAsStringAsync());

			SupportedEmojis = new HashSet<string>(emojilibJson.Select(x => x.Value.@char));

			rawJson = await Client.GetAsync("https://raw.githubusercontent.com/night/BetterTTV/master/src/utils/emoji-blacklist.json");
			var emojiblacklistJson = JsonConvert.DeserializeObject<List<string>>(await rawJson.Content.ReadAsStringAsync());

			UnsupportedEmojis = new HashSet<string>(emojiblacklistJson);

			await Task.WhenAll(emoteFetches);
		}

		private HashSet<string> UnsupportedEmojis;
		private HashSet<string> SupportedEmojis;

		public bool IsEmojiSupported(string Emoji)
		{
			return !UnsupportedEmojis.Contains(Emoji) && SupportedEmojis.Contains(Emoji);
		}
	}
}
