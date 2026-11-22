$ErrorActionPreference = 'Stop'

$source = Join-Path $PSScriptRoot 'src'
$outputFolder = Join-Path $PSScriptRoot 'dist'
$output = Join-Path $outputFolder 'HealthReminder.exe'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $compiler)) {
    throw 'The Windows .NET Framework C# compiler could not be found.'
}

New-Item -Path $outputFolder -ItemType Directory -Force | Out-Null
$manifest = Join-Path $source 'app.manifest'
$icon = Join-Path $source 'app.ico'
$defaultPet = Join-Path $source 'default-pet.webp'
$license = Join-Path $PSScriptRoot 'LICENSE'
$webp = Join-Path $PSScriptRoot 'vendor\webp'
$notices = Join-Path $webp 'ThirdPartyNotices.txt'
$sources = Get-ChildItem -LiteralPath $source -Filter '*.cs' -File | Sort-Object Name
$options = @(
    '/nologo', '/target:winexe', '/optimize+', '/warnaserror+', '/codepage:65001', '/unsafe+',
    "/out:$output", "/win32manifest:$manifest", "/win32icon:$icon",
    "/resource:$defaultPet,DesktopPet.Default", "/resource:$license,HealthReminder.License",
    "/resource:$notices,WebP.ThirdPartyNotices"
)
foreach ($architecture in @('x64', 'x86', 'arm64')) {
    foreach ($library in @('libsharpyuv.dll', 'libwebp.dll', 'libwebpdemux.dll')) {
        $path = Join-Path (Join-Path $webp $architecture) $library
        $options += "/resource:$path,WebP.$architecture.$library"
    }
}
& $compiler @options @($sources.FullName)
if ($LASTEXITCODE -ne 0) {
    throw "Compilation failed with exit code $LASTEXITCODE."
}
Copy-Item -LiteralPath $notices -Destination (Join-Path $outputFolder 'ThirdPartyNotices.txt')
Write-Output $output
