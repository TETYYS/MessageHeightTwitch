using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Threading;
using SixLabors.ImageSharp;

namespace MessageHeightTwitch
{
	public class MessageHeightTwitch
	{
		public struct CharacterProperty
		{
			public float Width;
			public bool CharWrapping;

			public CharacterProperty(float Width, bool CharWrapping)
			{
				this.Width = Width;
				this.CharWrapping = CharWrapping;
			}
		}

		public static CharacterProperty[] CharacterProperties;

		public delegate bool Fx3rdPartyEmote(string Name, out SizeF Size);
		private readonly Fx3rdPartyEmote BTTVGetEmote = (string __, out SizeF _) => { _ = default; return false; };
		private readonly Fx3rdPartyEmote FFZGetEmote = (string __, out SizeF _) => { _ = default; return false; };
		private readonly Fx3rdPartyEmote SevenTVGetEmote = (string __, out SizeF _) => { _ = default; return false; };
		private readonly Func<string, string, SizeF> TwitchGetEmote;

		private readonly Func<string, bool> BTTVIsEmojiSupported;
		private readonly Func<string, bool> FFZIsEmojiSupported;

		public static void FillCharMap(string CharMapPath)
		{
			var charProperties = new List<CharacterProperty>();

			bool broken = false;
			using (FileStream originalFileStream = File.Open(CharMapPath, FileMode.Open)) {
				using (GZipStream decompressionStream = new GZipStream(originalFileStream, CompressionMode.Decompress)) {
					while (true) {
						var bX = new byte[sizeof(float)];
						if (decompressionStream.Read(bX, 0, sizeof(float)) == 0) {
							broken = true;
							break;
						}
						var bCharWrapping = decompressionStream.ReadByte() == 0x01;

						charProperties.Add(new CharacterProperty(BitConverter.ToSingle(bX, 0), bCharWrapping));
					}
				}
			}

			CharacterProperties = charProperties.ToArray();

			Debug.Assert(broken);
			Debug.Assert(CharacterProperties['A'].Width == 8);
			Debug.Assert(CharacterProperties['@'].Width == 12.171875f);
		}

		private SevenTVEmoteProvider SevenTVEmoteProvider;
		private FFZEmoteProvider FFZEmoteProvider;
		private BTTVEmoteProvider BTTVEmoteProvider;
		private TwitchEmotes TwitchEmotes;

		public MessageHeightTwitch(string Channel, string ChannelId, int TimeoutMs, bool Enable7TVEmotes)
		{
			var cts = new CancellationTokenSource(TimeoutMs);
			FFZEmoteProvider = new FFZEmoteProvider();
			try {
				FFZEmoteProvider.Initialize(Channel, cts.Token).GetAwaiter().GetResult();
				this.FFZGetEmote = FFZEmoteProvider.TryGetEmote;
			} catch (Exception ex) {
				Console.WriteLine("Failed to initialize FFZ: " + ex.ToString());
			}
			BTTVEmoteProvider = new BTTVEmoteProvider();
			try {
				BTTVEmoteProvider.Initialize(ChannelId, cts.Token).GetAwaiter().GetResult();
				this.BTTVGetEmote = BTTVEmoteProvider.TryGetEmote;
			} catch (Exception ex) {
				Console.WriteLine("Failed to initialize BTTV: " + ex.ToString());
			}
			if (Enable7TVEmotes) {
				SevenTVEmoteProvider = new SevenTVEmoteProvider();
				try {
					SevenTVEmoteProvider.Initialize(ChannelId, cts.Token).GetAwaiter().GetResult();
					SevenTVGetEmote = SevenTVEmoteProvider.TryGetEmote;
				} catch (Exception ex) {
					Console.WriteLine("Failed to initialize 7TV: " + ex.ToString());
				}
			}
			TwitchEmotes = new TwitchEmotes();
			this.TwitchGetEmote = TwitchEmotes.GetEmote;
			this.BTTVIsEmojiSupported = (e) => BTTVEmoteProvider.IsEmojiSupported(e);
			this.FFZIsEmojiSupported = (e) => FFZEmoteProvider.IsEmojiSupported(e);
		}

		public MessageHeightTwitch(string Channel, string ChannelId, int TimeoutMs)
			: this(Channel, ChannelId, TimeoutMs, false)
		{
		}

		public MessageHeightTwitch(Fx3rdPartyEmote BTTVGetEmote, Fx3rdPartyEmote FFZGetEmote, Func<string, string, SizeF> TwitchGetEmote, Func<string, bool> BTTVIsEmojiSupported, Func<string, bool> FFZIsEmojiSupported)
		{
			this.SevenTVGetEmote = (string __, out SizeF _) => { _ = default; return false; };
			this.BTTVGetEmote = BTTVGetEmote;
			this.FFZGetEmote = FFZGetEmote;
			this.TwitchGetEmote = TwitchGetEmote;
			this.BTTVIsEmojiSupported = BTTVIsEmojiSupported;
			this.FFZIsEmojiSupported = FFZIsEmojiSupported;
		}

		public MessageHeightTwitch(Fx3rdPartyEmote SevenTVGetEmote, Fx3rdPartyEmote BTTVGetEmote, Fx3rdPartyEmote FFZGetEmote, Func<string, string, SizeF> TwitchGetEmote, Func<string, bool> BTTVIsEmojiSupported, Func<string, bool> FFZIsEmojiSupported)
		{
			this.SevenTVGetEmote = SevenTVGetEmote;
			this.BTTVGetEmote = BTTVGetEmote;
			this.FFZGetEmote = FFZGetEmote;
			this.TwitchGetEmote = TwitchGetEmote;
			this.BTTVIsEmojiSupported = BTTVIsEmojiSupported;
			this.FFZIsEmojiSupported = FFZIsEmojiSupported;
		}

