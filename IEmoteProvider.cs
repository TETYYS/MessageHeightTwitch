using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Threading.Tasks;

namespace MessageHeightTwitch
{
	interface IEmoteProvider
	{
		Dictionary<string, SizeF> EmoteCache { get; }

		bool TryGetEmote(string Name, out SizeF Size);
		Task Initialize(string Channel);

		bool IsEmojiSupported(string Emoji);
	}
}
