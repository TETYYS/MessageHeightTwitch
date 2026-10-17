#ifndef MESSAGE_HEIGHT_TWITCH_EXPORTS_H
#define MESSAGE_HEIGHT_TWITCH_EXPORTS_H

#if defined(_WIN32)
#define MHT_CDECL __cdecl
#else
#define MHT_CDECL
#endif

#ifdef __cplusplus
extern "C" {
#endif

typedef struct TwitchEmote {
    const char* Name;
    const char* Url;
} TwitchEmote;

typedef enum EmoteErrorCode {
    MHT_FFZ_FETCH_FAILED = 1,
    MHT_BTTV_FETCH_FAILED = 2,
    MHT_SEVENTV_FETCH_FAILED = 3,
    MHT_TWITCH_FETCH_FAILED = 4,
    MHT_CHANNEL_INITIALIZATION_FAILED = 5,
    MHT_GLOBAL_INITIALIZATION_FAILED = 6
} EmoteErrorCode;

/*++

Routine Description:

    Reports emote loading errors using the Cdecl calling convention. Fetch
    errors are reported on background threads, potentially concurrently.
    Strings are null-terminated UTF-8 and valid only during the callback.
    Keep the callback and userData alive for the channel's lifetime, including
    callbacks already in flight when the channel is reinitialized. Keep the
    global callback and its userData alive for the process lifetime.

Arguments:

    userData - Caller-supplied context passed to the initialization function.

    channel - Channel name, or an empty string for global initialization errors.

    errorCode - An EmoteErrorCode value identifying the failure.

    message - Exception detail, including the URL for Twitch image failures.

Return Value:

    None.

--*/
typedef void (MHT_CDECL *EmoteErrorCallback)(void* userData, const char* channel, int errorCode, const char* message);

/*++

Routine Description:

    Loads fonts and WindowsFontMetrics.bin / WindowsSystemFallbacks.bin from
    the font directory, then queues global emote loading with automatic retries.

Arguments:

    fontDir - Font directory as a null-terminated UTF-8 string. NULL or an empty
        string uses MESSAGEHEIGHTTWITCH_FONTDIR, then ./Fonts.

    timeoutMs - Per-attempt timeout in milliseconds. Pass -1 to disable it.

    enable7TvEmotes - Zero disables 7TV emotes; nonzero enables them.

    onError - Optional error callback, or NULL. Global initialization failures
        report code 6 with an empty channel name.

    userData - Caller-supplied context passed to onError.

Return Value:

    1 after local setup succeeds; 0 on failure.

--*/
int MHT_CDECL InitMessageHeightTwitch(const char* fontDir, int timeoutMs, unsigned char enable7TvEmotes, EmoteErrorCallback onError, void* userData);

/*++

Routine Description:

    Queues channel initialization. Call InitMessageHeightTwitch first.
    All strings are null-terminated UTF-8. The channel is published after its
    catalogs finish loading. Failed network initialization attempts retry
    indefinitely with delays of 1, 2, 4, 8, 16, then 30 seconds. Non-network
    setup errors are not retried. Reinitializing the same channel cancels its
    previous background loads.

Arguments:

    channel - Channel name used to register and look up the channel.

    channelId - Twitch channel ID used to fetch channel emotes.

    timeoutMs - Per-attempt timeout in milliseconds, also used for Twitch image
        requests. Pass -1 to disable it.

    enable7TvEmotes - Zero disables 7TV emotes; nonzero enables them. Enabling
        7TV requires enabling it in InitMessageHeightTwitch first.

    onError - Optional error callback, or NULL. Channel initialization failures
        report code 5.

    userData - Caller-supplied context passed to onError.

Return Value:

    1 if initialization was queued; 0 for invalid arguments or if the required
    global resources have not been initialized.

--*/
int MHT_CDECL InitChannel(const char* channel, const char* channelId, int timeoutMs, unsigned char enable7TvEmotes, EmoteErrorCallback onError, void* userData);

/*++

Routine Description:

    Calculates a chat message's height. All strings are null-terminated UTF-8.
    Missing third-party emotes are measured as text until loaded. Twitch images
    use a 28x28 placeholder until fetched. Failed images retry in the background.

Arguments:

    channel - Previously initialized channel name.

    input - Message text to measure.

    username - Message author's username.

    displayName - Message author's display name.

    numberOfBadges - Number of badges displayed before the author's name.

    twitchEmotes - Array of Twitch emote names and image URLs, or NULL when
        twitchEmotesLen is zero.

    twitchEmotesLen - Number of entries in twitchEmotes.

Return Value:

    The message height, or 0 for an unknown or pending channel, failed channel
    setup, or invalid input.

--*/
float MHT_CDECL CalculateMessageHeight(const char* channel, const char* input, const char* username, const char* displayName, int numberOfBadges, const TwitchEmote* twitchEmotes, int twitchEmotesLen);

#ifdef __cplusplus
}
#endif

#endif