		Regex EmojiRegex = new Regex(@"(?:\uD83D(?:[\uDC76\uDC66\uDC67](?:\uD83C[\uDFFB-\uDFFF])?|\uDC68(?:(?:\uD83C(?:[\uDFFB-\uDFFF](?:\u200D(?:\u2695\uFE0F?|\uD83C[\uDF93\uDFEB\uDF3E\uDF73\uDFED\uDFA4\uDFA8]|\u2696\uFE0F?|\uD83D[\uDD27\uDCBC\uDD2C\uDCBB\uDE80\uDE92]|\u2708\uFE0F?|\uD83E[\uDDB0-\uDDB3]))?)|\u200D(?:\u2695\uFE0F?|\uD83C[\uDF93\uDFEB\uDF3E\uDF73\uDFED\uDFA4\uDFA8]|\u2696\uFE0F?|\uD83D(?:\uDC69\u200D\uD83D(?:\uDC66(?:\u200D\uD83D\uDC66)?|\uDC67(?:\u200D\uD83D[\uDC66\uDC67])?)|\uDC68\u200D\uD83D(?:\uDC66(?:\u200D\uD83D\uDC66)?|\uDC67(?:\u200D\uD83D[\uDC66\uDC67])?)|\uDC66(?:\u200D\uD83D\uDC66)?|\uDC67(?:\u200D\uD83D[\uDC66\uDC67])?|[\uDD27\uDCBC\uDD2C\uDCBB\uDE80\uDE92])|\u2708\uFE0F?|\uD83E[\uDDB0-\uDDB3]|\u2764(?:\uFE0F\u200D\uD83D(?:\uDC8B\u200D\uD83D\uDC68|\uDC68)|\u200D\uD83D(?:\uDC8B\u200D\uD83D\uDC68|\uDC68)))))?|\uDC69(?:(?:\uD83C(?:[\uDFFB-\uDFFF](?:\u200D(?:\u2695\uFE0F?|\uD83C[\uDF93\uDFEB\uDF3E\uDF73\uDFED\uDFA4\uDFA8]|\u2696\uFE0F?|\uD83D[\uDD27\uDCBC\uDD2C\uDCBB\uDE80\uDE92]|\u2708\uFE0F?|\uD83E[\uDDB0-\uDDB3]))?)|\u200D(?:\u2695\uFE0F?|\uD83C[\uDF93\uDFEB\uDF3E\uDF73\uDFED\uDFA4\uDFA8]|\u2696\uFE0F?|\uD83D(?:\uDC69\u200D\uD83D(?:\uDC66(?:\u200D\uD83D\uDC66)?|\uDC67(?:\u200D\uD83D[\uDC66\uDC67])?)|\uDC66(?:\u200D\uD83D\uDC66)?|\uDC67(?:\u200D\uD83D[\uDC66\uDC67])?|[\uDD27\uDCBC\uDD2C\uDCBB\uDE80\uDE92])|\u2708\uFE0F?|\uD83E[\uDDB0-\uDDB3]|\u2764(?:\uFE0F\u200D\uD83D(?:\uDC8B\u200D\uD83D[\uDC68\uDC69]|[\uDC68\uDC69])|\u200D\uD83D(?:\uDC8B\u200D\uD83D[\uDC68\uDC69]|[\uDC68\uDC69])))))?|[\uDC74\uDC75](?:\uD83C[\uDFFB-\uDFFF])?|\uDC6E(?:(?:\uD83C(?:[\uDFFB-\uDFFF](?:\u200D(?:[\u2642\u2640]\uFE0F?))?)|\u200D(?:[\u2642\u2640]\uFE0F?)))?|\uDD75(?:(?:\uFE0F(?:\u200D(?:[\u2642\u2640]\uFE0F?))?|\uD83C(?:[\uDFFB-\uDFFF](?:\u200D(?:[\u2642\u2640]\uFE0F?))?)|\u200D(?:[\u2642\u2640]\uFE0F?)))?|[\uDC82\uDC77](?:(?:\uD83C(?:[\uDFFB-\uDFFF](?:\u200D(?:[\u2642\u2640]\uFE0F?))?)|\u200D(?:[\u2642\u2640]\uFE0F?)))?|\uDC78(?:\uD83C[\uDFFB-\uDFFF])?|\uDC73(?:(?:\uD83C(?:[\uDFFB-\uDFFF](?:\u200D(?:[\u2642\u2640]\uFE0F?))?)|\u200D(?:[\u2642\u2640]\uFE0F?)))?|\uDC72(?:\uD83C[\uDFFB-\uDFFF])?|\uDC71(?:(?:\uD83C(?:[\uDFFB-\uDFFF](?:\u200D(?:[\u2642\u2640]\uFE0F?))?)|\u200D(?:[\u2642\u2640]\uFE0F?)))?|[\uDC70\uDC7C](?:\uD83C[\uDFFB-\uDFFF])?|[\uDE4D\uDE4E\uDE45\uDE46\uDC81\uDE4B\uDE47\uDC86\uDC87\uDEB6](?:(?:\uD83C(?:[\uDFFB-\uDFFF](?:\u200D(?:[\u2642\u2640]\uFE0F?))?)|\u200D(?:[\u2642\u2640]\uFE0F?)))?|[\uDC83\uDD7A](?:\uD83C[\uDFFB-\uDFFF])?|\uDC6F(?:\u200D(?:[\u2642\u2640]\uFE0F?))?|[\uDEC0\uDECC](?:\uD83C[\uDFFB-\uDFFF])?|\uDD74(?:(?:\uD83C[\uDFFB-\uDFFF]|\uFE0F))?|\uDDE3\uFE0F?|[\uDEA3\uDEB4\uDEB5](?:(?:\uD83C(?:[\uDFFB-\uDFFF](?:\u200D(?:[\u2642\u2640]\uFE0F?))?)|\u200D(?:[\u2642\u2640]\uFE0F?)))?|[\uDCAA\uDC48\uDC49\uDC46\uDD95\uDC47\uDD96](?:\uD83C[\uDFFB-\uDFFF])?|\uDD90(?:(?:\uD83C[\uDFFB-\uDFFF]|\uFE0F))?|[\uDC4C-\uDC4E\uDC4A\uDC4B\uDC4F\uDC50\uDE4C\uDE4F\uDC85\uDC42\uDC43](?:\uD83C[\uDFFB-\uDFFF])?|\uDC41(?:(?:\uFE0F(?:\u200D\uD83D\uDDE8\uFE0F?)?|\u200D\uD83D\uDDE8\uFE0F?))?|[\uDDE8\uDDEF\uDD73\uDD76\uDECD\uDC3F\uDD4A\uDD77\uDD78\uDDFA\uDEE3\uDEE4\uDEE2\uDEF3\uDEE5\uDEE9\uDEF0\uDECE\uDD70\uDD79\uDDBC\uDDA5\uDDA8\uDDB1\uDDB2\uDCFD\uDD6F\uDDDE\uDDF3\uDD8B\uDD8A\uDD8C\uDD8D\uDDC2\uDDD2\uDDD3\uDD87\uDDC3\uDDC4\uDDD1\uDDDD\uDEE0\uDDE1\uDEE1\uDDDC\uDECF\uDECB\uDD49]\uFE0F?|[\uDE00-\uDE06\uDE09-\uDE0B\uDE0E\uDE0D\uDE18\uDE17\uDE19\uDE1A\uDE42\uDE10\uDE11\uDE36\uDE44\uDE0F\uDE23\uDE25\uDE2E\uDE2F\uDE2A\uDE2B\uDE34\uDE0C\uDE1B-\uDE1D\uDE12-\uDE15\uDE43\uDE32\uDE41\uDE16\uDE1E\uDE1F\uDE24\uDE22\uDE2D\uDE26-\uDE29\uDE2C\uDE30\uDE31\uDE33\uDE35\uDE21\uDE20\uDE37\uDE07\uDE08\uDC7F\uDC79\uDC7A\uDC80\uDC7B\uDC7D\uDC7E\uDCA9\uDE3A\uDE38\uDE39\uDE3B-\uDE3D\uDE40\uDE3F\uDE3E\uDE48-\uDE4A\uDC64\uDC65\uDC6B-\uDC6D\uDC8F\uDC91\uDC6A\uDC63\uDC40\uDC45\uDC44\uDC8B\uDC98\uDC93-\uDC97\uDC99-\uDC9C\uDDA4\uDC9D-\uDC9F\uDC8C\uDCA4\uDCA2\uDCA3\uDCA5\uDCA6\uDCA8\uDCAB-\uDCAD\uDC53-\uDC62\uDC51\uDC52\uDCFF\uDC84\uDC8D\uDC8E\uDC35\uDC12\uDC36\uDC15\uDC29\uDC3A\uDC31\uDC08\uDC2F\uDC05\uDC06\uDC34\uDC0E\uDC2E\uDC02-\uDC04\uDC37\uDC16\uDC17\uDC3D\uDC0F\uDC11\uDC10\uDC2A\uDC2B\uDC18\uDC2D\uDC01\uDC00\uDC39\uDC30\uDC07\uDC3B\uDC28\uDC3C\uDC3E\uDC14\uDC13\uDC23-\uDC27\uDC38\uDC0A\uDC22\uDC0D\uDC32\uDC09\uDC33\uDC0B\uDC2C\uDC1F-\uDC21\uDC19\uDC1A\uDC0C\uDC1B-\uDC1E\uDC90\uDCAE\uDD2A\uDDFE\uDDFB\uDC92\uDDFC\uDDFD\uDD4C\uDD4D\uDD4B\uDC88\uDE82-\uDE8A\uDE9D\uDE9E\uDE8B-\uDE8E\uDE90-\uDE9C\uDEB2\uDEF4\uDEF9\uDEF5\uDE8F\uDEA8\uDEA5\uDEA6\uDED1\uDEA7\uDEF6\uDEA4\uDEA2\uDEEB\uDEEC\uDCBA\uDE81\uDE9F-\uDEA1\uDE80\uDEF8\uDD5B\uDD67\uDD50\uDD5C\uDD51\uDD5D\uDD52\uDD5E\uDD53\uDD5F\uDD54\uDD60\uDD55\uDD61\uDD56\uDD62\uDD57\uDD63\uDD58\uDD64\uDD59\uDD65\uDD5A\uDD66\uDD25\uDCA7\uDEF7\uDD2E\uDD07-\uDD0A\uDCE2\uDCE3\uDCEF\uDD14\uDD15\uDCFB\uDCF1\uDCF2\uDCDE-\uDCE0\uDD0B\uDD0C\uDCBB\uDCBD-\uDCC0\uDCFA\uDCF7-\uDCF9\uDCFC\uDD0D\uDD0E\uDCA1\uDD26\uDCD4-\uDCDA\uDCD3\uDCD2\uDCC3\uDCDC\uDCC4\uDCF0\uDCD1\uDD16\uDCB0\uDCB4-\uDCB8\uDCB3\uDCB9\uDCB1\uDCB2\uDCE7-\uDCE9\uDCE4-\uDCE6\uDCEB\uDCEA\uDCEC-\uDCEE\uDCDD\uDCBC\uDCC1\uDCC2\uDCC5-\uDCD0\uDD12\uDD13\uDD0F-\uDD11\uDD28\uDD2B\uDD27\uDD29\uDD17\uDD2C\uDD2D\uDCE1\uDC89\uDC8A\uDEAA\uDEBD\uDEBF\uDEC1\uDED2\uDEAC\uDDFF\uDEAE\uDEB0\uDEB9-\uDEBC\uDEBE\uDEC2-\uDEC5\uDEB8\uDEAB\uDEB3\uDEAD\uDEAF\uDEB1\uDEB7\uDCF5\uDD1E\uDD03\uDD04\uDD19-\uDD1D\uDED0\uDD4E\uDD2F\uDD00-\uDD02\uDD3C\uDD3D\uDD05\uDD06\uDCF6\uDCF3\uDCF4\uDD31\uDCDB\uDD30\uDD1F\uDCAF\uDD20-\uDD24\uDD36-\uDD3B\uDCA0\uDD18\uDD32-\uDD35\uDEA9])|\uD83E(?:[\uDDD2\uDDD1\uDDD3](?:\uD83C[\uDFFB-\uDFFF])?|[\uDDB8\uDDB9](?:\u200D(?:[\u2640\u2642]\uFE0F?))?|[\uDD34\uDDD5\uDDD4\uDD35\uDD30\uDD31\uDD36](?:\uD83C[\uDFFB-\uDFFF])?|[\uDDD9-\uDDDD](?:(?:\uD83C(?:[\uDFFB-\uDFFF](?:\u200D(?:[\u2640\u2642]\uFE0F?))?)|\u200D(?:[\u2640\u2642]\uFE0F?)))?|[\uDDDE\uDDDF](?:\u200D(?:[\u2640\u2642]\uFE0F?))?|[\uDD26\uDD37](?:(?:\uD83C(?:[\uDFFB-\uDFFF](?:\u200D(?:[\u2642\u2640]\uFE0F?))?)|\u200D(?:[\u2642\u2640]\uFE0F?)))?|[\uDDD6-\uDDD8](?:(?:\uD83C(?:[\uDFFB-\uDFFF](?:\u200D(?:[\u2640\u2642]\uFE0F?))?)|\u200D(?:[\u2640\u2642]\uFE0F?)))?|\uDD38(?:(?:\uD83C(?:[\uDFFB-\uDFFF](?:\u200D(?:[\u2642\u2640]\uFE0F?))?)|\u200D(?:[\u2642\u2640]\uFE0F?)))?|\uDD3C(?:\u200D(?:[\u2642\u2640]\uFE0F?))?|[\uDD3D\uDD3E\uDD39](?:(?:\uD83C(?:[\uDFFB-\uDFFF](?:\u200D(?:[\u2642\u2640]\uFE0F?))?)|\u200D(?:[\u2642\u2640]\uFE0F?)))?|[\uDD33\uDDB5\uDDB6\uDD1E\uDD18\uDD19\uDD1B\uDD1C\uDD1A\uDD1F\uDD32](?:\uD83C[\uDFFB-\uDFFF])?|[\uDD23\uDD70\uDD17\uDD29\uDD14\uDD28\uDD10\uDD24\uDD11\uDD2F\uDD75\uDD76\uDD2A\uDD2C\uDD12\uDD15\uDD22\uDD2E\uDD27\uDD20\uDD21\uDD73\uDD74\uDD7A\uDD25\uDD2B\uDD2D\uDDD0\uDD13\uDD16\uDD3A\uDD1D\uDDB0-\uDDB3\uDDE0\uDDB4\uDDB7\uDDE1\uDD7D\uDD7C\uDDE3-\uDDE6\uDD7E\uDD7F\uDDE2\uDD8D\uDD8A\uDD9D\uDD81\uDD84\uDD93\uDD8C\uDD99\uDD92\uDD8F\uDD9B\uDD94\uDD87\uDD98\uDDA1\uDD83\uDD85\uDD86\uDDA2\uDD89\uDD9A\uDD9C\uDD8E\uDD95\uDD96\uDD88\uDD80\uDD9E\uDD90\uDD91\uDD8B\uDD97\uDD82\uDD9F\uDDA0\uDD40\uDD6D\uDD5D\uDD65\uDD51\uDD54\uDD55\uDD52\uDD6C\uDD66\uDD5C\uDD50\uDD56\uDD68\uDD6F\uDD5E\uDDC0\uDD69\uDD53\uDD6A\uDD59\uDD5A\uDD58\uDD63\uDD57\uDDC2\uDD6B\uDD6E\uDD5F-\uDD61\uDDC1\uDD67\uDD5B\uDD42\uDD43\uDD64\uDD62\uDD44\uDDED\uDDF1\uDDF3\uDDE8\uDDE7\uDD47-\uDD49\uDD4E\uDD4F\uDD4D\uDD4A\uDD4B\uDD45\uDD4C\uDDFF\uDDE9\uDDF8\uDD41\uDDEE\uDDFE\uDDF0\uDDF2\uDDEA-\uDDEC\uDDEF\uDDF4-\uDDF7\uDDF9-\uDDFD])|[\u263A\u2639\u2620]\uFE0F?|\uD83C(?:\uDF85(?:\uD83C[\uDFFB-\uDFFF])?|\uDFC3(?:(?:\uD83C(?:[\uDFFB-\uDFFF](?:\u200D(?:[\u2642\u2640]\uFE0F?))?)|\u200D(?:[\u2642\u2640]\uFE0F?)))?|[\uDFC7\uDFC2](?:\uD83C[\uDFFB-\uDFFF])?|\uDFCC(?:(?:\uFE0F(?:\u200D(?:[\u2642\u2640]\uFE0F?))?|\uD83C(?:[\uDFFB-\uDFFF](?:\u200D(?:[\u2642\u2640]\uFE0F?))?)|\u200D(?:[\u2642\u2640]\uFE0F?)))?|[\uDFC4\uDFCA](?:(?:\uD83C(?:[\uDFFB-\uDFFF](?:\u200D(?:[\u2642\u2640]\uFE0F?))?)|\u200D(?:[\u2642\u2640]\uFE0F?)))?|\uDFCB(?:(?:\uFE0F(?:\u200D(?:[\u2642\u2640]\uFE0F?))?|\uD83C(?:[\uDFFB-\uDFFF](?:\u200D(?:[\u2642\u2640]\uFE0F?))?)|\u200D(?:[\u2642\u2640]\uFE0F?)))?|[\uDFCE\uDFCD\uDFF5\uDF36\uDF7D\uDFD4-\uDFD6\uDFDC-\uDFDF\uDFDB\uDFD7\uDFD8\uDFDA\uDFD9\uDF21\uDF24-\uDF2C\uDF97\uDF9F\uDF96\uDF99-\uDF9B\uDF9E\uDFF7\uDD70\uDD71\uDD7E\uDD7F\uDE02\uDE37]\uFE0F?|\uDFF4(?:(?:\u200D\u2620\uFE0F?|\uDB40\uDC67\uDB40\uDC62\uDB40(?:\uDC65\uDB40\uDC6E\uDB40\uDC67\uDB40\uDC7F|\uDC73\uDB40\uDC63\uDB40\uDC74\uDB40\uDC7F|\uDC77\uDB40\uDC6C\uDB40\uDC73\uDB40\uDC7F)))?|\uDFF3(?:(?:\uFE0F(?:\u200D\uD83C\uDF08)?|\u200D\uD83C\uDF08))?|\uDDE6\uD83C[\uDDE8-\uDDEC\uDDEE\uDDF1\uDDF2\uDDF4\uDDF6-\uDDFA\uDDFC\uDDFD\uDDFF]|\uDDE7\uD83C[\uDDE6\uDDE7\uDDE9-\uDDEF\uDDF1-\uDDF4\uDDF6-\uDDF9\uDDFB\uDDFC\uDDFE\uDDFF]|\uDDE8\uD83C[\uDDE6\uDDE8\uDDE9\uDDEB-\uDDEE\uDDF0-\uDDF5\uDDF7\uDDFA-\uDDFF]|\uDDE9\uD83C[\uDDEA\uDDEC\uDDEF\uDDF0\uDDF2\uDDF4\uDDFF]|\uDDEA\uD83C[\uDDE6\uDDE8\uDDEA\uDDEC\uDDED\uDDF7-\uDDFA]|\uDDEB\uD83C[\uDDEE-\uDDF0\uDDF2\uDDF4\uDDF7]|\uDDEC\uD83C[\uDDE6\uDDE7\uDDE9-\uDDEE\uDDF1-\uDDF3\uDDF5-\uDDFA\uDDFC\uDDFE]|\uDDED\uD83C[\uDDF0\uDDF2\uDDF3\uDDF7\uDDF9\uDDFA]|\uDDEE\uD83C[\uDDE8-\uDDEA\uDDF1-\uDDF4\uDDF6-\uDDF9]|\uDDEF\uD83C[\uDDEA\uDDF2\uDDF4\uDDF5]|\uDDF0\uD83C[\uDDEA\uDDEC-\uDDEE\uDDF2\uDDF3\uDDF5\uDDF7\uDDFC\uDDFE\uDDFF]|\uDDF1\uD83C[\uDDE6-\uDDE8\uDDEE\uDDF0\uDDF7-\uDDFB\uDDFE]|\uDDF2\uD83C[\uDDE6\uDDE8-\uDDED\uDDF0-\uDDFF]|\uDDF3\uD83C[\uDDE6\uDDE8\uDDEA-\uDDEC\uDDEE\uDDF1\uDDF4\uDDF5\uDDF7\uDDFA\uDDFF]|\uDDF4\uD83C\uDDF2|\uDDF5\uD83C[\uDDE6\uDDEA-\uDDED\uDDF0-\uDDF3\uDDF7-\uDDF9\uDDFC\uDDFE]|\uDDF6\uD83C\uDDE6|\uDDF7\uD83C[\uDDEA\uDDF4\uDDF8\uDDFA\uDDFC]|\uDDF8\uD83C[\uDDE6-\uDDEA\uDDEC-\uDDF4\uDDF7-\uDDF9\uDDFB\uDDFD-\uDDFF]|\uDDF9\uD83C[\uDDE6\uDDE8\uDDE9\uDDEB-\uDDED\uDDEF-\uDDF4\uDDF7\uDDF9\uDDFB\uDDFC\uDDFF]|\uDDFA\uD83C[\uDDE6\uDDEC\uDDF2\uDDF3\uDDF8\uDDFE\uDDFF]|\uDDFB\uD83C[\uDDE6\uDDE8\uDDEA\uDDEC\uDDEE\uDDF3\uDDFA]|\uDDFC\uD83C[\uDDEB\uDDF8]|\uDDFD\uD83C\uDDF0|\uDDFE\uD83C[\uDDEA\uDDF9]|\uDDFF\uD83C[\uDDE6\uDDF2\uDDFC]|[\uDFFB-\uDFFF\uDF92\uDFA9\uDF93\uDF38-\uDF3C\uDF37\uDF31-\uDF35\uDF3E-\uDF43\uDF47-\uDF53\uDF45\uDF46\uDF3D\uDF44\uDF30\uDF5E\uDF56\uDF57\uDF54\uDF5F\uDF55\uDF2D-\uDF2F\uDF73\uDF72\uDF7F\uDF71\uDF58-\uDF5D\uDF60\uDF62-\uDF65\uDF61\uDF66-\uDF6A\uDF82\uDF70\uDF6B-\uDF6F\uDF7C\uDF75\uDF76\uDF7E\uDF77-\uDF7B\uDF74\uDFFA\uDF0D-\uDF10\uDF0B\uDFE0-\uDFE6\uDFE8-\uDFED\uDFEF\uDFF0\uDF01\uDF03-\uDF07\uDF09\uDF0C\uDFA0-\uDFA2\uDFAA\uDF11-\uDF20\uDF00\uDF08\uDF02\uDF0A\uDF83\uDF84\uDF86-\uDF8B\uDF8D-\uDF91\uDF80\uDF81\uDFAB\uDFC6\uDFC5\uDFC0\uDFD0\uDFC8\uDFC9\uDFBE\uDFB3\uDFCF\uDFD1-\uDFD3\uDFF8\uDFA3\uDFBD\uDFBF\uDFAF\uDFB1\uDFAE\uDFB0\uDFB2\uDCCF\uDC04\uDFB4\uDFAD\uDFA8\uDFBC\uDFB5\uDFB6\uDFA4\uDFA7\uDFB7-\uDFBB\uDFA5\uDFAC\uDFEE\uDFF9\uDFE7\uDFA6\uDD8E\uDD91-\uDD9A\uDE01\uDE36\uDE2F\uDE50\uDE39\uDE1A\uDE32\uDE51\uDE38\uDE34\uDE33\uDE3A\uDE35\uDFC1\uDF8C])|\u26F7\uFE0F?|\u26F9(?:(?:\uFE0F(?:\u200D(?:[\u2642\u2640]\uFE0F?))?|\uD83C(?:[\uDFFB-\uDFFF](?:\u200D(?:[\u2642\u2640]\uFE0F?))?)|\u200D(?:[\u2642\u2640]\uFE0F?)))?|[\u261D\u270C](?:(?:\uD83C[\uDFFB-\uDFFF]|\uFE0F))?|[\u270B\u270A](?:\uD83C[\uDFFB-\uDFFF])?|\u270D(?:(?:\uD83C[\uDFFB-\uDFFF]|\uFE0F))?|[\u2764\u2763\u26D1\u2618\u26F0\u26E9\u2668\u26F4\u2708\u23F1\u23F2\u2600\u2601\u26C8\u2602\u26F1\u2744\u2603\u2604\u26F8\u2660\u2665\u2666\u2663\u260E\u2328\u2709\u270F\u2712\u2702\u26CF\u2692\u2694\u2699\u2696\u26D3\u2697\u26B0\u26B1\u26A0\u2622\u2623\u2B06\u2197\u27A1\u2198\u2B07\u2199\u2B05\u2196\u2195\u2194\u21A9\u21AA\u2934\u2935\u269B\u267E\u2721\u2638\u262F\u271D\u2626\u262A\u262E\u25B6\u23ED\u23EF\u25C0\u23EE\u23F8-\u23FA\u23CF\u2640\u2642\u2695\u267B\u269C\u2611\u2714\u2716\u303D\u2733\u2734\u2747\u203C\u2049\u3030\u00A9\u00AE\u2122]\uFE0F?|[\u0023\u002A\u0030-\u0039](?:\uFE0F\u20E3|\u20E3)|[\u2139\u24C2\u3297\u3299\u25AA\u25AB\u25FB\u25FC]\uFE0F?|[\u2615\u26EA\u26F2\u26FA\u26FD\u2693\u26F5\u231B\u23F3\u231A\u23F0\u2B50\u26C5\u2614\u26A1\u26C4\u2728\u26BD\u26BE\u26F3\u267F\u26D4\u2648-\u2653\u26CE\u23E9-\u23EC\u2B55\u2705\u274C\u274E\u2795-\u2797\u27B0\u27BF\u2753-\u2755\u2757\u25FD\u25FE\u2B1B\u2B1C\u26AA\u26AB])", RegexOptions.Compiled);

