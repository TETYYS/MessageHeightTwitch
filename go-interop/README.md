# Go interop

The example calls the Native AOT library through cgo and
[exports.h](../c-interop/exports.h). It does not host CoreCLR.

From the repository root on Windows:

```sh
dotnet publish MessageHeightTwitch.csproj -c Release -r win-x64
go build -o bin/Release/net10.0/win-x64/publish/go-example.exe go-interop/main.go
cd bin/Release/net10.0/win-x64/publish
./go-example.exe CHANNEL CHANNEL_ID
```

From the repository root on Linux or WSL:

```sh
dotnet publish MessageHeightTwitch.csproj -c Release -r linux-x64
nm -D --defined-only bin/Release/net10.0/linux-x64/publish/MessageHeightTwitch.so
go build -o bin/Release/net10.0/linux-x64/publish/go-example go-interop/main.go
cd bin/Release/net10.0/linux-x64/publish
./go-example CHANNEL CHANNEL_ID
```

The Linux linker flags embed `$ORIGIN` as the runtime library search path, so the
loader finds `MessageHeightTwitch.so` beside the Go executable, regardless of the
working directory. Keep the published native dependencies alongside it too.
`-L` only sets the build-time search path; Linux does not automatically search the
executable's directory at runtime. Rebuild the Go executable to apply these flags.
For an existing executable without this search path, run from the publish directory
with `LD_LIBRARY_PATH="$PWD${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}" ./go-example CHANNEL CHANNEL_ID`.

The project enables `PublishAot=true` and `NativeLib=Shared`, producing
`MessageHeightTwitch.so` on Linux and a native `MessageHeightTwitch.dll` on Windows.
The `nm` output must include `InitMessageHeightTwitch`, `InitChannel`, and
`CalculateMessageHeight`. A publish with `-p:PublishAot=false` produces a managed
`.dll` without these native exports; renaming it to `.so` does not make it linkable
from cgo. Native AOT publishing must run on the target OS.

Set `MESSAGEHEIGHTTWITCH_FONTDIR` to the published `Fonts` subdirectory, pass that
path as the optional `FONT_DIR` command-line argument,
or run from the publish directory so the font files can be found.
Call `InitMessageHeightTwitch(fontDir, timeoutMs, enable7TvEmotes, onError, userData)`
first. It loads the font files and both Windows `.bin` profiles synchronously, then
starts global emote loading in the background. The process shares one font directory.
A `nil` directory uses the environment or `./Fonts`.

`InitChannel` queues channel emotes, with automatic retries and an optional Cdecl
error callback; see [exports.h](../c-interop/exports.h). The example passes `nil`
for the callbacks and retries measurement until the channel is published.
Keep a global callback alive for the process; its channel name is empty.
For a Go callback, use a C bridge to an exported Go function and a `runtime/cgo.Handle`
for context rather than storing a Go pointer in C. The callback may run concurrently
on background threads. The library does not write to stdout or stderr.
