using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;

namespace MessageHeightTwitch.Emotes
{
	interface IEmoteProvider
	{
		bool TryGetEmote(ReadOnlySpan<char> Name, out SizeF Size);
		Task Initialize(string Channel, CancellationToken Token);

		bool IsEmojiSupported(ReadOnlySpan<char> Emoji);
	}
}