		private readonly Regex BTTVEmoteStrip = new Regex(@"(^[~!@#$%\^&\*\(\)]+|[~!@#$%\^&\*\(\)]+$)", RegexOptions.Compiled);

		[Flags]
		public enum EMOTE_PROVIDER
		{
			/// <summary>
			/// Original
			/// </summary>
			NONE = 1,

			/// <summary>
			/// BTTV (20x20 emojis)
			/// </summary>
			BTTV = 2,

			/// <summary>
			/// FFZ (18x18 emojis), this provider is usually used on emojis
			/// </summary>
			FFZ = 4,

			/// <summary>
			/// Is an emoji
			/// </summary>
			EMOJI = 8
		}

		public enum LOCALIZED_NAME_MODE
		{
			/// <summary>
			/// Calculate using username only
			/// </summary>
			USERNAME_ONLY,

			/// <summary>
			/// Calculate using display name only
			/// </summary>
			DISPLAY_NAME_ONLY,

			/// <summary>
			/// Calculate using both. If username and display name are the same, only display name is used
			/// </summary>
			USERNAME_DISPLAY_NAME
		}

		public struct CalculateMessageHeightParams
		{
			/// <summary>
			/// Number of badges on the left side of the username
			/// </summary>
			public int NumberOfBadges;

