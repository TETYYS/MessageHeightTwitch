using SixLabors.Primitives;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MessageHeightTwitch
{
	interface IEmoteProvider
	{
		Dictionary<string, SizeF> EmoteCache { get; }

		bool TryGetEmote(string Name, out SizeF Size);
		Task Initialize(string Channel, CancellationToken Token);

		bool IsEmojiSupported(string Emoji);
	}
}
