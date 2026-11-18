param([Parameter(Mandatory)][string]$Ndk)
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $Ndk 'toolchains/llvm/prebuilt/windows-x86_64/bin/aarch64-linux-android32-clang++.cmd'
$output = Join-Path $PSScriptRoot '../Assets/Plugins/Android/arm64-v8a/libdvrvideo.so'
New-Item -ItemType Directory -Force (Split-Path $output) | Out-Null
& $compiler -std=c++17 -O2 -shared -fPIC -static-libstdc++ '-Wl,-z,max-page-size=16384' '-Wl,--no-undefined' (Join-Path $PSScriptRoot 'VideoTexture.cpp') -o $output -landroid -lGLESv3 -lEGL -llog
if ($LASTEXITCODE) { throw 'Native video bridge compilation failed' }