			/// <summary>
			/// Twitch emotes, name to image url
			/// </summary>
			public Dictionary<string, string> TwitchEmotes;

			/// <summary>
			/// Apply negative margins to emotes, making large emotes overlap on other lines
			/// </summary>
			public bool EmoteMargin;

			/// <summary>
			/// Apply margins to individual lines
			/// </summary>
			public bool LineMargin;

			/// <summary>
			/// Apply margins to whole message at the end
			/// </summary>
			public bool WholeMargin;

			/// <summary>
			/// What provider to use for replacing emojis. Different providers
			/// have different image sizes, influencing final height.
			/// Note that not all emotes are replaced to their BTTV and FFZ versions
			/// and this would require a list of replaceble emojis to solve this.
			/// </summary>
			public EMOTE_PROVIDER EmojiReplaceMode;

			/// <summary>
			/// Which username to use when calculating size
			/// </summary>
			public LOCALIZED_NAME_MODE LocalizedNameMode;

			/// <summary>
			/// (Chatterino 2 behavior) Ignore specific characters that do no word wrap
			/// and reset next line word wrapping for other characters and words
			/// </summary>
			public bool IgnoreCharWrappingRules;

			/// <summary>
			/// When using both BTTV and FFZ, BTTV splits strings without FFZ knowing in a way that
			/// FFZ replaces them with actual emotes. When using only FFZ, it doesn't.
			/// </summary>
			public bool ApplyBTTVStripToFFZ;

