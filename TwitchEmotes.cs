using SixLabors.ImageSharp;
using SixLabors.Primitives;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Threading.Tasks;

namespace MessageHeightTwitch
{
	class TwitchEmotes
	{
		private ConcurrentDictionary<string, SizeF> Cache = new ConcurrentDictionary<string, SizeF>();
		private static readonly HttpClient Client = new HttpClient();

		public SizeF GetEmote(string Name, string Url)
		{
			SizeF ret;
			if (!Cache.TryGetValue(Name, out ret)) {
				Client.GetStreamAsync(
					Url
				).ContinueWith(
					(res) => {
						var img = Image.Load(res.Result);
						Cache[Name] = new SizeF(img.Width, img.Height);
					}, TaskContinuationOptions.OnlyOnRanToCompletion
				);
				return new SizeF(28, 28);
			}
			return ret;
		}
	}
}
