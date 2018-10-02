package main

// #cgo LDFLAGS: -L../c-interop -lcoreruncommon -ldl -lstdc++
// #include "../c-interop/exports.h"
// #include <stdlib.h>
import "C"
import "fmt"
import "unsafe"
import "os"
import "time"

func main() {
	
	clr1 := C.CString("/home/user/prog/MessageHeightTwitch/go-interop/main")
	clr2 := C.CString("/home/user/.dotnet/shared/Microsoft.NETCore.App/2.1.4/")
	clr3 := C.CString("/home/user/prog/MessageHeightTwitch/bin/Debug/netstandard2.0/MessageHeightTwitch.dll")
	
	var res C.int
	
	res = C.LoadCLRRuntime(
		clr1,
		clr2,
		clr3)
	
	C.free(unsafe.Pointer(clr1))
	C.free(unsafe.Pointer(clr2))
	C.free(unsafe.Pointer(clr3))
	
	if res != 0 {
		fmt.Println("failed to load CLR runtime")
		os.Exit(int(res))
	}
	
	charMap := C.CString("../charmap.bin.gz")
	channel := C.CString("channel")
	
	res = C.InitMessageHeightTwitch(charMap, channel)
	
	C.free(unsafe.Pointer(charMap))
	C.free(unsafe.Pointer(channel))
	
	if res != 0 {
		fmt.Println("failed to init MessageHeightTwitch")
		os.Exit(int(res))
	}
	
	input := C.CString("NaM")
	username := C.CString("tetyys")
	test := C.CString("test")
	testhttp := C.CString("http://test.com/")
	pajaDank := C.CString("pajaDank")
	pajaDankUrl := C.CString("https://static-cdn.jtvnw.net/emoticons/v1/129570/1.0")
	
	var te [2](C.TwitchEmote)
	te[0] = C.TwitchEmote{pajaDank, pajaDankUrl}
	te[1] = C.TwitchEmote{test, testhttp}
	
	pArray := unsafe.Pointer(&te[0])
	
	start := time.Now()
	var height C.float
	
	for i := 0; i < 1000; i++ {
		height = C.CalculateMessageHeightDirect(
			input,
			username,
			username,
			C.int(1),
			((*C.TwitchEmote)(pArray)),
			C.int(2))
	}
	
	elapsed := time.Since(start)
	
	fmt.Println(elapsed)
	
	C.free(unsafe.Pointer(input))
	C.free(unsafe.Pointer(username))
	C.free(unsafe.Pointer(test))
	C.free(unsafe.Pointer(testhttp))
	C.free(unsafe.Pointer(pajaDank))
	C.free(unsafe.Pointer(pajaDankUrl))
	
	fmt.Println(height)
}
