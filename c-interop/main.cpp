#include <chrono>
#include <cstdio>
#include <thread>
#include "exports.h"

static const int kTimeoutMs = 30000;
static const int kMeasureAttempts = 300;
static const auto kMeasureInterval = std::chrono::milliseconds(100);

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

    // Channel registration is queued; measurement returns 0 until it is published.
    float height = 0;
    for (int attempt = 0; attempt < kMeasureAttempts && height == 0; attempt++) {
        if (attempt > 0)
            std::this_thread::sleep_for(kMeasureInterval);
        height = CalculateMessageHeight(argv[1], "Hello pajaDank", "example_user", "Example User", 1, emotes, 1);
    }

    if (height == 0) {
        std::fprintf(stderr, "Channel did not finish loading\n");
        return 1;
    }

    std::printf("Message height: %.1f\n", height);
    return 0;
}