			/// <summary>
			/// This protects FFZ-only users and Chatterino users from seeing seeminly too long messages,
			/// because BTTV can strip infinite characters that are attached to the emote. This way
			/// web chat users see only the emote, Chatterino users will see a very long message with
			/// a lot of symbols and an emote (which is not rendered) in between.
			/// </summary>
			public bool AddStrippedEmoteCharsToCalc;

			public static CalculateMessageHeightParams Default => new CalculateMessageHeightParams
			{
				NumberOfBadges = 0,
				TwitchEmotes = null,
				EmoteMargin = true,
				LineMargin = true,
				WholeMargin = true,
				EmojiReplaceMode = EMOTE_PROVIDER.FFZ,
				LocalizedNameMode = LOCALIZED_NAME_MODE.USERNAME_DISPLAY_NAME,
				IgnoreCharWrappingRules = false,
				ApplyBTTVStripToFFZ = true,
				AddStrippedEmoteCharsToCalc = true,
			};
		}

		/// <summary>
		/// Measures the height of Twitch chat message
		/// </summary>
		/// <param name="Input">Input message</param>
		/// <param name="Username">Poster username</param>
		/// <param name="DisplayName">Poster display name</param>
		/// <param name="NumberOfBadges">Number of badges associated with the poster</param>
		/// <param name="AdditionalEmotes">Custom emote to size map (primarily used for channel emotes)</param>
		/// <returns>Height of measured message in pixels</returns>
		public float CalculateMessageHeight(string Input, string Username, string DisplayName, int NumberOfBadges, Dictionary<string, string> TwitchEmotes)
		{
			var param = CalculateMessageHeightParams.Default;
			param.NumberOfBadges = NumberOfBadges;
			param.TwitchEmotes = TwitchEmotes;
			return CalculateMessageHeightEx(Input, Username, DisplayName, param);
		}

