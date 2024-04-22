using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

class MessageHeightTwitchStatic
{
	public struct TwitchEmote
	{
		public string Name;
		public string Url;
	}

	private static Dictionary<string, MessageHeightTwitch.MessageHeightTwitch> Instances = new Dictionary<string, MessageHeightTwitch.MessageHeightTwitch>();

	public static int InitCharMap(string CharMapPath)
	{
		try {
			MessageHeightTwitch.MessageHeightTwitch.FillCharMap(CharMapPath);
		} catch (Exception ex) {
			Console.WriteLine(ex);
			return 0;
		}
		return 1;
	}

	public static int InitChannel(string Channel, string ChannelId, int TimeoutMs)
	{
		try {
			var instance = new MessageHeightTwitch.MessageHeightTwitch(Channel, ChannelId, TimeoutMs);
			if (Instances.ContainsKey(Channel))
				Instances[Channel] = instance;
			else
				Instances.Add(Channel, instance);
		} catch (Exception ex) {
			Console.WriteLine(ex);
			return 0;
		}
		return 1;
	}

	public static int InitChannel2(string Channel, string ChannelId, int TimeoutMs, bool Enable7TVEmotes)
	{
		try {
			var instance = new MessageHeightTwitch.MessageHeightTwitch(Channel, ChannelId, TimeoutMs, Enable7TVEmotes);
			if (Instances.ContainsKey(Channel))
				Instances[Channel] = instance;
			else
				Instances.Add(Channel, instance);
		} catch (Exception ex) {
			Console.WriteLine(ex);
			return 0;
		}
		return 1;
	}

	public static float CalculateMessageHeight(string Channel, string Input, string Username, string DisplayName, int NumberOfBadges, IntPtr TwitchEmotes, int TwitchEmotesLen)
	{
		if (!Instances.ContainsKey(Channel)) {
			Console.WriteLine("Channel is not initialized, initialized:");
			foreach (var chan in Instances.Keys) {
				Console.WriteLine(chan);
			}
			return 0;
		}

		try {
			var dict = new Dictionary<string, string>();
			if (TwitchEmotes != null) {
				for (int x = 0; x < TwitchEmotesLen * Marshal.SizeOf<TwitchEmote>(); x += Marshal.SizeOf<TwitchEmote>()) {
					var te = Marshal.PtrToStructure<TwitchEmote>(new IntPtr(TwitchEmotes.ToInt64() + x));
					dict.Add(te.Name, te.Url);
				}
			}

			return Instances[Channel].CalculateMessageHeight(Input, Username, DisplayName, NumberOfBadges, dict);
		} catch (Exception ex) {
			Console.WriteLine(ex);
			return 0;
		}
	}
}