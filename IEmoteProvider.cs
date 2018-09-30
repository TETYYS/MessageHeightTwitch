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

		SizeF? GetEmote(string Name);
		Task Initialize(string Channel);

		bool IsEmojiSupported(string Emoji);
	}
}
