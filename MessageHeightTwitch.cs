using MessageHeightTwitch.Emotes;
using MessageHeightTwitch.Measurement.EmoteRewriting;
using MessageHeightTwitch.Measurement.InlineCollection;
using MessageHeightTwitch.Measurement.LineFitting;
using MessageHeightTwitch.Measurement.ParagraphShaping;
using MessageHeightTwitch.Measurement.Shared;
using MessageHeightTwitch.Measurement.Shared.Shaping;

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Net.Http;
#if TEST
using System.Runtime.InteropServices;
#endif
using System.Threading;
using System.Threading.Tasks;

namespace MessageHeightTwitch
{
    /// <summary>
    /// Measures Twitch chat messages using shared fonts and channel emote catalogs.
    /// </summary>
    public partial class MessageHeightTwitch : IDisposable
    {
        internal readonly EmoteResolver Emotes;
        private readonly FontFallbackList ChannelFonts;
        private static bool Initialized;

        static MessageHeightTwitch()
        {
            Icu.Wrapper.Init();
        }

        /// <summary>Loads shared fonts and global emote catalogs before constructing instances.
        /// Repeated calls reuse successful resources; enabling 7TV later loads its global catalog.
        /// Cancellation and loading failures are thrown to the caller.</summary>
        public static async Task Init(string? FontDir = null, bool Enable7TVEmotes = false, CancellationToken CancellationToken = default)
        {
            CancellationToken.ThrowIfCancellationRequested();
            FontGlobalContext.Load(FontDir);
            await GlobalEmotes.Shared.Initialize(Enable7TVEmotes, CancellationToken).ConfigureAwait(false);
            CancellationToken.ThrowIfCancellationRequested();
            Volatile.Write(ref Initialized, true);
        }

        // Custom lookup consumers and the native shim can initialize fonts independently of catalogs.
        internal static void InitFonts(string? FontDir = null)
        {
            FontGlobalContext.Load(FontDir);
            Volatile.Write(ref Initialized, true);
        }

        internal static void CheckInit()
        {
            if (!Volatile.Read(ref Initialized))
                throw new InvalidOperationException("Call Init before constructing a MessageHeightTwitch instance.");
        }

        /// <summary>Loads channel emotes, throwing cancellation and loading failures.
        /// Call Init first. Dispose to stop subsequent image requests.</summary>
        public MessageHeightTwitch(string Channel, string ChannelId, bool Enable7TVEmotes = false, CancellationToken CancellationToken = default)
            : this(Channel, ChannelId, CancellationToken, Enable7TVEmotes) { }

        private readonly CancellationTokenSource EmoteLifetime;
        internal readonly TwitchEmotes? TwitchImages;

        public MessageHeightTwitch(
            EmoteLookup SevenTvGetEmote,
            EmoteLookup BTTVGetEmote,
            EmoteLookup FFZGetEmote,
            TwitchEmoteLookup TwitchGetEmote,
            EmojiSupport BTTVIsEmojiSupported,
            EmojiSupport FFZIsEmojiSupported)
        {
            CheckInit();
            EmoteLifetime = new();
            ChannelFonts = FontGlobalContext.Shared.FallbackList;
            Emotes = new EmoteResolver(SevenTvGetEmote, BTTVGetEmote, FFZGetEmote, TwitchGetEmote, BTTVIsEmojiSupported, FFZIsEmojiSupported);
        }

