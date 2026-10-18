<#
.SYNOPSIS
Builds the filtered ICU data DLLs in Native/icu78 from the official ICU release.
#>
param(
    [string]$Output = (Join-Path $PSScriptRoot '..\..\Native\icu78'),
    # A short path: pkgdata and the linker run inside it.
    [string]$Work = (Join-Path $env:TEMP 'icudata78')
)
$ErrorActionPreference = 'Stop'

$Release = '78.3'
$LaoRevision = '6ebb40c594776cc2c21ea14df85a2a89a328b364'
$LaoSha256 = 'D2C4F10B0E80ABAA59EF7DCDAC0D1ADEDB785E2F4F40F4F7FF79F87E36BB55F2'
$Targets = @(
    @{ Rid = 'win-x64'; Zip = 'icu4c-78.3-Win64-MSVC2022.zip'; Sha256 = '446B671F9437227DAA79E221D4521D75793F9ECD65AC44C06E34DD848F201AC2'; Bin = 'bin64'; Vcvars = 'vcvars64.bat' }
    @{ Rid = 'win-x86'; Zip = 'icu4c-78.3-Win32-MSVC2022.zip'; Sha256 = '4DCB7CB19C375A3CA0B38B7C82CEBAA11D2E765C57BC49A49162BB469BA568A2'; Bin = 'bin'; Vcvars = 'vcvars32.bat' }
)

# The common data starts with its header size, the magic bytes 0xDA 0x27 and,
# at offset 12, the "CmnD" data format.
$MagicOffset = 2
$FormatOffset = 12
$Magic = [byte[]](0xDA, 0x27)
$Format = [Text.Encoding]::ASCII.GetBytes('CmnD')

function Find-CommonData([byte[]]$Bytes) {
    for ($x = 0; $x -le $Bytes.Length - $FormatOffset - $Format.Length; $x++) {
        if ($Bytes[$x + $MagicOffset] -eq $Magic[0] -and $Bytes[$x + $MagicOffset + 1] -eq $Magic[1] -and
            [Linq.Enumerable]::SequenceEqual([ArraySegment[byte]]::new($Bytes, $x + $FormatOffset, $Format.Length), $Format)) {
            return $x
        }
    }
    throw 'No ICU common data found.'
}

$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$vs = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $vs) { throw 'Visual Studio with the C++ x86/x64 build tools is required.' }

$items = Get-Content (Join-Path $PSScriptRoot 'items.txt') | Where-Object { $_ -and -not $_.StartsWith('#') }
New-Item -ItemType Directory -Force $Work | Out-Null
$list = Join-Path $Work 'items.txt'
Set-Content $list $items -Encoding ascii

# Chromium retains the smaller ICU 69 Lao word list; upstream 78.3 splits different words.
$lao = Join-Path $Work 'chromium-laodict.txt'
if (-not (Test-Path -LiteralPath $lao)) {
    $response = Invoke-WebRequest "https://chromium.googlesource.com/chromium/deps/icu/+/$LaoRevision/source/data/brkitr/dictionaries/laodict.txt?format=TEXT"
    [IO.File]::WriteAllBytes($lao, [Convert]::FromBase64String($response.Content))
}
if ((Get-FileHash -LiteralPath $lao -Algorithm SHA256).Hash -ne $LaoSha256) { throw 'Chromium Lao word list does not match its SHA-256.' }

foreach ($target in $Targets) {
    $zip = Join-Path $Work $target.Zip
    if (-not (Test-Path $zip)) {
        Invoke-WebRequest "https://github.com/unicode-org/icu/releases/download/release-$Release/$($target.Zip)" -OutFile $zip
    }
    if ((Get-FileHash $zip -Algorithm SHA256).Hash -ne $target.Sha256) { throw "$($target.Zip) does not match its SHA-256." }

    $root = Join-Path $Work $target.Rid
    Remove-Item -Recurse -Force $root -ErrorAction SilentlyContinue
    Expand-Archive $zip $root
    $bin = Join-Path $root $target.Bin

    $bytes = [IO.File]::ReadAllBytes((Join-Path $bin 'icudt78.dll'))
    $start = Find-CommonData $bytes
    $dat = Join-Path $root 'icudt78l.dat'
    [IO.File]::WriteAllBytes($dat, $bytes[$start..($bytes.Length - 1)])

    $source = Join-Path $root 'items'
    $temp = Join-Path $root 'tmp'
    New-Item -ItemType Directory $source, $temp | Out-Null
    & (Join-Path $bin 'icupkg.exe') -x $list -d $source $dat
    if ($LASTEXITCODE) { throw "icupkg failed for $($target.Rid)." }

    & (Join-Path $bin 'gendict.exe') --bytes --transform offset-0e80 $lao (Join-Path $source 'brkitr/laodict.dict')
    if ($LASTEXITCODE) { throw "gendict failed for $($target.Rid)." }

    # pkgdata writes the DLL to the source directory.
    $vcvars = Join-Path $vs "VC\Auxiliary\Build\$($target.Vcvars)"
    cmd /c "`"$vcvars`" >nul 2>nul && `"$(Join-Path $bin 'pkgdata.exe')`" -q -m dll -p icudt78l -e icudt78 -L icudt78 -s `"$source`" -d `"$temp`" -T `"$temp`" `"$list`""
    if ($LASTEXITCODE) { throw "pkgdata failed for $($target.Rid)." }

    $destination = Join-Path $Output $target.Rid
    New-Item -ItemType Directory -Force $destination | Out-Null
    Copy-Item (Join-Path $source 'icudt78.dll') $destination -Force
    Copy-Item (Join-Path $bin 'icuuc78.dll') $destination -Force
    Get-ChildItem $destination -Filter *.dll | ForEach-Object { '{0}\{1} {2} {3}' -f $target.Rid, $_.Name, $_.Length, (Get-FileHash $_.FullName).Hash }
}
