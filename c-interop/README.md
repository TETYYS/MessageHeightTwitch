# C and C++ interop

The Native AOT build exports the functions in [exports.h](exports.h) directly.
The former CoreCLR host and delegate creation layer are no longer needed.

From the repository root on Windows:

```sh
dotnet publish MessageHeightTwitch.csproj -c Release -r win-x64
g++ c-interop/main.cpp bin/Release/net10.0/win-x64/publish/MessageHeightTwitch.dll -o bin/Release/net10.0/win-x64/publish/example.exe
cd bin/Release/net10.0/win-x64/publish
./example.exe CHANNEL CHANNEL_ID
```

From the repository root on Linux or WSL:

```sh
dotnet publish MessageHeightTwitch.csproj -c Release -r linux-x64
g++ c-interop/main.cpp -Lbin/Release/net10.0/linux-x64/publish -l:MessageHeightTwitch.so -o bin/Release/net10.0/linux-x64/publish/example
cd bin/Release/net10.0/linux-x64/publish
LD_LIBRARY_PATH="$PWD${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}" ./example CHANNEL CHANNEL_ID
```

The project enables Native AOT shared-library publishing by default. Publish on
the target OS and link against the native `MessageHeightTwitch.so`; a managed
`.dll` produced with `-p:PublishAot=false` cannot provide the C exports.
`LD_LIBRARY_PATH` lets the loader find the library and its native dependencies.
Set `MESSAGEHEIGHTTWITCH_FONTDIR`
to the published `Fonts` subdirectory, pass it as the optional `FONT_DIR` command-line argument,
or keep the process working directory at the publish directory.

All C strings are null-terminated UTF-8. The `TwitchEmote` array is optional;
pass `NULL, 0` when there are no emotes. Call `InitMessageHeightTwitch` first.
Its first argument selects the font directory, overriding `MESSAGEHEIGHTTWITCH_FONTDIR`
and the default `./Fonts`. Deploy the `.ttf` files and both Windows `.bin` profiles
together. Local setup is synchronous; global emote loading runs in the background.
A process shares one font directory, and repeated initialization reuses it.

`InitChannel` queues only channel resources. `CalculateMessageHeight` returns 0
until its catalogs finish loading and the channel is published, so the example
retries until it gets a height. Timeouts and error callbacks are implemented by
the native shim around the managed cancellation and exception API.
Global and channel emote requests require network access and retry on failures.

`InitChannel` takes an optional error callback; the example passes one that prints to stderr.
Global errors use an empty channel name; keep the global callback alive for the process.
See [exports.h](exports.h) for error codes, callback lifetime, timeout semantics, and
measurement behavior while resources are loading. The library does not write to stdout or stderr.