        /// <param name="Client">Serves the emote catalogs and images; the shared clients when null.</param>
        /// <param name="Globals">The global emote catalogs and image sizes; those every channel in the process shares when null.</param>
        internal MessageHeightTwitch(string Channel, string ChannelId, CancellationToken Token, bool Enable7TvEmotes,
            HttpClient? Client = null, GlobalEmotes? Globals = null)
        {
            CheckInit();
            Token.ThrowIfCancellationRequested();
            ChannelFonts = FontGlobalContext.Shared.FallbackList;
            EmoteLifetime = new();
            var loading = new EmoteLoading(Client, Globals);
            var ffz = new FFZEmoteProvider(loading);
            var bttv = new BTTVEmoteProvider(loading);
            var sevenTv = Enable7TvEmotes ? new SevenTVEmoteProvider(loading) : null;
            var sevenTvGlobal = Enable7TvEmotes ? sevenTv!.Global.TryGetSize : new EmoteLookup((ReadOnlySpan<char> name, out SizeF size) => { size = default; return false; });
            var sevenTvChannel = Enable7TvEmotes ? sevenTv!.Channel.TryGetSize : new EmoteLookup((ReadOnlySpan<char> name, out SizeF size) => { size = default; return false; });
            TwitchImages = new TwitchEmotes(loading);
            Emotes = new EmoteResolver(
                sevenTvChannel,
                bttv.Channel.TryGetSize,
                ffz.Room.TryGetSize,
                TwitchImages.GetEmote,
                bttv.IsEmojiSupported,
                ffz.IsEmojiSupported) {

                BTTVGlobal = bttv.Global.TryGetSize,
                FFZGlobal = ffz.Global.TryGetSize,
                SevenTvGlobal = sevenTvGlobal,
                BTTVProvider = bttv,
                FFZProvider = ffz
            };
            try {
                Task.WhenAll(
                    loading.Globals.Initialize(Enable7TvEmotes, Token, Client),
                    ffz.InitializeChannel(Channel, Token), bttv.InitializeChannel(ChannelId, Token),
                    sevenTv?.InitializeChannel(ChannelId, Token) ?? Task.CompletedTask)
                    .WaitAsync(Token).GetAwaiter().GetResult();
            } catch {
                EmoteLifetime.Dispose();
                throw;
            }
        }

