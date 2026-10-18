package main

// #cgo linux LDFLAGS: -L../bin/Release/net10.0/linux-x64/publish -l:MessageHeightTwitch.so -Wl,-rpath,$ORIGIN
// #cgo windows LDFLAGS: -L../bin/Release/net10.0/win-x64/publish -lMessageHeightTwitch
// #include "../c-interop/exports.h"
// #include <stdlib.h>
import "C"

import (
    "fmt"
    "os"
    "time"
    "unsafe"
)

func main() {
    if len(os.Args) != 3 && len(os.Args) != 4 {
        fmt.Fprintf(os.Stderr, "Usage: %s CHANNEL CHANNEL_ID [FONT_DIR]\n", os.Args[0])
        os.Exit(2)
    }

    channel := C.CString(os.Args[1])
    defer C.free(unsafe.Pointer(channel))
    channelID := C.CString(os.Args[2])
    defer C.free(unsafe.Pointer(channelID))

    var fontDir *C.char
    if len(os.Args) == 4 {
        fontDir = C.CString(os.Args[3])
        defer C.free(unsafe.Pointer(fontDir))
    }

    if C.InitMessageHeightTwitch(fontDir, 30000, 0, nil, nil) != 1 {
        fmt.Fprintln(os.Stderr, "Could not initialize shared resources")
        os.Exit(1)
    }

    if C.InitChannel(channel, channelID, 30000, 0, nil, nil) != 1 {
        fmt.Fprintln(os.Stderr, "Could not initialize channel")
        os.Exit(1)
    }

    input := C.CString("Hello from Go")
    defer C.free(unsafe.Pointer(input))
    user := C.CString("example_user")
    defer C.free(unsafe.Pointer(user))
    measure := func() C.float {
        return C.CalculateMessageHeight(channel, input, user, user, 0, nil, 0)
    }

    // Channel registration is queued; measurement returns 0 until it is published.
    height := measure()
    for attempt := 0; height == 0 && attempt < 300; attempt++ {
        time.Sleep(100 * time.Millisecond)
        height = measure()
    }
    if height == 0 {
        fmt.Fprintln(os.Stderr, "Channel did not finish loading")
        os.Exit(1)
    }
    fmt.Println("Message height:", float32(height))
}
