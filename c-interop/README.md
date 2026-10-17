# c-interop

Example how to call MessageHeightTwitch from C++

```
g++ c-interop/main.cpp -Lbin/Release/net10.0/linux-x64/publish -l:MessageHeightTwitch.so -o bin/Release/net10.0/linux-x64/publish/example
cd bin/Release/net10.0/linux-x64/publish/
LD_LIBRARY_PATH="$PWD${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}" ./example CHANNEL CHANNEL_ID

```