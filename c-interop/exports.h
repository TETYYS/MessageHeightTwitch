#include <stdbool.h>

typedef struct _TwitchEmote
{
	const char *Name;
	const char *Url;
} TwitchEmote;

typedef float(CalculateMessageHeight)(const char *Channel, const char *Input, const char *Username, const char *DisplayName, int NumberOfBadges, TwitchEmote *TwitchEmotes, int TwitchEmotesLen);
typedef int(FxInitCharMap)(const char *CharMapPath);
typedef int(FxInitChannel)(const char *Channel, const char* ChannelId, int TimeoutMs);
typedef int(FxInitChannel2)(const char *Channel, const char* ChannelId, int TimeoutMs, bool Enable7TVEmotes);

#ifdef __cplusplus
extern "C" {
#endif
	extern int UnloadCLRRuntime();
	extern int LoadCLRRuntime(
		const char* currentExeAbsolutePath,
		const char* clrFilesAbsolutePath,
		const char* managedAssemblyAbsolutePath);
	extern CalculateMessageHeight *CreateCalculateMessageHeightDelegate();
	extern float CalculateMessageHeightDirect(const char *Channel, const char *Input, const char *Username, const char *DisplayName, int NumberOfBadges, TwitchEmote *TwitchEmotes, int TwitchEmotesLen);
	extern int InitCharMap(const char *CharMapPath);
	extern int InitChannel(const char *Channel, const char* ChannelId, int TimeoutMs);
	extern int InitChannel2(const char *Channel, const char* ChannelId, int TimeoutMs, bool Enable7TVEmotes);
#ifdef __cplusplus
}
#endif