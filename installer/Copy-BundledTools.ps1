# Добавляет инструменты обработки в автономную сборку.
[CmdletBinding()]
param([Parameter(Mandatory)][string]$Destination)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$video2x = Join-Path $root 'tools/video2x'
$ffmpeg = Join-Path $root 'tools/ffmpeg/ffmpeg-9.0.2-essentials_build'
$video2xFiles = @(
    'video2x.exe', 'libvideo2x.dll', 'ncnn.dll', 'spdlog.dll',
    'librealcugan-ncnn-vulkan.dll', 'librealesrgan-ncnn-vulkan.dll', 'librife-ncnn-vulkan.dll',
    'boost_program_options-vc143-mt-x64-1_86.dll',
    'avcodec-61.dll', 'avdevice-61.dll', 'avfilter-10.dll', 'avformat-61.dll',
    'avutil-59.dll', 'postproc-58.dll', 'swresample-5.dll', 'swscale-8.dll'
)
$ffmpegFiles = @('bin/ffmpeg.exe', 'bin/ffprobe.exe', 'LICENSE', 'README.txt')
$models = @(
    'realcugan/models-se/up2x-no-denoise', 'realcugan/models-se/up3x-no-denoise',
    'realcugan/models-se/up4x-no-denoise', 'realcugan/models-se/up2x-denoise1x',
    'realcugan/models-pro/up2x-conservative', 'realcugan/models-pro/up3x-conservative',
    'realesrgan/realesr-animevideov3-x2', 'realesrgan/realesr-animevideov3-x3',
    'realesrgan/realesr-animevideov3-x4', 'realesrgan/realesrgan-plus-x4'
)
$required = @($video2xFiles | ForEach-Object { Join-Path $video2x $_ })
$required += $ffmpegFiles | ForEach-Object { Join-Path $ffmpeg $_ }
$required += $models | ForEach-Object {
    Join-Path $video2x "models/$_.bin"
    Join-Path $video2x "models/$_.param"
}
foreach ($file in $required) {
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "Не найден файл для установщика: $file" }
}

$vswhere = Join-Path ([Environment]::GetFolderPath('ProgramFilesX86')) 'Microsoft Visual Studio/Installer/vswhere.exe'
if (-not (Test-Path -LiteralPath $vswhere -PathType Leaf)) {
    throw 'Для сборки установщика установите Visual Studio или Build Tools с компонентом C++ x64.'
}
$runtimeNames = @('msvcp140.dll', 'vcruntime140.dll', 'vcruntime140_1.dll', 'vcomp140.dll')
$runtimeFiles = @()
foreach ($installation in @(& $vswhere -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath)) {
    $redist = Join-Path $installation 'VC/Redist/MSVC'
    if (-not (Test-Path -LiteralPath $redist -PathType Container)) { continue }

    $versions = Get-ChildItem -LiteralPath $redist -Directory |
        Where-Object { $_.Name -match '^\d+\.\d+\.\d+(\.\d+)?$' } |
        Sort-Object { [version]$_.Name } -Descending
    foreach ($version in $versions) {
        $x64 = Join-Path $version.FullName 'x64'
        if (-not (Test-Path -LiteralPath $x64 -PathType Container)) { continue }

        $candidate = @(Get-ChildItem -LiteralPath $x64 -Recurse -File | Where-Object {
            $_.Name -in $runtimeNames -and $_.Directory.Name -match '^Microsoft\.VC\d+\.(CRT|OpenMP)$'
        })
        if (@($candidate.Name | Sort-Object -Unique).Count -eq $runtimeNames.Count) {
            $runtimeFiles = $candidate
            break
        }
    }
    if ($runtimeFiles.Count) { break }
}
if (-not $runtimeFiles.Count) { throw 'Не найдены DLL Visual C++ x64 и OpenMP в VC/Redist/MSVC.' }

$videoTarget = Join-Path $Destination 'tools/video2x'
$ffmpegTarget = Join-Path $Destination 'tools/ffmpeg'
New-Item -ItemType Directory -Path $videoTarget, $ffmpegTarget -Force | Out-Null
foreach ($file in $video2xFiles) {
    Copy-Item -LiteralPath (Join-Path $video2x $file) -Destination $videoTarget
}
Copy-Item -LiteralPath (Join-Path $video2x 'models') -Destination $videoTarget -Recurse
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'licenses/Video2X.txt') -Destination (Join-Path $videoTarget 'LICENSE.txt')
foreach ($file in $ffmpegFiles) {
    Copy-Item -LiteralPath (Join-Path $ffmpeg $file) -Destination $ffmpegTarget
}
foreach ($file in $runtimeFiles) {
    Copy-Item -LiteralPath $file.FullName -Destination $videoTarget
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'licenses/THIRD-PARTY-NOTICES.txt') -Destination $Destination
