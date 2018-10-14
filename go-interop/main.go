package main

// #cgo LDFLAGS: -L../c-interop -lcoreruncommon -ldl -lstdc++
// #include "../c-interop/exports.h"
// #include <stdlib.h>
import "C"
import "fmt"
import "unsafe"
import "os"

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
	channel2 := C.CString("channel2")
	
	res = C.InitCharMap(charMap)
	C.free(unsafe.Pointer(charMap))
	
	if res != 1 {
		fmt.Println("Failed to fill charmap")
		os.Exit(1)
	}
	
	res = C.InitChannel(channel)
	
	if res != 1 {
		fmt.Println("Failed to load channel")
		os.Exit(1)
	}
	
	res = C.InitChannel(channel2)
	
	if res != 1 {
		fmt.Println("Failed to load channel2")
		os.Exit(1)
	}
	
	input := C.CString("NaM")
	username := C.CString("tetyys")
	test := C.CString("test")
	testhttp := C.CString("https://test.com/")
	pajaDank := C.CString("pajaDank")
	pajaDankUrl := C.CString("https://static-cdn.jtvnw.net/emoticons/v1/129570/1.0")
	
	var te [2](C.TwitchEmote)
	te[0] = C.TwitchEmote{pajaDank, pajaDankUrl}
	te[1] = C.TwitchEmote{test, testhttp}
	
	pArray := unsafe.Pointer(&te[0])
	
	var height C.float
	var height2 C.float
	
	height = C.CalculateMessageHeightDirect(
		channel,
		input,
		username,
		username,
		C.int(1),
		((*C.TwitchEmote)(pArray)),
		C.int(2))
		
	height2 = C.CalculateMessageHeightDirect(
		channel2,
		input,
		username,
		username,
		C.int(1),
		((*C.TwitchEmote)(pArray)),
		C.int(2))
	
	C.free(unsafe.Pointer(channel))
	C.free(unsafe.Pointer(channel2))
	
	C.free(unsafe.Pointer(input))
	C.free(unsafe.Pointer(username))
	C.free(unsafe.Pointer(test))
	C.free(unsafe.Pointer(testhttp))
	C.free(unsafe.Pointer(pajaDank))
	C.free(unsafe.Pointer(pajaDankUrl))
	
	fmt.Println(height)
	fmt.Println(height2)
}
