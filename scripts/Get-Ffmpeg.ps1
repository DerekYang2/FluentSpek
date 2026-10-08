# Downloads ffmpeg.exe from BtbN's FFmpeg 9.0 LGPL builds into ffmpeg\win-<arch>\ in the repository. FluentSpek's build
# copies it next to FluentSpek.exe when it's there, and CI points the tests at it.
param(
    [ValidateSet('x64', 'arm64')]
    [string[]] $Architecture = @('x64'),
    [string] $Destination = (Join-Path $PSScriptRoot '..\ffmpeg')
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
Add-Type -AssemblyName System.IO.Compression.FileSystem

$assets = @{
    x64   = 'ffmpeg-n9.0-latest-win64-lgpl-9.0.zip'
    arm64 = 'ffmpeg-n9.0-latest-winarm64-lgpl-9.0.zip'
}

foreach ($arch in $Architecture) {
    $url = "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/$($assets[$arch])"
    $folder = Join-Path $Destination "win-$arch"
    $zip = Join-Path ([IO.Path]::GetTempPath()) $assets[$arch]
    New-Item -ItemType Directory -Force $folder | Out-Null

    Write-Host "Downloading $url"
    Invoke-WebRequest $url -OutFile $zip

    $archive = [IO.Compression.ZipFile]::OpenRead($zip)
    try {
        foreach ($name in 'bin/ffmpeg.exe', 'LICENSE.txt') {
            $entry = $archive.Entries | Where-Object { $_.FullName -like "*/$name" } | Select-Object -First 1
            if (-not $entry) { throw "$name is missing from $($assets[$arch])" }
            $target = Join-Path $folder (Split-Path $name -Leaf)
            [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $target, $true)
        }
    }
    finally {
        $archive.Dispose()
        Remove-Item $zip
    }

    $native = if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'arm64' } else { 'x64' }
    if ($arch -eq $native -or ($arch -eq 'x64' -and $native -eq 'arm64')) {
        $version = (& (Join-Path $folder 'ffmpeg.exe') -hide_banner -version | Select-Object -First 1)
        Write-Host "$folder\ffmpeg.exe: $version"
    }
    else {
        # An x64 machine can't run ARM64 programs, so this one can't be checked here.
        Write-Host "$folder\ffmpeg.exe: downloaded"
    }
}
