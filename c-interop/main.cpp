#include <cstdio>
#include "exports.h"

static const int kTimeoutMs = 30000;

static void MHT_CDECL OnEmoteError(void*, const char* channel, int errorCode, const char* message)
{
    std::fprintf(stderr, "%s: error %d: %s\n", channel, errorCode, message);
}

int main(int argc, char** argv)
{
    if (argc != 3 && argc != 4) {
        std::fprintf(stderr, "Usage: %s CHANNEL CHANNEL_ID [FONT_DIR]\n", argv[0]);
        return 2;
    }

    if (InitMessageHeightTwitch(argc == 4 ? argv[3] : nullptr, kTimeoutMs, 0, OnEmoteError, nullptr) != 1) {
        std::fprintf(stderr, "Could not initialize shared resources\n");
        return 1;
    }

    if (InitChannel(argv[1], argv[2], kTimeoutMs, 0, OnEmoteError, nullptr) != 1) {
        std::fprintf(stderr, "Could not initialize channel\n");
        return 1;
    }

    const TwitchEmote emotes[] = {
        {"pajaDank", "https://static-cdn.jtvnw.net/emoticons/v1/129570/1.0"}
    };

    float height = CalculateMessageHeight(argv[1], "Hello pajaDank", "example_user", "Example User", 1, emotes, 1);

    if (height == 0) {
        std::fprintf(stderr, "Measurement failed; see the error callback\n");
        return 1;
    }

    std::printf("Message height: %.1f\n", height);
    return 0;
}