		/// <summary>
		/// Measures the height of Twitch chat message (extended parameters)
		/// </summary>
		/// <param name="Input">Input message</param>
		/// <param name="Username">Poster username</param>
		/// <param name="DisplayName">Poster display name</param>
		/// <param name="Params">Other parameters</param>
		/// <returns>Height of measured message in pixels</returns>
		/// <remarks>Issues:
		///		<para>Issue 1: Character spacing</para>
		///			<para>
		///				Some characters have different spacings between each other when combined/repeated.
		///				Although spacing pattern seems to be linear, it only starts from certain character count
		///				(when combined) and these two parameters change when combined characters are different.
		///			</para>
		/// </remarks>
		public float CalculateMessageHeightEx(
			string Input,
			string Username,
			string DisplayName,
			CalculateMessageHeightParams Params)
		{
			// Whole chat width
			const int CHAT_WIDTH = 339 - 40; // -20 for padding on both sides

			// One line height - i.e. One character with margin on top and bottom
			const int CHAR_W_MARG = 20;

			/*
			 * Pre-processing, this deals with usernames and display names with hieroglyphs
			 */
			switch (Params.LocalizedNameMode) {
				case LOCALIZED_NAME_MODE.USERNAME_DISPLAY_NAME:
					if (DisplayName.Equals(Username, StringComparison.OrdinalIgnoreCase))
						Input = DisplayName + ": " + Input;
					else
						Input = Username + " (" + DisplayName + "): " + Input;
					break;
				case LOCALIZED_NAME_MODE.DISPLAY_NAME_ONLY:
					Input = DisplayName + ": " + Input;
					break;
				case LOCALIZED_NAME_MODE.USERNAME_ONLY:
					Input = Username + ": " + Input;
					break;
			}

			float finalH = 0; // Final height
			SizeF cur = new SizeF(); // Current line size
			SizeF sz; // Current charater size
			string prevEmote = null;

			// Assuming all badges are smaller than the whole chat width
			cur.Width += Params.NumberOfBadges * 21.0f;

			var split = Regex.Split(Input, @"(?<=[ -])");
			int curChar = 0;
			for (int x = 0; x < split.Length;/* Increment is at the end of the loop */) {
				// Currently processing emote name
				string curEmoteName = null;
				// Currently processing emote provider
				EMOTE_PROVIDER curEmoteProvider;
				// Currently processing emote size
				SizeF emoteSz = new SizeF(Single.MaxValue, Single.MaxValue);
				// If current emote was BTTV stripped, this specifies last stripped char in split + 1
				int emoteStrippedCharsToIndex = -1;

				/*
				* Emojis:
				* 
				* Some emojis are supported by FFZ, some are supported by BTTV,
				* some are not supported by neither. If emoji is supported by both,
				* it seems that FFZ version is used.
				* 
				*/
				var emojiMatch = EmojiRegex.Match(split[x].Substring(curChar));
				if (Params.EmojiReplaceMode != EMOTE_PROVIDER.NONE && emojiMatch.Success && emojiMatch.Index == 0) {
					// Processing emoji...
					curEmoteProvider = Params.EmojiReplaceMode;
					curEmoteName = split[x].Substring(curChar, emojiMatch.Length);
					if (curEmoteProvider == EMOTE_PROVIDER.FFZ && !FFZIsEmojiSupported(curEmoteName))
						curEmoteProvider = EMOTE_PROVIDER.BTTV;
					if (curEmoteProvider == EMOTE_PROVIDER.BTTV && !BTTVIsEmojiSupported(curEmoteName)) {
						curEmoteName = null;
						curEmoteProvider = EMOTE_PROVIDER.NONE;
					}

					if (curEmoteProvider != EMOTE_PROVIDER.NONE) {
						emoteSz = curEmoteProvider == EMOTE_PROVIDER.BTTV ? new SizeF(20, 20) : new SizeF(18, 18);
						curEmoteProvider |= EMOTE_PROVIDER.EMOJI;
						curChar += emojiMatch.Length;
					}
				} else {
					// Processing emote...
					bool tryGetEmote()
					{
						// Try get BTTV emote
						curEmoteProvider = EMOTE_PROVIDER.BTTV;
						if (!BTTVGetEmote(curEmoteName, out emoteSz)) {
							curEmoteProvider = EMOTE_PROVIDER.FFZ;

							// Try get FFZ emote
							if (!FFZGetEmote(curEmoteName, out emoteSz)) {
								// Try get 7TV emote, apply same behavior as FFZ
								if (!SevenTVGetEmote(curEmoteName, out emoteSz)) {
									if (Params.TwitchEmotes == null)
										return false;

									// Try get channel sub emote (passed to this method by chat parser)
									curEmoteProvider = EMOTE_PROVIDER.NONE;
									string url;
									if (Params.TwitchEmotes.TryGetValue(curEmoteName, out url)) {
										emoteSz = TwitchGetEmote(curEmoteName, url);
										return true;
									}
									return false;
								}
							}
						}
						return true;
					}

					// Try to get emote normally, just crop the space at the end
					curEmoteName = split[x].Substring(curChar).TrimEnd(' ');
					if (!tryGetEmote()) {
						// No dice, try to:
						curEmoteName = split[x].Substring(curChar);
						emojiMatch = EmojiRegex.Match(curEmoteName);

						//	...limit current lookup to first possible emoji
						if (emojiMatch.Success)
							curEmoteName = curEmoteName.Substring(0, emojiMatch.Index);
						else
							curEmoteName = curEmoteName.TrimEnd(' '); // ...or just crop the space at the end

						//	...perform a BTTV strip
						var oldCurEmoteName = curEmoteName;
						curEmoteName = BTTVEmoteStrip.Replace(curEmoteName, "");

						/*
						 * If stripped anything, save what we stripped.
						 * 
						 * This works because there is nothing at the start of the string except stripped chars.
						 */
						if (oldCurEmoteName != curEmoteName)
							emoteStrippedCharsToIndex = oldCurEmoteName.Length;

						// Try to get emote again
						if (!tryGetEmote())
							curEmoteName = null;
					}
					if (emoteStrippedCharsToIndex != -1 &&
						!Params.ApplyBTTVStripToFFZ &&
						curEmoteProvider == EMOTE_PROVIDER.FFZ) {
						// If we stripped an FFZ emoji and Params.ApplyBTTVStripToFFZ wasn't specified, invalidate the emote.
						curEmoteName = null;
					}
				}

				if (curEmoteName != null) {
					/*
					* Emotes/Emojis:
					* 
					* First, apply margin to the emote if needed (-5 on top and bottom)
					* limited to CHAR_W_MARG (lowest boundary)
					* 
					* Check if upcoming emote is wider than the chat width:
					* 
					* if so then increase the final height (apply line margin if needed)
					* and put emote on a new line.
					* 
					* If not, increase the width and height, if this is the biggest emote
					* of current line yet.
					*/

					Debug.Assert(emoteSz.Width != Single.MaxValue);

					if (emoteSz.Width < 0) {
						// Zero width emote
						if (prevEmote != null) {
							// - space size
							emoteSz = new SizeF(-CharacterProperties[0x20].Width, 0);
						} else {
							// Treat as regular emote
							if (Params.EmoteMargin)
								emoteSz = new SizeF(-emoteSz.Width, Math.Max(-emoteSz.Height - 10, CHAR_W_MARG));
							else
								emoteSz = new SizeF(-emoteSz.Width, -emoteSz.Height);
						}

					} else if (Params.EmoteMargin)
						emoteSz = new SizeF(emoteSz.Width, Math.Max(emoteSz.Height - 10, CHAR_W_MARG));

					/*
					 * Images wrap to next line if line reaches the chat width, not even exceeding it (1px diff),
					 * so >= is required here.
					 */
					if (cur.Width + emoteSz.Width >= CHAT_WIDTH) {
						if (Params.LineMargin)
							finalH += Math.Max(cur.Height, CHAR_W_MARG);
						else
							finalH += cur.Height;
						cur.Height = emoteSz.Height;
						cur.Width = emoteSz.Width;
					} else {
						if (curEmoteProvider == EMOTE_PROVIDER.BTTV) {
							/*
							 * For BTTV emotes, apply 1.33px margin at emote which are on the start of the line,
							 * 1.33px * 2 for which are in the middle of the line
							 */
							bool notEndsWithSpace = (curChar + curEmoteName.Length) == split[x].Length || split[x][curChar + curEmoteName.Length] != ' ';
							if (cur.Width == 0 && notEndsWithSpace)
								cur.Width += 1.33f;
							else if (notEndsWithSpace)
								cur.Width += 1.33f * 2;
						}

						cur.Width += emoteSz.Width;
						cur.Height = Math.Max(cur.Height, emoteSz.Height);
					}

					/* 
					* Push curChar over the current processed emote and protect
					* Chatterino users if needed by leaving the prefix and suffix characters
					* that were otherwise removed by BTTV (emotes only).
					*/
					if (!curEmoteProvider.HasFlag(EMOTE_PROVIDER.EMOJI)) {
						if (emoteStrippedCharsToIndex != -1) {
							if (Params.AddStrippedEmoteCharsToCalc)
								split[x] = split[x].Substring(curChar, emoteStrippedCharsToIndex).Replace(curEmoteName, "");
							else
								curChar = emoteStrippedCharsToIndex;
						} else
							curChar += curEmoteName.Length;
					}

					prevEmote = curEmoteName;
					continue;
				}

				/*
				* Words:
				* 
				* Loop each character in current word, add its width to current line and adjust height if needed.
				* 
				* If current line is larger than the chat width and has not wrapped to next line once,
				* for example, message
				* 
				* |TETYYS: Very nice message with some word wra|pping
				* |                                            |
				* |                                            |
				* 
				* would wrap to
				* 
				* |TETYYS: Very nice message with some word    |
				* |wrapping                                    |
				* |                                            |
				* 
				* In situations where the whole word is wider than the whole chat width, it wraps twice, like so:
				* 
				* |TETYYS: Very nice message with some SPAMSPAM|SPAMSPAMSPAMSPAMSPAMSPAMSPAMSPAMSPAMSPAMSPAM
				* |                                            |
				* |                                            |
				* 
				* =>
				* 
				* |TETYYS: Very nice message with some 
				* |SPAMSPAMSPAMSPAMSPAMSPAMSPAMSPAMSPAMSPAMSPAM|
				* |SPAMSPAM                                    |
				* 
				* ===
				* 
				* Additionally, some special characters do not word wrap,
				* move to next line without moving the whole word and reset (enable)
				* the ability of subsequent whole words to move to the next line.
				* 
				* If this kind of character is encountered, save the position of it (+1),
				* to be able to go back to this character. Measure subsequent words and move them
				* to next line if the ability of this rule was reset (enabled) or wasn't used before
				* 
				* Apply margins, if specified.
				*/
				SizeF old = cur;
				bool wrappedOnce = false;
				int wrapReset = curChar;
				emojiMatch = EmojiRegex.Match(split[x].Substring(curChar));

				// Reset previous emote if we have something else
				if (split[x].Substring(curChar) != " ")
					prevEmote = null;

				for (int oldCurChar = curChar; curChar < split[x].Length; curChar++) {
					if (emojiMatch.Success && emojiMatch.Index == curChar - oldCurChar) {
						if (!FFZIsEmojiSupported(split[x].Substring(curChar, emojiMatch.Length)) &&
							!BTTVIsEmojiSupported(split[x].Substring(curChar, emojiMatch.Length))) {
							emojiMatch = EmojiRegex.Match(split[x].Substring(curChar + 1));
							oldCurChar = curChar + 1;
							// Emoji not supported, search for a new one
						} else {
							// Encountered emoji, let emote block process it
							break;
						}
					}

					int iChar = (int)split[x][curChar];
					if (Char.IsHighSurrogate(split[x][curChar]) &&
						curChar < split[x].Length - 1 &&
						Char.IsSurrogatePair(split[x][curChar], split[x][curChar + 1])) {
						iChar = Char.ConvertToUtf32(split[x][curChar], split[x][curChar + 1]);
						curChar++;
					}
					bool charWrapping;
					if (iChar < CharacterProperties.Length) {
						var props = CharacterProperties[iChar];
						sz = new SizeF(props.Width, 14);
						charWrapping = !Params.IgnoreCharWrappingRules && props.CharWrapping;
					} else {
						sz = new SizeF(9, 14);
						charWrapping = !Params.IgnoreCharWrappingRules;
					}

					cur.Width += sz.Width;
					cur.Height = Math.Max(cur.Height, sz.Height);

					if (charWrapping) {
						wrappedOnce = false;
						wrapReset = curChar + 1;
					}

					/*
					 * Characters on the other hand, wrap to the next line if they EXCEED chat width,
					 * so > is required here.
					 */
					if (cur.Width > CHAT_WIDTH) {
						if (wrappedOnce || charWrapping) {
							if (Params.LineMargin)
								finalH += Math.Max(cur.Height, CHAR_W_MARG);
							else
								finalH += cur.Height;
							cur.Width = sz.Width;
							cur.Height = sz.Height;
						} else {
							if (Params.LineMargin)
								finalH += Math.Max(old.Height, CHAR_W_MARG);
							else
								finalH += old.Height;
							cur.Width = 0;
							cur.Height = 0;

							/*
							 * Special case for space when wrapping it to next line -
							 * it gets lost and doesn't move current word to next line with itself.
							 */
							if (split[x][curChar] != ' ')
								curChar = wrapReset - 1; // Compensate for next increment
							wrappedOnce = true;
						}
					}
				}

				if (curChar == split[x].Length) {
					x++;
					curChar = 0;
				}
			}

			// Return final calculated height plus current line with line margins and whole message margins added, if specified.
			return finalH + (Params.LineMargin ? Math.Max(cur.Height, CHAR_W_MARG) : cur.Height) + (Params.WholeMargin ? 10.0f : 0.0f);
		}
	}
}
