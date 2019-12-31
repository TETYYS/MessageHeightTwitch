#include <stdio.h>
#include "coreruncommon.h"
#include "exports.h"

int main(int argc, char **argv)
{
	int st;
	if ((st = LoadCLRRuntime("/home/user/prog/MessageHeightTwitch/c-interop/a.out", "/home/user/.dotnet/shared/Microsoft.NETCore.App/3.1.0/", "/home/user/prog/MessageHeightTwitch/bin/Debug/netstandard2.1/MessageHeightTwitch.dll")) != 0) {
		printf("failed to load CLR runtime");
		return st;
	}

	if ((st = InitMessageHeightTwitch()) != 0) {
		printf("failed to to init MessageHeightTwitch");
		return st;
	}

	CalculateMessageHeight *c = CreateCalculateMessageHeightDelegate();

	TwitchEmote emotes[3];
	emotes[0].Name = "a";
	emotes[0].Url = "http";
	emotes[1].Name = "pajaDank";
	emotes[1].Url = "https://static-cdn.jtvnw.net/emoticons/v1/129570/1.0";
	emotes[2].Name = "thirdemote";
	emotes[2].Url = "http://test.com/";

	float res = c("🍁😄😊😃☺😉😍😘😚😳😌😌😁😜😝😒😒😏😏😓😔😞😖😖😰😨😣😢😢😭😂😲😱💛👽👿👿😷😪😡😠💙💜💗💚❤💔💓💘💨👎✨💦👌🌟🎶👊✊💢💢❕🔥✌👋💩❔💤👍✋👐☝👯🙆🙆👆👇💪🙅💁🚶👉👈🏃🙇💏💏💃🙌🙌🙏💃💑💆👶👮👮👼👵💇💅👸👸💂👱👦👧👲👲💀👣👳👩👨👷👷💋🍀🌴🌵🌹🌵🌾🌻🐚🌺🍃🍃🍂🐘🐙🐙🌷🌸🐛🐑🐫🐧🐧💐🐔🐔🐎🐤🐳🐟🐦🐒🐵🐍🐠🌀🐺🐗🐮🐰⚡🌙🐹🐷🐻🐭⛄☁🐶🐨🐯☔☔☀🌊🐸🎍🎇🎁🔔🎐💝🎎🎑🎉🎈🎈🎃🎒🎓👻💿📀🎏🎏🎆🎄📷📼🔍🔓🔊💻🎥📺📢🔒🔑📣📱📠📻✂🔨📠☎➿💡📲🏈🏈🏀📩📫🚬⚽⚾⚾📮🛀🛀🎾⛳💊🚽💺💉🎱🏊🏆🎨🎤👾🏄🎯🎧🎧🎺🀄♠♥🎬🎷🎷🎸📝♣♦📖〽👟👘💼👜👡👠👠🎀💄💍🎩👢👕👑💎☕☕👒👔👗🌂🍵🍺🍜🍜🍲🍻🍻🍱🍞🍞🍳🍣🍶🍴🍙🍢🍡🍘🍔🍔🍟🍚🍦🍧🍆🍅🎂🍰🍎🍊🍉🍓🍊🌄🎢🚗🚗🚢🌃🌃🚕🚌🚌🗽🗽✈🚓🚒🎡🎡🚒🚒🚲⛲🌈🏠🏩🏧🏯🏨🏫💒🏰🏰⛺⛪🏣🏥🏬🏭🏭🗼🌇🏦🌆🗻🇫🇷🇪🇸🇮🇹🇷🇺🇩🇪🇺🇸🇨🇳💈⛽🎫🎰🇰🇷🇰🇷🇯🇵🔰🚄🚉🚧🎌🏁⚠🚃🚚🚥♨1⃣1⃣3⃣4⃣5⃣6⃣7⃣⬅🈵🈵🈵🈁🆕🔝🆙🆒🈹🉐🈳🈵🈺🈯🈯🈶🈶🈶🈚🈷🈸🈸🈂🚻🚹🚺🚼🚭♿♿✳🔞㊗㊙🚾🚇✴💟📴💹💹♈♉♊♋♋♌♎♎🔯⛎♒♒♑♒♓⛎⛎⛎♎♍♌♊♑🅰🅱🆎🆎🔲🔲🔴🔳🔳🕛🕐🕑🕒🕓🕔🕕❌🕚🕘🕗🕖©®™⁭", "tetyys", "TETYYS", 1, emotes, 3);
	printf("res: %f", res);
}
