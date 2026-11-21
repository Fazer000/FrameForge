# Собирает установщик Frame Forge для Windows x64.
[CmdletBinding()]
param(
    [string]$Compiler = '',
    [ValidatePattern('^\d+\.\d+\.\d+(\.\d+)?$')]
    [string]$Version = '0.1.0'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (-not $Compiler) {
    $Compiler = (Get-Command ISCC.exe -ErrorAction SilentlyContinue).Source
}
if (-not $Compiler) {
    $Compiler = @(
        (Join-Path $root 'tools/inno-6.7.4/ISCC.exe')
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    ) | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
}
if (-not $Compiler -or -not (Test-Path -LiteralPath $Compiler -PathType Leaf)) {
    throw 'Укажите путь к ISCC.exe через -Compiler или установите Inno Setup 6.'
}

$project = Join-Path $root 'src/FrameForge/FrameForge.csproj'
$publish = Join-Path $root 'artifacts/publish/win-x64'
$parent = $publish
while ($parent -ne $root) {
    if ((Test-Path -LiteralPath $parent) -and
        (Get-Item -LiteralPath $parent).Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)) {
        throw 'Папка публикации перенаправлена.'
    }
    $parent = Split-Path $parent -Parent
}
if (Test-Path -LiteralPath $publish) {
    $target = (Resolve-Path -LiteralPath $publish).Path
    if ($target -ne $publish) { throw 'Не удалось подтвердить путь очистки.' }
    Remove-Item -LiteralPath $target -Recurse -Force
}

dotnet publish $project -c Release -r win-x64 --self-contained true -p:Version=$Version -o $publish
if ($LASTEXITCODE -ne 0) { throw "Ошибка публикации .NET: $LASTEXITCODE" }

& (Join-Path $PSScriptRoot 'Copy-BundledTools.ps1') -Destination $publish

& $Compiler "/DAppVersion=$Version" (Join-Path $PSScriptRoot 'FrameForge.iss')
if ($LASTEXITCODE -ne 0) { throw "Ошибка Inno Setup: $LASTEXITCODE" }
