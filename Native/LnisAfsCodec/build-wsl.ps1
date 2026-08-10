param([string]$Distribution = "Ubuntu")
$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = (Resolve-Path (Join-Path $here "..\..")).Path
$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$stage = Join-Path $tempRoot ("LnisAfsCodecBuild-" + [guid]::NewGuid().ToString("N"))
try {
    New-Item -ItemType Directory -Path "$stage\wrapper" | Out-Null
    New-Item -ItemType Directory -Path "$stage\lans" | Out-Null
    New-Item -ItemType Directory -Path "$stage\pocketlib" | Out-Null
    Copy-Item -LiteralPath "$here\lnis_afs_codec.c" -Destination "$stage\wrapper\lnis_afs_codec.c"
    Copy-Item -LiteralPath "$here\lnis_afs_codec.h" -Destination "$stage\wrapper\lnis_afs_codec.h"
    Copy-Item -LiteralPath "$here\build-mingw.sh" -Destination "$stage\build-mingw.sh"
    $openSource = Get-ChildItem -LiteralPath $root -Directory | Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName "LANS-AFS-SIM-main") -PathType Container } | Select-Object -First 1
    if ($null -eq $openSource) { throw "Unable to locate the open-source directory." }
    $lans = Join-Path $openSource.FullName "LANS-AFS-SIM-main"
    $pocket = Join-Path $openSource.FullName "PocketSDR-AFS-main"
    Copy-Item -LiteralPath "$lans\afs_nav.c" -Destination "$stage\lans\afs_nav.c"
    Copy-Item -LiteralPath "$lans\afs_nav.h" -Destination "$stage\lans\afs_nav.h"
    Copy-Item -LiteralPath "$lans\ldpc" -Destination "$stage\lans\ldpc" -Recurse
    Copy-Item -LiteralPath "$lans\rtklib" -Destination "$stage\lans\rtklib" -Recurse
    Copy-Item -LiteralPath "$lans\pocketsdr" -Destination "$stage\lans\pocketsdr" -Recurse
    Copy-Item -LiteralPath "$pocket\lib\win32\libsdr.a" -Destination "$stage\pocketlib\libsdr.a"
    Copy-Item -LiteralPath "$pocket\lib\win32\libldpc.a" -Destination "$stage\pocketlib\libldpc.a"
    $source = Join-Path $stage "lans\afs_nav.c"
    $text = [IO.File]::ReadAllText($source)
    $text = [Text.RegularExpressions.Regex]::Replace($text, 'static void bits_to_hex\(.*?\r?\n\}', 'static void bits_to_hex(const uint8_t* bits, int len, char* hex) { (void)bits; (void)len; if (hex) hex[0] = 0; }', [Text.RegularExpressions.RegexOptions]::Singleline)
    $text = [Text.RegularExpressions.Regex]::Replace($text, 'void log_AFS_bits\(.*?\r?\n\}', 'void log_AFS_bits(FILE* fp, const char* id, int prn, int toi, int sb, const char* stage, const uint8_t* bits, int len) { (void)fp; (void)id; (void)prn; (void)toi; (void)sb; (void)stage; (void)bits; (void)len; }', [Text.RegularExpressions.RegexOptions]::Singleline)
    [IO.File]::WriteAllText($source, $text, [Text.UTF8Encoding]::new($false))
    $linuxStage = '/mnt/c' + $stage.Substring(2).Replace('\','/')
    wsl.exe -d $Distribution --cd /home/imt bash "$linuxStage/build-mingw.sh" "$linuxStage"
    if ($LASTEXITCODE -ne 0) { throw "Native DLL build failed with exit code $LASTEXITCODE." }
    $output = Join-Path $here "bin\win-x64"
    New-Item -ItemType Directory -Path $output -Force | Out-Null
    Copy-Item -LiteralPath "$stage\LnisAfsCodec.dll" -Destination "$output\LnisAfsCodec.dll" -Force
}
finally {
    $resolved = [IO.Path]::GetFullPath($stage)
    if ($resolved.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -and (Split-Path $resolved -Leaf).StartsWith("LnisAfsCodecBuild-")) {
        if (Test-Path -LiteralPath $resolved -PathType Container) { Remove-Item -LiteralPath $resolved -Recurse -Force }
    }
}
