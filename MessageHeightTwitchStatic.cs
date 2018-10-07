using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

class MessageHeightTwitchStatic
{
	public struct TwitchEmote {
		public string Name;
		public string Url;
	}

	private static MessageHeightTwitch.MessageHeightTwitch Instance;

	public static void Init(string CharMapPath, string Channel)
	{
		Instance = new MessageHeightTwitch.MessageHeightTwitch(CharMapPath, Channel);
	}

	public static float CalculateMessageHeight(string Input, string Username, string DisplayName, int NumberOfBadges, IntPtr TwitchEmotes, int TwitchEmotesLen)
	{
		var dict = new Dictionary<string, string>();
		if (TwitchEmotes != null) {
			for (int x = 0;x < TwitchEmotesLen * Marshal.SizeOf<TwitchEmote>();x += Marshal.SizeOf<TwitchEmote>()) {
				var te = Marshal.PtrToStructure<TwitchEmote>(new IntPtr(TwitchEmotes.ToInt64() + x));
				dict.Add(te.Name, te.Url);
			}
		}
		return Instance.CalculateMessageHeight(Input, Username, DisplayName, NumberOfBadges, dict);
	}
}