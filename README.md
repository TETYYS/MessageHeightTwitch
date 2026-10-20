# MessageHeightTwitch

Library to measure message height on Twitch. Supports FFZ, BTTV and 7TV emotes. It replicates message dimension measurement under FFZ 4.82.0 with BTTV + 7TV sub-addons in Chrome 155, Windows 11 without any headless clients.

## Limitations

 * Personal emotes for 7TV are not supported
 * There is no automatic refresh for channel emotes nor there is a websocket connection for channel emotes
 * You are responsible for specifying the amount of badges message has
 * Full size GIFs are not supported, and their placeholder text will be measured instead
 * Link previews are not measured
 * Library is built with Twitch limits in mind (ex. 25 login length limit), passing parameters outside Twitch limits results in undefined behavior
 * Any bugs in FFZ or BTTV/7TV addons are measured with an expectation of a better future

## Setup

On Debian/Ubuntu, the following packages are required:

 * libc6
 * libstdc++6
 * libgcc-s1
 * libssl3t64
 * ca-certificates
 * cargo (with Rust compiler)

Managed executable projects using this library must set `InvariantGlobalization=true`, because filtered ICU bundle omits the locale data required by .NET's culture APIs.

Publish for the target platform with .NET 10:

```sh
dotnet publish MessageHeightTwitch.csproj -c Release -r linux-x64
```

Note that this will also build ICU 78.3 (`Tools/IcuData/Build-IcuData.*`) with same filtered data and Chromium Lao dictionary as Windows. Rust helper library will also be built (`Native/opentype`).

Font files are required and loaded from the first available setting:

1. `FontDir` argument to `InitMessageHeightTwitch`.
2. `MESSAGEHEIGHTTWITCH_FONTDIR` environment variable.
3. `Fonts` subdirectory of the process working directory.

## Usage

Call `InitMessageHeightTwitch(fontDir, timeoutMs, enable7TvEmotes, onError, userData)` before initializing individual channels. It loads fonts, profiles and shaping, then queues global BTTV, FFZ and optional 7TV resources.

`InitChannel(channel, channelId, timeoutMs, enable7TvEmotes, onError, userData)` sets up the channel measurer before returning success. Measurement is immediately available while emote catalogs and images load in the background. Known BTTV and Twitch images use a 28x28 placeholder until fetched. Failed loads retry automatically. Measurement exceptions are reported through the channel callback with code 7. Call `InitChannel` again to refresh a channel's emotes.

See [c-interop](c-interop/README.md) and [go-interop](go-interop/README.md).
