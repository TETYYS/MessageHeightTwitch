typedef struct _TwitchEmote
{
	const char *Name;
	const char *Url;
} TwitchEmote;

typedef float(CalculateMessageHeight)(const char *Input, const char *Username, const char *DisplayName, int NumberOfBadges, TwitchEmote *TwitchEmotes, int TwitchEmotesLen);
typedef void(Init)(const char *CharMapPath, const char *Channel);

#ifdef __cplusplus
extern "C" {
#endif
	extern int UnloadCLRRuntime();
	extern int LoadCLRRuntime(
		const char* currentExeAbsolutePath,
		const char* clrFilesAbsolutePath,
		const char* managedAssemblyAbsolutePath);
	extern CalculateMessageHeight *CreateCalculateMessageHeightDelegate();
	extern float CalculateMessageHeightDirect(const char *Input, const char *Username, const char *DisplayName, int NumberOfBadges, TwitchEmote *TwitchEmotes, int TwitchEmotesLen);
	extern int InitMessageHeightTwitch(const char *CharMapPath, const char *Channel);
#ifdef __cplusplus
}
#endif