        /// <summary>Loads Twitch image sizes before measurement. Unloaded images use a 28x28 placeholder.
        /// Throws on loading failure or cancellation. Custom Twitch lookups own their image loading.</summary>
        public async Task LoadTwitchEmotes(IEnumerable<string> Urls, CancellationToken CancellationToken = default)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken, EmoteLifetime.Token);
            linked.Token.ThrowIfCancellationRequested();
            if (TwitchImages != null)
                await TwitchImages.Load(Urls, linked.Token).ConfigureAwait(false);
        }

        public void Dispose() => EmoteLifetime.Cancel();

        [Flags]
        public enum EMOTE_PROVIDER
        {
            /// <summary>Original</summary>
            NONE = 1,

            /// <summary>BTTV (20x20 emojis)</summary>
            BTTV = 2,

            /// <summary>FFZ emoji images (21x21 at the target 14px font size).</summary>
            FFZ = 4,

            /// <summary>Is an emoji</summary>
            EMOJI = 8,

            SEVENTV = 16
        }

        public enum LOCALIZED_NAME_MODE
        {
            /// <summary>Calculate using username only</summary>
            USERNAME_ONLY,

            /// <summary>Calculate using display name only</summary>
            DISPLAY_NAME_ONLY,

            /// <summary>Calculate using both. If username and display name are the same, only display name is used</summary>
            USERNAME_DISPLAY_NAME
        }

        public struct CalculateMessageHeightParams
        {
            /// <summary>Measure a /me body (without the command), with a space separator and italic text.</summary>
            public bool IsAction;

            /// <summary>Number of badges on the left side of the username</summary>
            public int NumberOfBadges;

            /// <summary>Twitch emotes, name to image url</summary>
            public Dictionary<string, string>? TwitchEmotes;

            /// <summary>Apply negative margins to emotes, making large emotes overlap on other lines</summary>
            public bool EmoteMargin;

            /// <summary>Apply margins to individual lines</summary>
            public bool LineMargin;

            /// <summary>Apply margins to whole message at the end</summary>
            public bool WholeMargin;

            /// <summary>
            /// What provider to use for replacing emojis. Different providers
            /// have different image sizes, influencing final height.
            /// Note that not all emotes are replaced to their BTTV and FFZ versions
            /// and this would require a list of replaceble emojis to solve this.
            /// </summary>
            public EMOTE_PROVIDER EmojiReplaceMode;

            /// <summary>Which username to use when calculating size</summary>
            public LOCALIZED_NAME_MODE LocalizedNameMode;

            // The FFZ sets granted to the sender's login, such as Subwoofer Emote Effects. Set for each message.
            internal EmoteSet[]? SenderEmotes;

            public static CalculateMessageHeightParams Default => new CalculateMessageHeightParams {
                NumberOfBadges = 0,
                TwitchEmotes = null,
                EmoteMargin = true,
                LineMargin = true,
                WholeMargin = true,
                EmojiReplaceMode = EMOTE_PROVIDER.FFZ,
                LocalizedNameMode = LOCALIZED_NAME_MODE.USERNAME_DISPLAY_NAME
            };
        }

        /// <summary>Measures the height of Twitch chat message</summary>
        /// <param name="Input">Input message</param>
        /// <param name="Username">Poster username</param>
        /// <param name="DisplayName">Poster display name</param>
        /// <param name="NumberOfBadges">Number of badges associated with the poster</param>
        /// <param name="AdditionalEmotes">Custom emote to size map (primarily used for channel emotes)</param>
        /// <returns>Height of measured message in pixels</returns>
        public float CalculateMessageHeight(ReadOnlySpan<char> Input, ReadOnlySpan<char> Username, ReadOnlySpan<char> DisplayName, int NumberOfBadges, Dictionary<string, string> TwitchEmotes)
        {
            var param = CalculateMessageHeightParams.Default;
            param.NumberOfBadges = NumberOfBadges;
            param.TwitchEmotes = TwitchEmotes;
            return CalculateMessageHeightEx(Input, Username, DisplayName, param);
        }

        private static void ValidateLength(ReadOnlySpan<char> Text, int Maximum, string ParameterName)
        {
            if (Text.Length <= Maximum)
                return;
            // Every rune takes one or two UTF-16 units, so a text longer than twice the maximum has too many.
            var count = Text.Length > Maximum * 2 ? Maximum : 0;
            foreach (var _ in Text.EnumerateRunes())
                if (++count > Maximum)
                    throw new ArgumentException($"Must contain at most {Maximum} runes.", ParameterName);
        }

        public delegate bool EmoteLookup(ReadOnlySpan<char> Name, out SizeF Size);
        public delegate SizeF TwitchEmoteLookup(ReadOnlySpan<char> Name, string Url);
        public delegate bool EmojiSupport(ReadOnlySpan<char> Emoji);

        // The measurement contract accepts at most 25 runes per name and 500 per message.
        public const int MAX_NAME_RUNES = 25;
        internal const int MAX_MESSAGE_RUNES = 500;
        // Logins are ASCII; display names and the body may use surrogate pairs. Formatting adds five units.
        public const int MAX_PARAGRAPH_UTF16 = MAX_MESSAGE_RUNES * 2 + MAX_NAME_RUNES * 3 + 5;

        public float CalculateMessageHeightEx(ReadOnlySpan<char> Input, ReadOnlySpan<char> Username, ReadOnlySpan<char> DisplayName, CalculateMessageHeightParams Parameters)
        {
            ValidateLength(Input, MAX_MESSAGE_RUNES, nameof(Input));
            ValidateLength(Username, MAX_NAME_RUNES, nameof(Username));
            ValidateLength(DisplayName, MAX_NAME_RUNES, nameof(DisplayName));
            Parameters.SenderEmotes = Emotes.SenderEmotes(Username);
            var ws = MeasurementWorkspace.Pool.Get();
            try {
                return CalculateHeightCore(Input, Username, DisplayName, Parameters, ws);
            } finally {
                MeasurementWorkspace.Pool.Return(ws);
            }
        }

        private float CalculateHeightCore(ReadOnlySpan<char> Input, ReadOnlySpan<char> Username, ReadOnlySpan<char> DisplayName, CalculateMessageHeightParams Parameters, MeasurementWorkspace Ws)
        {
            var message = Ws.Collection.Modifiers.Rewrite(Input, Parameters, Emotes);
            return new ChatMessageLayout(this, message, Username, DisplayName, Parameters, Ws).LayOut();
        }

        /// <summary>
        /// Coordinates stages 2-4 (inline collection, paragraph shaping and line fitting) after emote rewriting.
        /// Stage 5 (height accumulation): commits line geometry, applies chat margins and padding, and returns the total height.
        /// </summary>
        private ref struct ChatMessageLayout
        {
            private const float CHAT_WIDTH = 293;
            private const float WIDTH_TO_FIT = CHAT_WIDTH + 1 / 64f;
            private const int MINIMUM_LINE_HEIGHT = 22;
            private const float BADGE_SLOT_WIDTH = 21;
            private const int MESSAGE_PADDING = 8;
            private readonly string Separator => Parameters.IsAction ? " " : ChatParagraphBuilder.Separator;
            private static readonly SizeF EMPTY_LINE = new(0, global::MessageHeightTwitch.Fonts.TWITCH_FONT_SIZE);

            private readonly FontFallbackList Fonts;
            private readonly RewrittenMessage Message;
            private readonly ReadOnlySpan<char> Input;
            private readonly CalculateMessageHeightParams Parameters;
            private readonly CollectionWorkspace Collection;
            private readonly ShapingWorkspace Shaping;
            private readonly ParagraphShapingWorkspace ParagraphShaping;
            private readonly LineBreakingWorkspace LineBreaking;
            private readonly InlineItemsData Paragraph;
            private readonly ReadOnlySpan<char> Login;
            private readonly ReadOnlySpan<char> DisplayName;
            private readonly ReadOnlySpan<char> LoginSuffix;
            private readonly bool AuthorNameIsLogin;
            private readonly ReadOnlySpan<char> AuthorName => Parameters.LocalizedNameMode == LOCALIZED_NAME_MODE.USERNAME_ONLY ? Login : DisplayName;
            private readonly PARAGRAPH_REGION AuthorNameRegion => Parameters.LocalizedNameMode == LOCALIZED_NAME_MODE.USERNAME_ONLY ? PARAGRAPH_REGION.LOGIN : PARAGRAPH_REGION.DISPLAY_NAME;

            internal ChatMessageLayout(MessageHeightTwitch Measurer, RewrittenMessage Message, ReadOnlySpan<char> Login, ReadOnlySpan<char> DisplayName, CalculateMessageHeightParams Parameters, MeasurementWorkspace Ws)
            {
                Fonts = Measurer.ChannelFonts;
                this.Message = Message;
                Input = Message.Text;
                this.Parameters = Parameters;
                this.Login = Login;
                this.DisplayName = DisplayName;
                Collection = Ws.Collection;
                Shaping = Ws.Shaping;
                ParagraphShaping = Ws.ParagraphShaping;
                LineBreaking = Ws.LineBreaking;
                Paragraph = Ws.Paragraph;

                // The selected author name is bold; a differing display name can add a regular-weight login suffix.
                var mode = Parameters.LocalizedNameMode;
                if (mode is < LOCALIZED_NAME_MODE.USERNAME_ONLY or > LOCALIZED_NAME_MODE.USERNAME_DISPLAY_NAME)
                    throw new ArgumentException(nameof(Parameters.LocalizedNameMode));

                var sameName = DisplayName.Equals(Login, StringComparison.OrdinalIgnoreCase);
                AuthorNameIsLogin = mode == LOCALIZED_NAME_MODE.USERNAME_ONLY || sameName;
                if (mode == LOCALIZED_NAME_MODE.USERNAME_DISPLAY_NAME && !sameName) {
                    PooledBuffers.Ensure(ref Collection.LoginSuffixText, Login.Length + 3);
                    var name = Collection.LoginSuffixText.AsSpan(0, Login.Length + 3);
                    " (".CopyTo(name);
                    Login.CopyTo(name[2..]);
                    name[^1] = ')';
                    LoginSuffix = name;
                }
            }

            internal float LayOut()
            {
                if (TryLayOutSingleEmote(out var height))
                    return height;

                CollectParagraph();
                var shaper = new ParagraphShaper(Fonts, Paragraph, ParagraphShaping, Shaping,
                    AuthorNameIsLogin ? 0..AuthorName.Length : null);
                shaper.Prepare();
                var breaker = new ParagraphLineBreaker(Paragraph, shaper, Shaping, LineBreaking, WIDTH_TO_FIT, Parameters.EmoteMargin);
                var badges = new SizeF(Parameters.NumberOfBadges * BADGE_SLOT_WIDTH, Parameters.NumberOfBadges > 0 && Parameters.LineMargin ? LineMetrics.MiddleAlignedLineHeight(20) : global::MessageHeightTwitch.Fonts.TWITCH_FONT_SIZE);
                var pass = new LineLayoutState(badges, Parameters.LineMargin ? MINIMUM_LINE_HEIGHT : float.NegativeInfinity) {
                    Leading = Parameters.NumberOfBadges == 0
                };
#if TEST
                LineBreaking.LineStarts?.Clear();
#endif
                for (var start = 0; start < Paragraph.Length;) {
                    var line = breaker.NextLine(start, Paragraph.Length, pass.Current, pass.Leading);
                    pass.Current = line.Size;
                    if (line.Next == Paragraph.Length)
                        break;
                    int? source = line.End >= Paragraph.BodyStart || line.Next >= Paragraph.BodyStart
                        ? Paragraph.SourceOffsets[Math.Max(line.End, Paragraph.BodyStart)] : null;
                    EndLine(ref pass, source);
                    start = line.Next;
                }
                return pass.Finish(Parameters.WholeMargin ? MESSAGE_PADDING : 0);
            }

            /// <summary>
            /// A single resolved atomic emote needs only the preamble's width and its final image box.
            /// Keep name wrapping and names the ASCII tables cannot measure on the general layout path.
            /// </summary>
            private readonly bool TryLayOutSingleEmote(out float Height)
            {
                Height = 0;
                if (Message.Emotes.Count != 1 || !Message.Emotes.TryGetValue(0, out var image) ||
                    image.Length != Input.Length || image.InlineImage ||
                    !Fonts.AsciiRegular.Accepts(LoginSuffix))
                    return false;

                long loginWidth = 0;
                var measuredLogin = AuthorNameIsLogin && Fonts.AsciiBold.TryMeasureLogin(AuthorName, out loginWidth);
                if (!measuredLogin && !Fonts.AsciiBold.Accepts(AuthorName))
                    return false;

                static float width(ReadOnlySpan<char> Text, AsciiFastPath Table) =>
                    Text.IsEmpty ? 0 : HarfBuzz.FixedToFloat(TextWidths.SnapFixed(Table.PrefixFixed(Text, Text.Length)));

                // Each style item is snapped separately, as in the prepared paragraph's item projections.
                var preamble = Parameters.NumberOfBadges * BADGE_SLOT_WIDTH + (measuredLogin ? HarfBuzz.FixedToFloat(TextWidths.SnapFixed(loginWidth)) : width(AuthorName, Fonts.AsciiBold)) + width(LoginSuffix, Fonts.AsciiRegular) + width(Separator, Fonts.AsciiRegular);
                if (preamble > WIDTH_TO_FIT)
                    return false;

#if TEST
                LineBreaking.LineStarts?.Clear();
#endif
                var badges = Parameters.NumberOfBadges > 0 && Parameters.LineMargin
                    ? LineMetrics.MiddleAlignedLineHeight(20) : global::MessageHeightTwitch.Fonts.TWITCH_FONT_SIZE;
                var pass = new LineLayoutState(new SizeF(preamble, badges), Parameters.LineMargin ? MINIMUM_LINE_HEIGHT : float.NegativeInfinity);
                var size = InlineSize(image);
                if (preamble + size.Width > WIDTH_TO_FIT)
                    EndLine(ref pass, 0);
                pass.Current.Height = Math.Max(pass.Current.Height, size.Height);
                Height = pass.Finish(Parameters.WholeMargin ? MESSAGE_PADDING : 0);
                return true;
            }

#if TEST
            /// <summary>Projects the prepared paragraph onto the plain body for independent browser range tests.</summary>
            internal ClassifiedText ClassifyMessageText(out int Lead, out float Indent)
            {
                CollectParagraph();
                var shaper = new ParagraphShaper(Fonts, Paragraph, ParagraphShaping, Shaping,
                    AuthorNameIsLogin ? 0..AuthorName.Length : null);
                shaper.Prepare();
                var breaker = new ParagraphLineBreaker(Paragraph, shaper, Shaping, LineBreaking, WIDTH_TO_FIT, Parameters.EmoteMargin);
                Lead = 0;
                if (Paragraph.BodyStart < Paragraph.Length) {
                    var item = Paragraph.LineItems[Paragraph.LineItemAt(Paragraph.BodyStart)];
                    if (item.Image == null) {
                        var group = Paragraph.Groups[item.Group];
                        shaper.EnsureItemShape(Paragraph.LineItemAt(Paragraph.BodyStart));
                        Lead = Paragraph.NextCluster(Paragraph.BodyStart, group) - Paragraph.BodyStart;
                    }
                }

                var pass = new LineLayoutState(default, float.NegativeInfinity);
                for (var start = 0; start < Paragraph.BodyStart;) {
                    var line = breaker.NextLine(start, Paragraph.BodyStart, pass.Current, pass.Leading);
                    pass.Current = line.Size;
                    if (line.Next == Paragraph.BodyStart)
                        break;
                    pass.EndLine(EMPTY_LINE);
                    start = line.Next;
                }
                Indent = pass.Current.Width;
                var body = Paragraph.BodyStart + Lead;
                var runs = LineBreaking.PieceRuns;
                runs.Clear();
                foreach (var run in Paragraph.Runs) {
                    if (run.Range.End.Value <= body)
                        continue;
                    runs.Add(run with {
                        Range = (Math.Max(run.Range.Start.Value, body) - body)..(run.Range.End.Value - body),
                        ShapingRange = null
                    });
                }
                Paragraph.SliceBidi(body, Paragraph.Length, LineBreaking.PieceBidi);
                if (LineBreaking.PieceBidi.Count == 0)
                    LineBreaking.PieceBidi.Add((0, HarfBuzzSharp.Direction.LeftToRight));
                return new(Paragraph.Text[body..], CollectionsMarshal.AsSpan(runs), CollectionsMarshal.AsSpan(LineBreaking.PieceBidi));
            }
#endif

            private readonly SizeF InlineSize(EmoteLayout Image) => LineMetrics.InlineSize(Image, Parameters.EmoteMargin);

            private readonly void EndLine(ref LineLayoutState Pass, int? SourceStart)
            {
                Pass.EndLine(EMPTY_LINE);
#if TEST
                if (SourceStart is { } start)
                    LineBreaking.LineStarts?.Add(start);
#endif
            }

            private readonly void CollectParagraph()
            {
                var builder = new ChatParagraphBuilder(Message, AuthorName, AuthorNameRegion, LoginSuffix, Collection, Paragraph, Parameters.IsAction);
                builder.Build();
                LineBreaking.BeginParagraph();
            }
        }

