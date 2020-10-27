using SixLabors.ImageSharp;
using SixLabors.Primitives;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MessageHeightTwitch
{
	public class BTTVEmoteProvider : IEmoteProvider
	{
		public Dictionary<string, SizeF> EmoteCache { get; } = new Dictionary<string, SizeF>();

		private static readonly HttpClient Client = new HttpClient();

		private class BTTVEmote
		{
			public string id { get; set; }
			public string code { get; set; }
			public string imageType { get; set; }

			public BTTVEmote() { }
		}

		private class BTTVResponse
		{
			public string id { get; set; }
			public IEnumerable<string> bots { get; set; }
			public IEnumerable<BTTVEmote> channelEmotes { get; set; }
			public IEnumerable<BTTVEmote> sharedEmotes { get; set; }

			public BTTVResponse() { }
		}

		private class EmojilibEmoji
		{
			public string[] keywords { get; set; }
			public string @char { get; set; }
			public bool fitzpatrick_scale { get; set; }
			public string category { get; set; }

			public EmojilibEmoji() { }
		}

		public bool TryGetEmote(string Name, out SizeF Size) => EmoteCache.TryGetValue(Name, out Size);

		static readonly string BTTVUrlTemplate = "https://cdn.betterttv.net/emote/{{id}}/{{image}}";

		public async Task Initialize(string ChannelId, CancellationToken Token)
		{
			var emoteFetches = new List<Task>();

			var rawJson = await Client.GetAsync("https://api.betterttv.net/3/cached/emotes/global", Token);
			rawJson.EnsureSuccessStatusCode();
			
			var globalEmotes = await JsonSerializer.DeserializeAsync<IEnumerable<BTTVEmote>>(await rawJson.Content.ReadAsStreamAsync());

			void addToList(string Name, Stream EmoteStream)
			{
				var img = Image.Load(EmoteStream);
				lock (EmoteCache) {
					if (!EmoteCache.ContainsKey(Name))
						EmoteCache.Add(Name, new SizeF(img.Width, img.Height));
				}
			}

			void fetchAllForList(IEnumerable<BTTVEmote> Emotes)
			{
				foreach (var emote in Emotes) {
					emoteFetches.Add(
						Client.GetStreamAsync(
							BTTVUrlTemplate.Replace("{{id}}", emote.id).Replace("{{image}}", "1x")
						).ContinueWith(
							(res) => addToList(emote.code, res.Result), TaskContinuationOptions.OnlyOnRanToCompletion
						)
					);
				}
			}

			fetchAllForList(globalEmotes);

			rawJson = await Client.GetAsync("https://api.betterttv.net/3/cached/users/twitch/" + ChannelId, Token);
			var rawContents = await rawJson.Content.ReadAsStringAsync();
			bool noUser = false;

			try {
				var error = JsonSerializer.Deserialize<Dictionary<string, string>>(rawContents);
				if (error["message"] == "user not found") {
					noUser = true;
				}
			} catch { }

			if (!noUser) {
				var channelEmotes = JsonSerializer.Deserialize<BTTVResponse>(rawContents);
				fetchAllForList(channelEmotes.channelEmotes.Union(channelEmotes.sharedEmotes));
			}

			rawJson = await Client.GetAsync("https://raw.githubusercontent.com/night/betterttv/4f1239b590b914376c035ad7eb37847e553faf3d/src/utils/emoji-blacklist.json", Token);
			var emojiBlacklist = JsonSerializer.Deserialize<List<string>>(await rawJson.Content.ReadAsStringAsync(), new JsonSerializerOptions() {
				ReadCommentHandling = JsonCommentHandling.Skip
			});

			rawJson = await Client.GetAsync("https://raw.githubusercontent.com/muan/emojilib/master/emojis.json", Token);
			var emojis = JsonSerializer.Deserialize<Dictionary<string, EmojilibEmoji>>(await rawJson.Content.ReadAsStringAsync());

			SupportedEmojis = new HashSet<string>(emojis.Select(x => x.Value.@char).Where(x => !emojiBlacklist.Contains(x)));

			await Task.WhenAll(emoteFetches);
		}

		private HashSet<string> SupportedEmojis;

		public bool IsEmojiSupported(string Emoji) => SupportedEmojis.Contains(Emoji);
	}
}
