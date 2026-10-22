<#
.SYNOPSIS
    Builds Quire executables: self-contained and framework-dependent, zipped with checksums.

.DESCRIPTION
    Self-contained builds bundle the .NET runtime into a single Quire.exe (about 60 MB) that runs
    on any Windows 10/11 machine. The framework-dependent build is a small Quire.exe (about 3 MB)
    that needs the .NET 10 Desktop Runtime installed. The release workflow runs this same script.

.EXAMPLE
    ./tools/publish.ps1
    Builds every variant into dist/ with the version from Directory.Build.props.

.EXAMPLE
    ./tools/publish.ps1 -Version 0.2.0 -Runtimes win-x64 -Variants SelfContained
    Builds only the x64 self-contained exe, stamped as version 0.2.0.
#>
[CmdletBinding()]
param(
    # Version stamped into the exe and the zip names. Defaults to <Version> in Directory.Build.props.
    [string] $Version,

    [ValidateSet('win-x64', 'win-arm64')]
    [string[]] $Runtimes = @('win-x64', 'win-arm64'),

    [ValidateSet('SelfContained', 'FrameworkDependent')]
    [string[]] $Variants = @('SelfContained', 'FrameworkDependent'),

    [string] $OutputDirectory = 'dist'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    if (-not $Version) {
        $Version = ([xml](Get-Content Directory.Build.props)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
    }

    $dotnet = (Get-Command dotnet -ErrorAction Stop).Source
    $work = Join-Path $root 'out'
    Remove-Item -Recurse -Force $work, $OutputDirectory -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force $OutputDirectory | Out-Null

    $common = @(
        'publish', 'src/Quire.App', '-c', 'Release', "-p:Version=$Version",
        '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true', '-p:DebugType=embedded'
    )

    foreach ($runtime in $Runtimes) {
        foreach ($variant in $Variants) {
            if ($variant -eq 'SelfContained') {
                $name = "Quire-$Version-$runtime"
                $flags = @('--self-contained', '-p:EnableCompressionInSingleFile=true')
            }
            else {
                $name = "Quire-$Version-$runtime-framework-dependent"
                $flags = @('--self-contained', 'false')
            }

            $target = Join-Path $work $name
            Write-Host "Publishing $name" -ForegroundColor Cyan
            & $dotnet @common -r $runtime @flags -o $target
            if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $name" }

            Copy-Item LICENSE.txt, README.md $target
            Compress-Archive -Path (Join-Path $target '*') -DestinationPath (Join-Path $OutputDirectory "$name.zip") -Force
        }
    }

    Get-ChildItem $OutputDirectory -Filter *.zip | Sort-Object Name | ForEach-Object {
        "$((Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant())  $($_.Name)"
    } | Set-Content (Join-Path $OutputDirectory 'SHA256SUMS.txt') -Encoding ascii

    Write-Host "`nBuilt into $OutputDirectory (unzipped copies in out/):" -ForegroundColor Green
    Get-ChildItem $OutputDirectory | ForEach-Object { '  {0,-55} {1,8:N1} MB' -f $_.Name, ($_.Length / 1MB) }
}
finally {
    Pop-Location
}