#if TEST
        /// <summary>The message's height, recording in <paramref name="LineStarts"/> the message offsets where its lines start.</summary>
        internal float CalculateMessageHeightEx(ReadOnlySpan<char> Input, ReadOnlySpan<char> Username, ReadOnlySpan<char> DisplayName, CalculateMessageHeightParams Parameters, System.Collections.Generic.List<int> LineStarts)
        {
            Parameters.SenderEmotes = Emotes.SenderEmotes(Username);
            var ws = MeasurementWorkspace.Pool.Get();
            ws.LineBreaking.LineStarts = LineStarts;
            try {
                return CalculateHeightCore(Input, Username, DisplayName, Parameters, ws);
            } finally {
                ws.LineBreaking.LineStarts = null;
                MeasurementWorkspace.Pool.Return(ws);
            }
        }

        /// <summary>
        /// The message's text after the characters the separator's cluster took (<paramref name="Lead"/>), classified as
        /// the layout classifies the item it is in a message without inline objects or mentions: after the names and the
        /// separator. <paramref name="Indent"/> is the width of the line before it. The text lives in <paramref name="Ws"/>.
        /// </summary>
        internal ClassifiedText ClassifyMessageText(ReadOnlySpan<char> Input, ReadOnlySpan<char> Username, ReadOnlySpan<char> DisplayName, CalculateMessageHeightParams Parameters, MeasurementWorkspace Ws, out int Lead, out float Indent)
        {
            Parameters.SenderEmotes = Emotes.SenderEmotes(Username);
            var message = Ws.Collection.Modifiers.Rewrite(Input, Parameters, Emotes);
            return new ChatMessageLayout(this, message, Username, DisplayName, Parameters, Ws).ClassifyMessageText(out Lead, out Indent);
        }
#endif
    }
}
