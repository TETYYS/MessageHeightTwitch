using System;
using System.Collections.Generic;
using Xunit;
using Xunit.Abstractions;

namespace Tests
{
    public class UnitTest
    {
        private readonly ITestOutputHelper output;

        public UnitTest(ITestOutputHelper output)
        {
            this.output = output;
        }

        [Fact]
        public void Test()
        {
			var tests = new List<KeyValuePair<Tuple<string, string, string, int, Dictionary<string, string>>, float>>() {
				KeyValuePair.Create(Tuple.Create<string, string, string, int, Dictionary<string, string>>(
					"Just 'test' 4HEad", "TETYYS", "test", 1, null), 30.89f
				),
				KeyValuePair.Create(Tuple.Create<string, string, string, int, Dictionary<string, string>>(
					"GGX GANG", "TETYYS", "♿ GGX ♿ GANG ♿ COMING ♿ THROUGH ♿", 1, null), 50.89f
				),
				KeyValuePair.Create(Tuple.Create<string, string, string, int, Dictionary<string, string>>(
					"FeelsGoodMan Clap fruits", "TETYYS", "🍓🍑🍊🍋🍍 NaM 🍐🍏🐬🐳NaM🍆🐙🌷🐷🍓🍑🍊🍋🍍 NaM 🍐🍏🐬🐳NaM🍓🍑🍊🍋🍍 NaM 🍐🍏🐬🐳NaM🍆🐙🌷🐷🍓🍑🍊🍋🍍 NaM 🍐🍏🐬🐳NaM🍓🍑🍊🍋🍍 NaM 🍐🍏🐬🐳NaM🍆🐙🌷🐷🍓🍑🍊🍋🍍 NaM 🍐🍏🐬🐳NaM🍓🍑🍊🍋🍍 NaM 🍐🍏🐬🐳NaM🍆🐙🌷🐷🍓🍑🍊🍋🍍 NaM 🍐🍏🐬🐳NaM🍓🍑🍊🍋🍍 NaM 🍐🍏🐬🐳NaM🍆🐙🌷🐷🍓🍑🍊🍋🍍 NaM 🍐🍏🐬🐳NaM🍓🍑🍊🍋🍍 NaM 🍐🍏🐬🐳NaM🍆🐙🌷🐷🍓🍑🍊🍋🍍 NaM 🍐🍏🐬🐳NaM🍓🍑🍊🍋🍍 NaM 🍐🍏🐬🐳NaM🍆🐙🌷🐷🍓🍑🍊🍋🍍 NaM 🍐🍏🐬🐳NaM🍓🍑🍊🍋🍍 NaM 🍐🍏🐬🐳NaM🍆🐙🌷🐷🍓🍑🍊🍋🍍 NaM 🍐🍏🐬🐳NaM🍓🍑🍊🍋🍍 NaM 🍐🍏🐬🐳NaM🍆🐙🌷🐷🍓🍑🍊🍋🍍 NaM 🍐🍏🐬🐳NaM🍓🍑🍊🍋🍍 NaM 🍐🍏🐬🐳NaM🍆🐙🌷🐷🍓🍑🍊🍋🍍 NaM 🍐🍏🐬🐳NaM🍓🍑🍊🍋🍍 NaM 🍐🍏🐬🐳NaM🍆🐙🌷🐷🍓🍑🍊🍋🍍 NaM 🍐🍏🐬🐳NaM🍓🍑🍊🍋🍍 NaM 🍐🍏🐬🐳", 1, null), 700f
				),
				KeyValuePair.Create(Tuple.Create<string, string, string, int, Dictionary<string, string>>(
					"FeelsGoodMan Clap Emoji spam", "TETYYS", "🍁😄😊😃☺😉😍😘😚😳😌😌😁😜😝😒😒😏😏😓😔😞😖😖😰😨😣😢😢😭😂😲😱💛👽👿👿😷😪😡😠💙💜💗💚❤💔💓💘💨👎✨💦👌🌟🎶👊✊💢💢❕🔥✌👋💩❔💤👍✋👐☝👯🙆🙆👆👇💪🙅💁🚶👉👈🏃🙇💏💏💃🙌🙌🙏💃💑💆👶👮👮👼👵💇💅👸👸💂👱👦👧👲👲💀👣👳👩👨👷👷💋🍀🌴🌵🌹🌵🌾🌻🐚🌺🍃🍃🍂🐘🐙🐙🌷🌸🐛🐑🐫🐧🐧💐🐔🐔🐎🐤🐳🐟🐦🐒🐵🐍🐠🌀🐺🐗🐮🐰⚡🌙🐹🐷🐻🐭⛄☁🐶🐨🐯☔☔☀🌊🐸🎍🎇🎁🔔🎐💝🎎🎑🎉🎈🎈🎃🎒🎓👻💿📀🎏🎏🎆🎄📷📼🔍🔓🔊💻🎥📺📢🔒🔑📣📱📠📻✂🔨📠☎➿💡📲🏈🏈🏀📩📫🚬⚽⚾⚾📮🛀🛀🎾⛳💊🚽💺💉🎱🏊🏆🎨🎤👾🏄🎯🎧🎧🎺🀄♠♥🎬🎷🎷🎸📝♣♦📖〽👟👘💼👜👡👠👠🎀💄💍🎩👢👕👑💎☕☕👒👔👗🌂🍵🍺🍜🍜🍲🍻🍻🍱🍞🍞🍳🍣🍶🍴🍙🍢🍡🍘🍔🍔🍟🍚🍦🍧🍆🍅🎂🍰🍎🍊🍉🍓🍊🌄🎢🚗🚗🚢🌃🌃🚕🚌🚌🗽🗽✈🚓🚒🎡🎡🚒🚒🚲⛲🌈🏠🏩🏧🏯🏨🏫💒🏰🏰⛺⛪🏣🏥🏬🏭🏭🗼🌇🏦🌆🗻🇫🇷🇪🇸🇮🇹🇷🇺🇩🇪🇺🇸🇨🇳💈⛽🎫🎰🇰🇷🇰🇷🇯🇵🔰🚄🚉🚧🎌🏁⚠🚃🚚🚥♨1⃣1⃣3⃣4⃣5⃣6⃣7⃣⬅🈵🈵🈵🈁🆕🔝🆙🆒🈹🉐🈳🈵🈺🈯🈯🈶🈶🈶🈚🈷🈸🈸🈂🚻🚹🚺🚼🚭♿♿✳🔞㊗㊙🚾🚇✴💟📴💹💹♈♉♊♋♋♌♎♎🔯⛎♒♒♑♒♓⛎⛎⛎♎♍♌♊♑🅰🅱🆎🆎🔲🔲🔴🔳🔳🕛🕐🕑🕒🕓🕔🕕❌🕚🕘🕗🕖©®™⁭", 1, null), 590.0f
				),
				/*KeyValuePair.Create(Tuple.Create<string, string, string, int, Dictionary<string, string>>(
					"??? (sub emote test)", "TETYYS", "———————————————————————— forsenDiglett ??? ????? ????? ??????????? ??????? forsenDiglett ——————————————————————— ⁭", 1, new Dictionary<string, string>() { { "forsenDiglett", "https://static-cdn.jtvnw.net/emoticons/v1/122255/1.0" } }), 90.89f
				),*/
				KeyValuePair.Create(Tuple.Create<string, string, string, int, Dictionary<string, string>>(
					"Original dick gachiBASS", "TETYYS", "_______________________ ░░░░█─────────────█──▀── ░░░░▓█───────▄▄▀▀█────── ░░░░▒░█────▄█▒░░▄░█───── ░░░░░░░▀▄─▄▀▒▀▀▀▄▄▀──DO─ ░░░░░░░░░█▒░░░░▄▀───YOU─ ▒▒▒░░░░▄▀▒░░░░▄▀───LIKE─ ▓▓▓▓▒░█▒░░░░░█▄───WHAT─ █████▀▒░░░░░█░▀▄───YOU── █████▒▒░░░▒█░░░▀▄─SEE?── ███▓▓▒▒▒▀▀▀█▄░░░░█────── ▓██▓▒▒▒▒▒▒▒▒▒█░░░░█───── ▓▓█▓▒▒▒▒▒▒▓▒▒█░░░░░█──── ░▒▒▀▀▄▄▄▄█▄▄▀░░░░░░░.", 1, null), 290.89f
				),
				KeyValuePair.Create(Tuple.Create<string, string, string, int, Dictionary<string, string>>(
					"SHUTTHEFUCKUPWEEBS NaM", "TETYYS", "SHUTTHEFUCKUPWEEBS NaM SHUTTHEFUCKUPWEEBS NaM SHUTTHEFUCKUPWEEBS NaM SHUTTHEFUCKUPWEEBS NaM SHUTTHEFUCKUPWEEBS NaM SHUTTHEFUCKUPWEEBS NaM SHUTTHEFUCKUPWEEBS NaM SHUTTHEFUCKUPWEEBS NaM SHUTTHEFUCKUPWEEBS NaM SHUTTHEFUCKUPWEEBS NaM SHUTTHEFUCKUPWEEBS NaM SHUTTHEFUCKUPWEEBS NaM SHUTTHEFUCKUPWEEBS NaM SHUTTHEFUCKUPWEEBS NaM SHUTTHEFUCKUPWEEBS NaM", 1, null), 460f
				),
				KeyValuePair.Create(Tuple.Create<string, string, string, int, Dictionary<string, string>>(
					"Ocean man", "TETYYS", "OCEAN MAN 🌊😍Take me by the hand ✋lead me to the land that you understand 🙌🌊OCEAN MAN 🌊😍The voyage 🚲to the corner of the 🌎globe is a real trip 👌🌊OCEAN MAN 🌊😍The crust of a tan man 👳imbibed by the sand 👍Soaking up the 💦thirst of the land 💯⁭", 1, null), 130.89f
				),
				KeyValuePair.Create(Tuple.Create<string, string, string, int, Dictionary<string, string>>(
					"Hydra dick nymnHands", "TETYYS", "───────────────── ▄▄▀▀▀▀█──────▄▄▀▀▀▀█─── █▒░░▄░█─────▄█▒░░▄░█──── █▀▀▀▄▄▀───▄▀▒▀▀▀▄▄▀───── █▒░░░█──█▒░░░░▄▀─▄▄▀▀▀▀█ █▒░░░█─█▒░░░░▄▀─▄█▒░░▄░█ █▒░░░█▒█░░░░█─▄▄▀▒▀▀▀▄▄▀ █▒▒▒▒▒▒▀▒▒▒▒▒▀░░░░░░▄▀ ██▒▒▒▒▒░░░░░░░░░░▀▀▄▄── ███▓▓▒▒▒▀▀▀█▄▒▒░░░█░░▀▀▄ ▓██▓▒▒▒▒▒▒▒▒▒█▀▀▄▄█▒░▄░█ ▓▓█▓▒▒▒▒▒▒▓▒▒█░░░░▀▀▄▄▄█░ ░▒▒▀▀▄▄▄▄█▄▄▀░░░░░░░ ⁭", 1, null), 270.89f
				),
				KeyValuePair.Create(Tuple.Create<string, string, string, int, Dictionary<string, string>>(
					"Cock o' clock", "TETYYS", "🕕🕖🕗🕘🕙🕚🕛🕐🕑🕐🕑🕒🔴🔴🔴🔴🔴🔴🕙🕚🕛🕐🕑🕐🕑🕒🕓🔴🕕🕖🕗🕘🕙🕚🔴🕐🕑🕐🕑🕒🕓🕔🕕🔴🕗🔴🕙🕚🔴🕐🔴🕐🕑🕒🕓🕔🕕🕖🔴🕘🕙🕚🕛🕐🕑🕐🕑🔴🕓🕔🕕🕖🕗🕘🔴🕚🔴🕐🕑🕐🕑🔴🕓🔴🕕🕖🕗🕘🕙🕚🔴🕐🕑🔴🔴🔴🔴🕔🕕🔴🕗🕘🕙🕚🕛🕐🕑🔴🕑🕒🕓🕔🕕🕖🔴🕘🕙🕚🕛🕐🕑🕐🕑🕒🔴🕔🕕🕖🕗🔴🕚🕛🕐🕑🕙🕐🕑🕒🕓🕔🔴🕖🕗🕘🕚🔴🕐🕑🕐🕑🕙🕒🕓🕔🕕🕖🔴🕘🕙🕚🕐🔴🕐🕑🕒🕓🕙🕔🕕🕖🕗🕘🔴🕚🕛🕐🕐🔴🕒🕓🕔🕕🕙🕖🕗🕘🕙🕚🔴🕐🕑🕑🕒🔴🕔🕕🕖🕗🕙🕘🕙🕚🕛🕐🔴🕐🕒🕓🕔🔴🕖🕗🕘🕙🕙🕚🕛🕐🕑🕐🔴🕒🕔🕕🕖🔴🕘🕙🕚🕛🕙🕐🕑🕐🕑🕒🔴🕔🕖🕗🕘🔴🕚🕛🕐🕑🕙🕐🕑🕒🕓🕔🔴🕖🕘🕙🕚🔴🕐🕑🕐🕑🕙🕒🕓🕔🕕🕖🔴🕘🕚🕛🕐🔴🕐🕑🕒🕓🕙🕔🕕🕖🕗🕘🔴🕚🕐🕑🕐🔴🕒🕓🕔🕕🕙🕖🕗🕘🕙🕚🔴🕐🕐🕑🕒🔴🕔🕕🕖🕗🕙🕘🕙🕚🕛🕐🔴🕐🕒🕓🕔🔴🕖🕗🕘🕙🕙🕚🕛🕐🕑🕐🔴🕒🕔🕕🕖🔴🕘🕙🕚🕛🕙🕐🕑🔴🔴🔴🕔🕕🕖🕗🕘🕚🔴🔴🔴🕐🕑🕐🔴🕔🕕🕖🕖🕖🕖🕖🕗🕘🕙🕚🕛🔴🕑🔴🕔🕕🕖🕗🕘🕙🕚🕛🕐🕑🕐🕑🕓🕐🔴🔴🕖🕗🕘🕙🕚🕛🕐🕑🕐🕑🕒🕓🕕🕐🔴🔴🕘🕙🕚🕛🕐🕑🕗🕘🕙🕚🕛🕐🕐🕐🔴🔴🕑🕓🕔🕕🕖🕗🕘🕙🕚🕛🕐🕑🕗🕙🔴🕚🔴🔴🕐🕑🕗🕒🔴🔴🕕🕖🕗🕗🔴🔴🕛🕐🕑🕗🔴🔴🔴🔴🕐🕑🔴🔴🔴🔴🕔🕕🕖", 1, null), 610.89f
				),
				KeyValuePair.Create(Tuple.Create<string, string, string, int, Dictionary<string, string>>(
					"WutFace", "TETYYS", "░░░░▄███▓███████▓▓▓░░░░ ░░░███░░░▒▒▒██████▓▓░░░ ░░██░░░░░░▒▒▒██████▓▓░░ ░██▄▄▄▄░░░▄▄▄▄█████▓▓░░ ░██░(◐)░░░▒(◐)▒███████▓▓░ ░██░░░░░░░▒▒▒▒▒█████▓▓░ ░██░░░▀▄▄▀▒▒▒▒▒█████▓▓░ ░█░███▄█▄█▄███░█▒████▓▓░ ░█░███▀█▀█▀█░█▀▀▒█████▓░ ░█░▀▄█▄█▄█▄▀▒▒▒▒█████▓░ ░████░░░░░░▒▓▓███████▓░ ░▓███▒▄▄▄▄▒▒▒▒████████░ ░▓▓██▒▓███████████████", 1, null), 270.89f
				),
				KeyValuePair.Create(Tuple.Create<string, string, string, int, Dictionary<string, string>>(
					"FAT COCK gachiBASS", "TETYYS", "__________________________ ─────────────────▄▀───── █───────────▄▄▄▄▄▄▄▄▄─── ▓█───────▄▄▀░░░░░░░░░▀▄─ ▒░█────▄█▒░░░░░▄░░░░░░█─ ░░▀▄─▄▀▒▀▀▀▀▀▀▄▄▄▄▄▄░▄▀─ ░░░░█▒░░░░░░░░░░░░░░▄▀── ░░░█▒░░░░░░░░░░░░░░▄▀─── ░▄▀▒░░░░░░░░░░░░░░▄▀─DO─ ▓█▒░░░░░░░░░░░░░░█─YOU── ██▀▒░░░░░░░░░░░░█─LIKE── ██▒▒░░░▒▒░░░░░░█▀▄──WHAT ▓▓▒▒▒▒▒▒▒▒▒▀▀█▄▀░░█─YOU ▓▒▒▒▒▒▒▒▒▒▒▒▒▒▒█░░░█SEE? ▓▒▒▒▒▒▒▒▓▒▒▒▒▒▒█░░░░░█── ▀▀▄▄▄▄▄▄█▄▄▄▄▄▀░░░░░░░█─", 1, null), 330.89f
				),
				KeyValuePair.Create(Tuple.Create<string, string, string, int, Dictionary<string, string>>(
					"Limp dick", "TETYYS", "────────────▄▀░░░░░▒▒▒█─ ───────────█░░░░░░▒▒▒█▒█ ──────────█░░░░░░▒▒▒█▒░█ ────────▄▀░░░░░░▒▒▒▄▓░░█ ───────█░░░░░░▒▒▒▒▄▓▒░▒▓ ──────█▄▀▀▀▄▄▒▒▒▒▓▀▒░░▒▓ ────▄▀░░░░░░▒▀▄▒▓▀▒░░░▒▓ ───█░░░░░░░░░▒▒▓▀▒░░░░▒▓ ───█░░░█░░░░▒▒▓█▒▒░░░▒▒▓ ────█░░▀█░░▒▒▒█▒█░░░░▒▓▀ ─────▀▄▄▀▀▀▄▄▀░█░░░░▒▒▓─ ───────────█▒░░█░░░▒▒▓▀─ ────────────█▒░░█▒▒▒▒▓── ─────────────▀▄▄▄▀▄▄▀───", 1, null), 290.89f
				),
				KeyValuePair.Create(Tuple.Create<string, string, string, int, Dictionary<string, string>>(
					"Chat nuke ANELE (mod only)", "TETYYS", "﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽﷽", 1, null), 1910.89f
				),
				KeyValuePair.Create(Tuple.Create<string, string, string, int, Dictionary<string, string>>(
					"Surrogate char wrap test 1", "TETYYS", "WRAPWRAPWRAPWRAPWRAPWRAPWRべWRAPWRAPWRAPWRAPWRAPWRAPWAPWRAPべWRAPWRAPWRAPWRAPWRAPWRAPWRAPWRAPWRAPWRAPべべべべべべべべべべべべべべべべべべべべべべべべべべべべべべべべべべべべべべべべべべべべべべべ", 1, null), 150.89f
				),
				KeyValuePair.Create(Tuple.Create<string, string, string, int, Dictionary<string, string>>(
					"Surrogate char wrap test 2", "TETYYS", "WRAPWRAPWRAPWRAPWRARAPWRべWRAPWRAPRP WRAPWRAPWRAPWRAPWRAPWRAPWRAPWRAPWRAPWRAP WRAPWRAPWRAPWRAPWRAPWRAPWRAPWRAPWRAPWRAP", 1, null), 130.89f
				),
				KeyValuePair.Create(Tuple.Create<string, string, string, int, Dictionary<string, string>>(
					"⭕ NaM", "TETYYS", "𒐫𒐫𒐫 NaM 𒐫𒐫𒐫 NaM 𒐫𒐫𒐫 NaM 𒐫𒐫𒐫 NaM 𒐫𒐫𒐫 NaM 𒐫𒐫𒐫 NaM 𒐫𒐫𒐫 NaM 𒐫𒐫𒐫 NaM 𒐫𒐫𒐫 NaM 𒐫𒐫𒐫 NaM 𒐫𒐫𒐫 NaM 𒐫𒐫𒐫 NaM 𒐫𒐫𒐫 NaM 𒐫𒐫𒐫 NaM 𒐫𒐫𒐫 NaM 𒐫𒐫𒐫 NaM 𒐫𒐫𒐫 NaM 𒐫𒐫𒐫 NaM 𒐫𒐫𒐫 NaM 𒐫𒐫𒐫 NaM ", 1, null), 610f
				),
				KeyValuePair.Create(Tuple.Create<string, string, string, int, Dictionary<string, string>>(
					"Traffic jam", "TETYYS", "🏍🚑 NaM 🚜🚓🚛🚕 NaM 🚚🚗🏎🚜🚓🏍 NaM 🚕🚜🚕🚛🚕🚚🚗 SORRY FOR TRAFFIC NAM 🚕🚜🚕🚓🚛🏎🚑🚒 NaM 🚓🏍🚓🚜 NaM 🏎🏎🚜 NaM 🏎🚜🚓🚜 NaM 🚑🚑 NaM 🚗🚗🚚 NaM 🚗🏎🏎🚚🚛 NaM 🚓🚜🚕🚜🚙🏍 NaM 🚙🏍🚌🚲 NaM 🚌🚐🚌🚒 NaM 🚎🚒🚙🚕🚕🚑🏍🚓🚜🚛 NaM 🚚🚚🚗🚗🚜🚓 NaM 🚑🚒🚑🚲🚒🚲 NaM 🚎🏍🚌🚜🚙🚛🚕🚚 NaM 🚕🚚🚗🏎🏍🚓🏍🚓🚓🚓🏍🚒 NaM 🚒🚕🚕🚚🚚🏎🚜 NaM 🚓🚓🚓🏍🚑🚑🚒🚌🏍🚜", 1, null), 350f
				),
				KeyValuePair.Create(Tuple.Create<string, string, string, int, Dictionary<string, string>>(
					"Smoking pepe", "TETYYS", "─────────▒▒▒▒──▒▒▒▒───────── ───────▒▒▒▒▒▒▒▒▒▒▒▒▒▒─────── ─────▒▒▒▒██░░████░░▒▒─────── ─────▒▒▒▒▒▒▒▒▒▒▒▒▒▒───────── ───▒▒▒▒▒▒▒▒░░░░░░░░(_̅_̅_̅_̅_̅_̅_̅_̅_̅() ด้็็็็็้็็็็็้็็็็็้็็็็็้็็็็็้็็็็็้็็็็็้็็็็็ ───▒▒▒▒▒▒▒▒▒▒▒▒▒▒─────────── ───░░░░░░░░░░░░░░─────────── ⁭", 1, null), 170.89f
				),
				KeyValuePair.Create(Tuple.Create<string, string, string, int, Dictionary<string, string>>(
					"pepe bout to get lung cancer", "TETYYS", "pepeL 🚬 ส์ํ์ั์ํ์๋์ํ์ั์ํ์่์ํ์ั์ํ์๋์ํ์ั์ํ์็์ํ์ั์ํ์๋์ํ์ั์ํ์่์ํ์ั์ํ์๋์ํ์ั์ํ์็์ํ์ั์ํ์๋์ํ์ั์ํ์่์ํ์ั์ํ์๋์ํ์ั์ํ์็์ํ์ั์ํ์๋์ํ์ั์ํ์่์ํ์ั์ํ์๋์ํ์ั์ํ์็์ํ์ั์ํ์๋์ํ์ั์ํ์่์ํ์ั์ํ์๋์ํ์ั์ํ์็์ํ์ั์ํ์๋์ํ์ั์ํ์่์ํ์ั์", 1, null), 30.89f
				),
				KeyValuePair.Create(Tuple.Create<string, string, string, int, Dictionary<string, string>>(
					"Dick knot", "TETYYS", "░░░░█───────█░░░▀░░░█─── ░░░░▓█──────▀▄▄▄█▄▄▄▀─── ░░░░▒░█─────▄▄█▄▄▄█▄▄─── ░░░░░░░▀─▄▀▀░░░░░░░░░▀▄─ ░░░░░░░░█░░░▄▄▀█▀▀▀▄░░░█ ▒▒▒░░░░█░░░█──▄█░░░░█░░█ ▓▓▓▓▒░░█░░░█▀▀░░█░░░█▄▀─ █████░░▄█▀▀░░░░▄█░░░█─── █████▒▒░░░▒░▄▀▀░░░░▄▀─── ███▓▓▒▒▒▀▀▀█▄░░░▄▄▀───── ▓██▓▒▒▒▒▒▒▒▒▒█▀▀░░░█──── ▓▓█▓▒▒▒▒▒▒▓▒▒█░░░░░█──── ░▒▒▀▀▄▄▄▄█▄▄▀░░░░░░░█───", 1, null), 270.89f
				),
				KeyValuePair.Create(Tuple.Create<string, string, string, int, Dictionary<string, string>>(
					"Triple dick", "TETYYS", "────────────── ▗▄▀▀▀▀▖────▄▞▀▀▀▚───── █▓▒▒░░▐────█▓▒▒░░▐───── █▙▀▚▄▞───▗▓▀▄▃▄▀────── █▓▒░▐────▟▓▒▒░░▎───▄▄▀▀▀▀▖ █▓▒░░▎─▟█▓░▒░▞──▐▓▒▒▒░░▐ ▐▓▒░░▚▐█▓▒░▒░▎▎──▟▓▒▀▚▃▞ ▐█▓▒░░▒█▒▒░▒░▐▗█▓▒░░░▞ ▕█▓▓▒░▒▓▒▒░░▟▓▓▒░░░▛ ▟██▓▒▒▒▒▀▚▟█▓▒░░░░▐ █▓▓▓▒░▒▒░░▐█▓▒▒▒░░░░▚ ▐█▓▒░▒▒▒░░▐▓▒▒▒▒░░░░▐ ░░▀▓▓▒░░░░▞▓▓▒░░░░░▞░", 1, null), 270.89f
				),
				KeyValuePair.Create(Tuple.Create<string, string, string, int, Dictionary<string, string>>(
					"Original dick (different phrase)", "TETYYS", "░░░░█──────────────█──▀─ ░░░░▓█───────▄▄▀▀█────── ░░░░▒░█────▄█▒░░▄░█───── ░░░░░░░▀▄─▄▀▒▀▀▀▄▄▀───── ░░░░░░░░░█▒░░░░▄▀──SUCK─ ▒▒▒░░░░▄▀▒░░░░▄▀──EVERY─ ▓▓▓▓▒░█▒░░░░░█▄──DROP─── █████▀▒░░░░░█░▀▄───OF─── █████▒▒░░░▒█░░░▀▄──MY─── ███▓▓▒▒▒▀▀▀█▄░░░░█──CUM─ ▓██▓▒▒▒▒▒▒▒▒▒█░░░░█───── ▓▓█▓▒▒▒▒▒▒▓▒▒█░░░░░█──── ░▒▒▀▀▄▄▄▄█▄▄▀░░░░░░░█───", 1, null), 270.89f
				),
				KeyValuePair.Create(Tuple.Create<string, string, string, int, Dictionary<string, string>>(
					"Upside down dick", "TETYYS", "─────────────────── ░░░░░░░░░░░░▄███████▄▄▒▒░ ░████▄▄░░░░█▒▒▓▒▒▒▒▒▒▓█▓▓ █░█░▒███▄▄█▒▒▒▒▒▒▒▒▒▓██▓ █▄▄░░█░░░▒▒██▄▄▄▒▒▒▓▓███ ───██▄▄░░░░░░░░░░▒▒▒▒▒██ ──▄█░░░░░░▄▒▒▒▒▒▄▒▒▒▒▒▒█ ▄██▄▄▄▒▄██─█░░░░█▒█░░░▒█ █░█░░▒██─▄█░░░░▒█─█░░░▒█ █▄▄▄▄██─▄█░░░░▒█──█░░░▒█ ─────▄██▄▄▄▒▄█───▄██▄▄▄█ ────█░█░░▒██─────█░█░░▒█ ────█▄▄▄▄██──────█▄▄▄▄██ ─────────────────", 1, null), 290.89f
				),
				KeyValuePair.Create(Tuple.Create<string, string, string, int, Dictionary<string, string>>(
					"Lil' dick", "TETYYS", "░░░░█─────────────█──▀── ░░░░▓█───────▄▄▀▀█────── ░░░░▒░█────▄█▒░░▄░█───── ░░░░░░░▀▄─▄▀▒▀▀▀▄▄▀─HEY─ ░░░░░░░░░█▒░░░░▄▀─LIL─── █████▒▒░░█░░░▀▄──FELLA── ███▓▓▒▒▒▀▀▀█▄░░░░█────── ▓██▓▒▒▒▒▒▒▒▒▒█░░░░█───── ▓▓█▓▒▒▒▒▒▒▓▒▒█░░░░░█──── ░▒▒▀▀▄▄▄▄█▄▄▀░░░░░░░█─", 1, null), 210.89f
				),
				KeyValuePair.Create(Tuple.Create<string, string, string, int, Dictionary<string, string>>(
					"TriHard", "TETYYS", "⣿⣿⣿⣿⣿⣿⣿⣿⣿⣿⡿⠿⣿⣿⣿⣿⣿⣿⣿⣿⣿⣿⣿⣿⣿ ⣿⣿⣿⣿⣿⠋⠉⠙⠻⠏⠀⠀⠀⠈⠀⠉⠉⠙⠛⢿⣿⣿⣿⣿⣿ ⣿⣿⣿⣿⠟⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀⠙⠻⣿⣿⣿ ⣿⣿⣏⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀⠹⣿⣿ ⠿⠛⠉⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀⠘⢿ ⣿⡆⠀⠀⠀⠀⠀⠀⠀⣀⡀⠀⢀⣀⡀⡀⠀⠀⠀⠀⠀⠀⠀⠀⠸ ⡿⠁⠀⠀⠀⠀⠀⠀⠀⢹⣿⣿⣿⣿⣿⣿⣶⣶⣤⠀⠀⠀⠀⠀⠀ ⣷⣦⣤⣄⢀⡀⠀⠀⠀⣰⣿⣿⣿⣿⣿⣿⣿⣿⡏⠀⠀⠀⠀⣠⣾ ⣿⣿⣿⣿⣿⡿⠀⢀⣿⣏ TriHard ⣿⣿⣿ TriHard ⣿⣿ ⣿⣿⣿⣿⣿⡇⠀⢸⣿⣿⣿⣿⣿⣿⣿⡧⢾⣦⣤⢀⣿⣿⣶⣿⣿ ⣿⣿⣿⣿⣿⣧⠀⠈⢛⠿⠿⣿⣿⣿⡿⢷⣼⣿⡿⣺⣿⣿⣿⣿⣿ ⣿⣿⣿⣿⣿⣿⣇⠀⠁⣶⣷⣶⣿⣿⣷⡄⡠⢀⣼⣿⣿⣿⣿⣿⣿ ⣿⣿⣿⣿⣿⣿⣿⣦⠀⢻⣿⣿⣿⣿⡿⠀⣴⣿⣿⣿⣿⣿⣿⣿⣿ ⣿⣿⣿⣿⣿⣿⣿⣿⣷⡘⠿⣿⣦⡤⢂⣾⣿⣿⣿⣿⣿⣿⣿⣿⣿", 1, null), 310.89f
				),
				KeyValuePair.Create(Tuple.Create<string, string, string, int, Dictionary<string, string>>(
					"gachiGASM", "TETYYS", "⣶⣿⣾⣾⣿⣿⣶⣆ ⠀⠀⠀⠀⣠⣴⣿⣿⣿⣿⠿⠛⠛⠿⣿⣿⡍⡁ ⠀⠀⡀⢶⣿⣿⡿⠛⠁⠀⠀⠀⠀⣀⡆⠉⢻⣮⣲⣤ ⠀⣀⣿⣿⣿⠏⠀⠀⠀⣠⣴⣶⣿⣿⣿⡷⢿⣿⣿⣿⣷⡄ ⢀⣿⣿⣿⣿⡄⠀⠀⠀⠟⣋⣭⣯⣿⡟⠀⠈⠻⡿⠿⣿⣧⡀ ⢸⣿⣿⣿⣿⠇⠀⠀⠠⠘⠉⠛⠋⠉⡄⠀⣠⣴⣦⣶⣿⣿⣇ ⢾⣿⣿⣿⣿⠀⠀⠀⠀⠀⠀⠀⠀⢸⣿⠞⠛⠿⢻⣻⣿⣿⣏ ⢸⣿⣿⣿⣿⡇⠀⠀⠀⠀⠀⠀⠀⣼⡏⢀⣴⣿⣿⠿⢿⣿⡿⣧ ⠘⣿⣿⣿⣿⣿⣦⠀⠀⠀⠀⠀⠀⢻⣷⣿⡋⣠⣤⣤⣤⣹⣷⣾⡀ ⠀⠸⢧⠖⠀⠈⣿⠁⠀⠀⠀⠀⠀⠸⣿⠙⠛⠛⠛⣿⣿⣿⣿⣿⡇ ⠀⠀⢸⢠⡾⠀⠀⠀⠀⠀⠀⠀⠀⠀⠋⠀⠀⠀⢰⣾⣿⣿⣿⣿⣷ ⠀⠀⠈⠀⠁⢀⣠⣄⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀⠈⢻⣿⣿⢿⣿⣇ ⠀⠀⠀⠀⠙⣆⠉⠉⠀⠀⠀⠀⠀⠀⠀⠀⠀⠀⣀⣼⣿⣧⣴⣿⣿ ⠀⠀⠀⠀⠀⠘⣆⠀⠀⠀⠀⠀⠀⢀⣀⣀⣠⣾⣿⣿⣿⣿⣿⣿⡏ ⠀⠀⠀⠀⠀⠀⠘⠆⠀⠀⠀⠀⠀⠸⣿⣿⣿⣿⣿⣿⣿⣿⣿⣿⡇", 1, null), 310.89f
				),
				KeyValuePair.Create(Tuple.Create<string, string, string, int, Dictionary<string, string>>(
					"SUCTION", "TETYYS", "SUCTION YES SIR ░░█───────────▄▀▀▄▀▀▄x ░░░░█─────────█░░░▀░░░█x ░░░░▓█────────▀▄▄▄█▄▄▄▀x ░░░░▒░█────────█░░░░░█xx ░░░░░░░▀▄──────█░░░░░█xx ░░░░░░░░░░▄▄▄▄▀░░░░░░█x ▒▒▒░░░░▄▀░░░░░░░░░░░▀xxx ▓▓▓▓▒░▀▒░░░░░▄▄▄▄▄▀xxxxx █████▀▒░░░░▄▀░▀▄xxxxxxxxx █████▒▒░░░▒█░░░▀▄xxxxxxxx ███▓▓▒▒▒▀▀▀█▄░░░░█xxxxxxx ▓██▓▒▒▒▒▒▒▒▒▒█░░░░█xxxxx ▓▓█▓▒▒▒▒▒▒▓▒▒█░░░░░█xxxx ░▒▒▀▀▄▄▄▄█▄▄▀░░░░░░░xxxx", 1, null), 310.89f
				),
				KeyValuePair.Create(Tuple.Create<string, string, string, int, Dictionary<string, string>>(
					"don't spam chat :(", "TETYYS", "😡😡😡⚪⚪⚪😡😡😡😡😡😡😡😡😡😡😡⚪⚪⚪⚪⚪😡😡😡😡😡😡😡😡😡⚪⚪⚪⚫⚪⚪⚪😡😡😡😡😡😡⚪⚪⚪⚫⚫⚫⚪⚪⚪😡😡😡😡😡⚪⚪⚪⚫⚫⚫⚪⚪⚪⚪⚪😡😡😡⚪⚪⚪⚫⚫⚫⚪⚪⚪⚫⚪⚪⚪🔴⚪⚪⚪⚫⚫⚫⚪⚪⚪⚫⚫⚫⚪⚪⚪⚪⚪⚪⚪⚫⚫⚫⚪⚫⚫⚫⚫⚫⚪⚪⚪⚫⚪⚪⚪⚫⚫⚫⚫⚫⚪⚫⚫⚫⚪⚫⚫⚫⚪⚪⚪⚫⚫⚫⚪⚪⚪⚫⚫⚫⚪⚫⚫⚫⚪⚫⚫⚫⚫⚫⚪⚪⚪⚫⚪⚪⚪⚫⚫⚫⚫⚫⚪⚫⚫⚫⚪⚪⚪⚪⚪⚪⚪⚫⚫⚫⚪⚪⚪⚫⚫⚫⚪⚪⚪🔴⚪⚪⚪⚫⚪⚪⚪⚫⚫⚫⚪⚪⚪😡😡😡⚪⚪⚪⚪⚪⚫⚫⚫⚪⚪⚪😡😡😡😡😡⚪⚪⚪⚫⚫⚫⚪⚪⚪😡😡😡😡😡😡😡⚪⚪⚪⚫⚪⚪⚪😡😡😡😡😡😡😡😡😡⚪⚪⚪⚪⚪😡😡😡😡😡😡😡😡😡😡😡⚪⚪⚪😡😡😡😡😡😡 DONT SPAM THE CHAT >( >( >( >( >(", 1, new Dictionary<string, string>() { { ">(", "https://static-cdn.jtvnw.net/emoticons/v1/4/1.0" } }), 390.89f
				),
				KeyValuePair.Create(Tuple.Create<string, string, string, int, Dictionary<string, string>>(
					"OEAOEOAEOAEO", "TETYYS", "LOØÖÓÒÔǑŐŎȮỌƟƠỎŌÕǪȌOŒOØÖÓÒÔǑŐŎȮỌƟƠỎŌÕǪȌOŒOØÖÓÒÔǑŐŎȮỌƟƠỎŌÕǪȌOŒOØÖÓÒÔǑŐŎȮỌƟƠỎŌÕǪȌOŒOØÖÓÒÔǑŐŎȮỌƟƠỎŌÕǪȌOŒOØÖÓÒÔǑŐŎȮỌƟƠỎŌÕǪȌOŒOØÖØÖÓÒÔǑŐŎȮỌƟƠỎŌÕǪȌOŒOØÖÓÒÔǑŐŎȮỌƟƠỎŌÕǪȌOŒOØÖÓÒÔǑŐŎȮỌƟƠỎŌÕǪȌOŒOØÖÓÒÔǑŐŎȮỌƟƠỎŌÕǪȌOŒOØÖÓÒÔǑŐŎȮỌƟƠỎŌÕǪȌOŒOØÖÓÒÔǑŐØÖØÖÓÒÔǑŐŎȮỌƟƠỎŌÕǪȌOŒOØÖÓÒÔǑŐŎȮỌƟƠỎŌÕǪȌOŒOØÖÓÒÔǑŐŎȮỌƟƠỎŌÕǪȌOŒOØÖÓÒÔǑŐŎȮỌƟƠỎŌÕǪȌOŒOØÖÓÒÔǑŐŎȮỌƟƠỎŌÕǪȌOŒOØÖÓÒÔǑŐŎȮỌƟƠỎŌÕǪȌOŒOØÖÓÒÔǑŐŎȮỌƟƠỎŌÕǪȌOŒL", 1, null), 290.89f
				),
				KeyValuePair.Create(Tuple.Create<string, string, string, int, Dictionary<string, string>>(
					"Spooky skeleton WutFace (issue 1)", "TETYYS", "U HAVE BEEN BANGED BY THE SPOOKY BOOTY BANGER░░░░░░░▄▐░░░░▒▒▒░░░ ▒░░░░░░▄▄▄░░▄██▄░░░ ░░░░░░▐▀█▀▌░░░░▀█▄░ ░░░░░░▐█▄█▌░░░░░░▀█▄ ░░░░░░░▀▄▀░░░▄▄▄▄▄▀▀ ░░░░░▄▄▄██▀▀▀▀░░░░░ ░░░░█▀▄▄▄█░▀▀░░░░░░ ░░░░▌░▄▄▄▐▌▀▀▀░░░░░ ░▄░▐░░░▄▄░█░▀▀░░░░░ ░▀█▌░░░▄░▀█▀░▀░░░░░ ░░░░░░░░▄▄▐▌▄▄░░░░░ ░HIV░░░░▀███▀█░▄░░░ ░..AIDS░░▐▌▀▄▀▄▀▐▄░░░ ░FEELS░░▐▀░▐░▌░░▐▌░░ ░░BAD░░░█░░▐░▌░░░█░░ ░░MAN░░▐▌░░░▀░░░░░█░", 1, null), 370.89f
				),
				KeyValuePair.Create(Tuple.Create<string, string, string, int, Dictionary<string, string>>(
					"bUrself", "TETYYS", "░A░n░d░ ░y░o░u░ ░t░h░o░u░g░h░t░ ░t░h░e░r░e░ ░w░o░u░l░d░n░t░ ░b░e░ ░a░n░y░ ░b░e░e░s░ ░i░n░ ░t░h░i░s░ ░c░h░a░t░ bUrself ", 1, null), 70.89f
				),
				KeyValuePair.Create(Tuple.Create<string, string, string, int, Dictionary<string, string>>(
					"KKona 💣💣", "TETYYS", "NaM 👍 💣 💣 🗾 NaM 👍 💣 💣 🗾 NaM 👍 💣 💣 🗾 NaM 👍 💣 💣 🗾 NaM 👍 💣 💣 🗾 NaM 👍 💣 💣 🗾 NaM 👍 💣 💣 🗾 NaM 👍 💣 💣 🗾 NaM 👍 💣 💣 🗾 NaM 👍 💣 💣 🗾 NaM 👍 💣 💣 🗾 NaM 👍 💣 💣 🗾 NaM 👍 💣 💣 🗾 NaM 👍 💣 💣 🗾 NaM 👍 💣 💣 🗾 NaM 👍 💣 💣 🗾 NaM 👍 💣 💣 🗾 NaM 👍 💣 💣 🗾 ⁭ NaM 👍 💣 💣 🗾 NaM 👍 💣 💣 🗾 NaM 👍 💣 💣 🗾 NaM 👍 💣 💣 🗾 NaM 👍 💣 💣 🗾 NaM 👍 💣 💣 🗾 NaM 👍 💣 💣 🗾 NaM 👍 💣 💣 🗾 NaM 👍 💣 💣 🗾 NaM 👍 💣 💣 🗾 NaM 👍 💣 💣 🗾 NaM 👍 💣 💣 🗾 NaM 👍 💣 💣 🗾 NaM 👍 💣 💣 🗾 NaM 👍 💣 💣 🗾 NaM 👍 💣 💣 🗾 NaM 👍 💣 💣 🗾 NaM 👍 💣 💣 🗾 ", 1, null), 490f
				),
				KeyValuePair.Create(Tuple.Create<string, string, string, int, Dictionary<string, string>>(
					"just someone smoking", "TETYYS", "░░░░░░░░░░░░░░░░░░░░ ░░░░░▄▀░░░░░░░░░░░░░ ░░░░█░▄██░░░░░░██▄░░ ░░░█▄▀▄▄░█░░░░█░▄░█▄ ░░▄▀░▀▀▀▀░░░░░░▀▀▀░░ ▄▀░░░░░░░░░░░░▄░░░░░ █░░░░█░░░░░░▄▄▀░░░░░ █░▄▀▄░▀▀▀▀▀▀░░░▄▀▀▄░ ▀▄▀░░▀▀▄▄▄▄▄(_̅_̅_̅_̅_̅_̅_̅_̅_̅_̅_̅_̅_̅_̅ () ด้็็็็็้็็็็็้็็็็็้็็็็็้็็็็็้็็็็็้็็็็็้็็็็็ . ░▀▄░░░░░░░░░░░░░░░░░ ░░█░░░░░░░░░░░░░░░░░", 1, null), 230.89f
				),
				KeyValuePair.Create(Tuple.Create<string, string, string, int, Dictionary<string, string>>(
					"CCCP", "TETYYS", "░░░░░░░░░░▀▀▀██████▄▄▄░░░░░░░░░░ ░░░░░░░░░░░░░░░░░▀▀▀████▄░░░░░░░ ░░░░░░░░░░▄███████▀░░░▀███▄░░░░░ ░░░░░░░░▄███████▀░░░░░░░▀███▄░░░ ░░░░░░▄████████░░░░░░░░░░░███▄░░ ░░░░░██████████▄░░░░░░░░░░░███▌░ ░░░░░▀█████▀░▀███▄░░░░░░░░░▐███░ ░░░░░░░▀█▀░░░░░▀███▄░░░░░░░▐███░ ░░░░░░░░░░░░░░░░░▀███▄░░░░░███▌░ ░░░░▄██▄░░░░░░░░░░░▀███▄░░▐███░░ ░░▄██████▄░░░░░░░░░░░▀███▄███░░░ ░█████▀▀████▄▄░░░░░░░░▄█████░░░░ ░████▀░░░▀▀█████▄▄▄▄█████████▄░░ ░░▀▀░░░░░░░░░▀▀██████▀▀░░░▀▀██░░", 1, null), 310.89f
				),
				KeyValuePair.Create(Tuple.Create<string, string, string, int, Dictionary<string, string>>(
					"diddly doo", "TETYYS", "▄▄▄▄▄▄▄▄▄ ░▄███████▀▀▀▀▀▀███████▄ ░▐████▀▒DIDDLY▒▒▒▒▀██████▄ ░███▀▒▒▒▒SPAMLY▒▒▒▒▒▀█████ ░▐██▒▒▒▒▒▒DOODLY▒▒▒▒▒▒████▌ ░▐█▌▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒▒████▌ ░░█▒▄▀▀▀▀▀▄▒▒▄▀▀▀▀▀▄▒▐███▌ ░░░▐░░░▄▄░░▌▐░░░▄▄░░▌▐███▌ ░▄▀▌░░░▀▀░░▌▐░░░▀▀░░▌▒▀▒█▌ ░▌▒▀▄░░░░▄▀▒▒▀▄░░░▄▀▒▒▄▀▒▌ ░▀▄▐▒▀▀▀▀▒▒▒▒▒▒▀▀▀▒▒▒▒▒▒█ ░░░▀▌▒▄██▄▄▄▄████▄▒▒▒▒█▀ ░░░░▄██████████████▒▒▐▌ ░░░▀███▀▀████▀█████▀▒▌ ░░░░░▌▒▒▒▄▒▒▒▄▒▒▒▒▒▒▐ ░░░░░▌▒▒▒▒▀▀▀▒▒▒▒▒▒▒▐", 1, null), 330.89f
				),
				KeyValuePair.Create(Tuple.Create<string, string, string, int, Dictionary<string, string>>(
					"literally hitler", "TETYYS", "░░░░░░░░░░░░░░░░░░░░░░░░░░░ ░░░░▓▓▀▀██████▓▄▒▒░░░░░░░░░ ░░░▀░░░░░░▀▀▀████▄▒░░░░░░░░ ░░▌░░░░░░░░░░░▀███▓▒░░░░░░░ ░▌░░░░░▄▄▄░░░░░░▐█▓▒░░░░░░░ ░▄▓▀█▌░▀██▀▒▄░░░▐▓▓▓▒░░░░░░ ░█▌░░░░░▀▒░░░▀░░░▐▓▒▒░░░░░░ ░▌▀▒▄▄░░░░░░░░░░░░░▄▒░░░░░░ ░▒▄█████▌▒▒█░█▀▀░░░▒▌▒░░░░░ ░░▓█████▄▒░▀▀█▀█░░░▐░░░░░░░ ░░▒▀▓▒▒▒░░░▀▀▀░▀▒▒░▒▒▒▄░░░░ ░░▓▒▒▒░░░░░░▒▒▒▒▒░▓░░░░░░░░ ░░████▄▄▄▄▓▓▓▒▒░░▐░░░░░░░░░ ░░░▀██████▓▒▒▒▒▒░▐░░░░░░░░░", 1, null), 310.89f
				),
				KeyValuePair.Create(Tuple.Create<string, string, string, int, Dictionary<string, string>>(
					"shit on mods", "TETYYS", "░░░░░░░░░▓▓▓▓▀█░░░░░░░░░░░░░ ░░░░░░▄▀▓▓▄██████▄░░░░░░░░░░ ░░░░░▄█▄█▀░░▄░▄░█▀░░░░░░░░░░ ░░░░▄▀░██▄░░▀░▀░▀▄░░░░░░░░░░ ░░░░▀▄░░▀░▄█▄▄░░▄█▄░░░░░░░░░ ░░░░░░▀█▄▄░░▀▀▀█▀░░░░░░░░░░░ ░░░░░░█░░░░░░░░▄▀▀░▐░░░░░░░░ ░░░░▄▀░░░░░░░░▐░▄▄▀░░░░░░░░░ ░░▄▀░░░▐░░░░░█▄▀░▐░░░░░░░░░░ ░░█░░░▐░░░░░░░░▄░█░░░░░░░░░░ ░░░█▄░░▀▄░░░░▄▀▐░█░░░░░░░░░░ ░░░█▐▀▀▀░▀▀▀▀░░▐░█░░░░░░░░░░ ░░▐█▐▄░░▀░░░░░░▐░█▄▄░░░░░░░░ ░░░▀▀░MODS░░░░▐▄▄▄▀░░░░░░░░░", 1, null), 310.89f
				),
				KeyValuePair.Create(Tuple.Create<string, string, string, int, Dictionary<string, string>>(
					"Kappa driving", "TETYYS", "──────▄▌▐▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▀▌ ───▄▄ █ Kappa WATCH OUT I'M DRIVING ███████▌█▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▄▌ ▀(@)▀▀▀▀▀▀▀(@)(@)▀▀▀▀▀▀▀▀▀▀▀▀▀(@)▀", 1, null), 110.89f
				),
				KeyValuePair.Create(Tuple.Create<string, string, string, int, Dictionary<string, string>>(
					"toucan pelican", "TETYYS", "░░░░░░░░▄▄▄▀▀▀▄▄███▄░░░░░░░░░░░░░░ ░░░░░▄▀▀░░░░░░░▐░▀██▌░░░░░░░░░░░░░ ░░░▄▀░░░░▄▄███░▌▀▀░▀█░░░░░░░░░░░░░ ░░▄█░░▄▀▀▒▒▒▒▒▄▐░░░░█▌░░░░░░░░░░░░ ░▐█▀▄▀▄▄▄▄▀▀▀▀▌░░░░░▐█▄░░░░░░░░░░░ ░▌▄▄▀▀░░░░░░░░▌░░░░▄███████▄░░░░░░ ░░░░░░░░░░░░░▐░░░░▐███████████▄░░░ ░░░░░le░░░░░░░▐░░░░▐█████████████▄ ░░░░toucan░░░░░░▀▄░░░▐█████████████▄ ░░░░░░has░░░░░░░░▀▄▄███████████████ ░░░░░arrived░░░░░░░░░░░░█▀██████░░", 1, null), 250.89f
				),
				KeyValuePair.Create(Tuple.Create<string, string, string, int, Dictionary<string, string>>(
					"literally random bytes casted to unicode", "TETYYS", "�ጶ͂ꏄ䔈罏㦇㊙윋䢃㤪麑瑛ᾲꯥ砡㴳镤曨쯄გ솯˜ꁿ㏋⅄굖ዊ虜雪㨛ケ㸘ྠ䙶ዊ유뤐쑔ঈ說朗좴⺚袜雬峋㌩鷼⛠싚狼ꍴ똿畭䶘ᇶ�᱙켣暑༉ิƿ魧䜫깍俠ﵰ좪닃깎섕ᬄ偾恭ꁚ袬䰣냨上넡蕂ᰇ跌煯퀌씧鑵췍惞⥖઩ᑳ엧ል샄㰕쎜樴ﺺ鰈편�騥窆쟁䥅炛辨ᥠꃸ㈔왿䜶輆ᘫ뱮�蟘켃蜓춬푯㷨稅씊얏팜꛺㲧ᗊᦗ鿆Ⓖ욟䲀ࠩ氈텕䊇쁫褭꫑썍അ￷ể톐抦ꅞ븱↱䣟鲚凬ꥯ戠౪ᝡ㾋ᗠ䜆줩ㆩ宦틳둬ꨞ퍇铵㨟驸䠖儳덦왌ᑌ汞Ⴢ�⦌ꕒ䠵썤︤⵾ᱛⳅ㈳Ъྖ䓑꬇Ꮒ踌멞畨䔓ﷶ綰䢃ˑ긿⨎訛䢊ꇸ惧�ꇙ큒⛦�⎢⁧ɵ⮕拮옘ר࿮횮ᰉㇹ귳认㰷൶ᶐ隫澐睧좻�ᜟ隃拭鴭탵穼�羧粵楥Ь돿浊필ી笇缸�獡吿謃屻逢虧븿暹㢗혗롥פ⡭鮀쥫僎ㆽ῰膙秒⥍橯餝뙍櫉뿦지㫛攇貵ꛤ㞫㺐茪⻻⌽ᙄ⛠ድ䆣▄ꡔﳎ俓춍椃幯髚樝룫⭃᳕헩剫곛뵓⣲衷ឱ屦嵰嚶蹰嚍౛焢ㆮ蘋牐식�锝솹䢚ໂ홁�Ἥ݃┙嬴嶝淯蜏猞粄禨�㉽휉뵃ꊡ瓧鹜冸鸄켹˹霑폁൰憣軒쪉ꒄﻺ䮏", 1, null), 310.89f
				),
				KeyValuePair.Create(Tuple.Create<string, string, string, int, Dictionary<string, string>>(
					"NaM but very rich", "TETYYS", "$$$$$$$$$$$NaM$$$$$$$$$$$$", 1, null), 40f
				),
				KeyValuePair.Create(Tuple.Create<string, string, string, int, Dictionary<string, string>>(
					"YooHoo but very rich (AddStrippedEmoteCharsToCalc enabled)", "TETYYS", "$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$YooHoo$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$", 1, null), 94.89f
				),
				KeyValuePair.Create(Tuple.Create<string, string, string, int, Dictionary<string, string>>(
					"Richest NaM (AddStrippedEmoteCharsToCalc enabled)", "TETYYS", "$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$NaM$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$", 1, null), 100.89f
				),
				KeyValuePair.Create(Tuple.Create<string, string, string, int, Dictionary<string, string>>(
					"Richest YooHoo (AddStrippedEmoteCharsToCalc and ApplyBTTVStripToFFZ enabled)", "TETYYS", "$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$YooHoo$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$$", 1, null), 94.89f
				),
				KeyValuePair.Create(Tuple.Create<string, string, string, int, Dictionary<string, string>>(
					"Article 13", "TETYYS", "𝐃𝐮𝐞 𝐭𝐨 𝐀𝐫𝐭𝐢𝐜𝐥𝐞 𝟏𝟑 𝐨𝐟 𝐭𝐡𝐞 𝐄𝐮𝐫𝐨𝐩𝐞𝐚𝐧 𝐔𝐧𝐢𝐨𝐧, 𝐭𝐡𝐢𝐬 𝐂𝐨𝐦𝐦𝐞𝐧𝐭 𝐢𝐬 𝐧𝐨𝐭 𝐚𝐯𝐚𝐢𝐥𝐚𝐛𝐥𝐞 𝐟𝐨𝐫 𝐯𝐢𝐞𝐰 𝐢𝐧 𝐲𝐨𝐮𝐫 𝐚𝐫𝐞𝐚. 𝐀𝐭𝐭𝐞𝐦𝐩𝐭𝐢𝐧𝐠 𝐭𝐨 𝐛𝐲𝐩𝐚𝐬𝐬 𝐭𝐡𝐢𝐬 𝐜𝐚𝐧 𝐫𝐞𝐬𝐮𝐥𝐭 𝐢𝐧 𝐮𝐩 𝐭𝐨 𝐚𝐭 𝐥𝐞𝐚𝐬𝐭 𝟏𝟎 𝐨𝐫 𝐦𝐨𝐫𝐞 𝐲𝐞𝐚𝐫𝐬 𝐢𝐧 𝐣𝐚𝐢𝐥 𝐚𝐧𝐝 𝐚 𝐟𝐢𝐧𝐞 𝐨𝐟 𝐚𝐭 𝐥𝐞𝐚𝐬𝐭 𝟓𝟎𝟎 𝐄𝐮𝐫𝐨𝐬. 𝐅𝐨𝐫 𝐦𝐨𝐫𝐞 𝐡𝐞𝐥𝐩 𝐩𝐥𝐞𝐚𝐬𝐞 𝐜𝐨𝐧𝐭𝐚𝐜𝐭 𝐲𝐨𝐮𝐫 𝐥𝐨𝐜𝐚𝐥 𝐫𝐞𝐩𝐫𝐞𝐬𝐞𝐧𝐭𝐚𝐭𝐢𝐯𝐞, 𝐨𝐫 𝐬𝐡𝐨𝐰 𝐭𝐡𝐢𝐬 𝐜𝐨𝐦𝐦𝐞𝐧𝐭 𝐭𝐨 𝐭𝐡𝐞 𝐧𝐞𝐚𝐫𝐞𝐬𝐭 𝐨𝐟𝐟𝐢𝐜𝐞𝐫 𝐨𝐟 𝐭𝐡𝐞 𝐥𝐚𝐰.", 1, null), 150.89f
				),
				KeyValuePair.Create(Tuple.Create<string, string, string, int, Dictionary<string, string>>(
					"Chat nuke ANELE (mod only) 2", "TETYYS", "﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽﷽﷽ NaM ﷽ NaM", 1, null), 1900f
				),
				/*KeyValuePair.Create(Tuple.Create<string, string, string, int, Dictionary<string, string>>("Arabic combining characters", "TETYYS", "بابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابابا", 1, null), 110.89f),*/
				KeyValuePair.Create(Tuple.Create<string, string, string, int, Dictionary<string, string>>(
					"7TV wide", "TETYYS", "AlienGathering AlienGathering AlienGathering", 3, null), 50f // actually 51.45f, but of course 7TV is different from FFZ in some way
				),
				KeyValuePair.Create(Tuple.Create<string, string, string, int, Dictionary<string, string>>(
					"7TV regular", "TETYYS", "catKISS catKISS catKISS catKISS catKISS catKISS catKISS catKISS catKISS catKISS catKISS catKISS catKISS catKISS catKISS catKISS", 3, null), 76f // actually 77.36f
				),
				KeyValuePair.Create(Tuple.Create<string, string, string, int, Dictionary<string, string>>(
					"7TV with zero width", "TETYYS", "catKISS Love catKISS Love catKISS Love catKISS Love catKISS Love catKISS Love catKISS Love catKISS Love catKISS Love catKISS Love catKISS Love catKISS Love catKISS Love", 3, null), 54f // actually 54.91f
				),
				KeyValuePair.Create(Tuple.Create<string, string, string, int, Dictionary<string, string>>(
					"7TV with zero width chaos", "TETYYS", "😂 Love a NaN Love Nam Love AlienGathering Love Love aa Love 👨 Love Love 😂 Love a NaN Love Nam Love AlienGathering Love Love aa Love 👨 Love Love 😂 Love a NaN Love Nam Love AlienGathering Love Love aa Love 👨 Love Love", 3, null), 98f // actually 119.81f but ugh
				),
			};

			MessageHeightTwitch.MessageHeightTwitch.FillCharMap("charmap.bin.gz");
			var mht = new MessageHeightTwitch.MessageHeightTwitch("tetyys", "36175310", 99999999, true);

			foreach (var test in tests) {
				var height = mht.CalculateMessageHeight(test.Key.Item3, test.Key.Item2, test.Key.Item2, test.Key.Item4, test.Key.Item5);
                if (Math.Floor(test.Value) != height) {
                    output.WriteLine(test.Key.Item1);
                }
				Assert.Equal(Math.Floor(test.Value), height);
			}
		}
    }
}
