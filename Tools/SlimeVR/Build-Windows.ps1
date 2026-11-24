[CmdletBinding()]
param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '../../Builds/SlimeVR'),
    [string]$JavaHome = $env:JAVA_HOME,
    [string]$StockSource,
    [string]$PairedSource
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$output = [IO.Directory]::CreateDirectory($OutputDirectory).FullName
$guiUrl = 'https://github.com/SlimeVR/SlimeVR-Server/releases/download/v21.1.0/SlimeVR-win64.zip'
$runtimeUrl = 'https://github.com/adoptium/temurin17-binaries/releases/download/jdk-17.0.21.1%2B2/OpenJDK17U-jre_x64_windows_hotspot_17.0.21.1_2.zip'
$guiHash = '44E6BA40BD4F15205648818E318F50ED8F049B3B80D577A227AEEF08F4D8C100'
$runtimeHash = '739D8F2FD286DC2736D55CC8C9260980BAEFD275AD48CDA242335BB0F08E0795'

function Get-VerifiedAsset([string]$Name, [string]$Url, [string]$Hash) {
    $file = Join-Path $output $Name
    if (-not (Test-Path -LiteralPath $file)) {
        $download = Join-Path $output ($Name + '.' + [Guid]::NewGuid() + '.download')
        try {
            Invoke-WebRequest -Uri $Url -OutFile $download -UseBasicParsing
            if ((Get-FileHash -LiteralPath $download).Hash -ne $Hash) { throw "SHA-256 mismatch for $Url" }
            Move-Item -LiteralPath $download -Destination $file
        } finally {
            if (Test-Path -LiteralPath $download) { Remove-Item -LiteralPath $download }
        }
    }
    if ((Get-FileHash -LiteralPath $file).Hash -ne $Hash) { throw "Unexpected SHA-256 for cached $file" }
    return $file
}

function Get-ChildPath([string]$Directory, [string]$Name) {
    $parent = [IO.Path]::GetFullPath($Directory).TrimEnd('\') + '\'
    $child = [IO.Path]::GetFullPath((Join-Path $Directory $Name))
    if (-not $child.StartsWith($parent, [StringComparison]::OrdinalIgnoreCase)) { throw "Path leaves $Directory" }
    return $child
}

function Write-ReleaseZip([string]$Directory, [string]$ZipPath) {
    $files = [IO.Directory]::GetFiles($Directory, '*', [IO.SearchOption]::AllDirectories)
    [Array]::Sort($files, [StringComparer]::Ordinal)
    $archive = [IO.Compression.ZipFile]::Open($ZipPath, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($file in $files) {
            $name = $file.Substring($Directory.Length + 1).Replace('\', '/')
            $entry = $archive.CreateEntry($name, [IO.Compression.CompressionLevel]::Optimal)
            $entry.LastWriteTime = [DateTimeOffset]::new(2000, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
            $target = $entry.Open()
            try {
                $sourceFile = [IO.File]::OpenRead($file)
                try { $sourceFile.CopyTo($target) } finally { $sourceFile.Dispose() }
            } finally { $target.Dispose() }
        }
    } finally { $archive.Dispose() }
}

& (Join-Path $PSScriptRoot 'Build-Server.ps1') -OutputDirectory $output -JavaHome $JavaHome -SourceDirectory $StockSource
& (Join-Path $PSScriptRoot 'Build-Server.ps1') -OutputDirectory $output -JavaHome $JavaHome -SourceDirectory $PairedSource -Paired
$gui = Get-VerifiedAsset 'SlimeVR-win64-v21.1.0.zip' $guiUrl $guiHash
$runtime = Get-VerifiedAsset 'OpenJDK17U-jre_x64_windows_hotspot_17.0.21.1_2.zip' $runtimeUrl $runtimeHash
$stagingName = 'package-' + [Guid]::NewGuid()
$staging = Get-ChildPath $output $stagingName
$portable = Get-ChildPath $staging 'Paired'
$javaExtract = Get-ChildPath $staging 'java'
$zipName = 'VRBodyTracking-Windows-x64.zip'
$newZip = Get-ChildPath $staging $zipName
$releaseZip = Get-ChildPath $output $zipName
try {
    [IO.Compression.ZipFile]::ExtractToDirectory($gui, $portable)
    [IO.Compression.ZipFile]::ExtractToDirectory($runtime, $javaExtract)
    [IO.Directory]::Move((Get-ChildPath $javaExtract 'jdk-17.0.21.1+2-jre'), (Get-ChildPath $portable 'jre'))
    $stockDir = [IO.Directory]::CreateDirectory((Join-Path $portable 'Stock')).FullName
    $stockJar = Join-Path $stockDir 'slimevr.jar'
    $pairedJar = Join-Path $portable 'slimevr.jar'
    Copy-Item -LiteralPath (Join-Path $output 'Stock/slimevr.jar') -Destination $stockJar -Force
    Copy-Item -LiteralPath (Join-Path $output 'PairedJar/slimevr.jar') -Destination $pairedJar -Force
    foreach ($notice in 'LICENSE-MIT', 'LICENSE-APACHE', 'TRADEMARK.md') {
        Copy-Item -LiteralPath (Join-Path $output "Stock/$notice") -Destination $portable
    }
    foreach ($name in 'Start-Paired.cmd', 'Start-Stock-Solo.cmd', 'Operator.txt') {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $portable
    }
    $patchHash = (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'paired-feet-v21.1.0.patch')).Hash
    $buildInfo = @(
        'VRBodyTracking desktop server for Windows x64',
        'SlimeVR Server v21.1.0, copyright (c) 2020 Eiren Rain and SlimeVR Contributors',
        'Source https://github.com/SlimeVR/SlimeVR-Server/tree/c11dc54ed1a745ffc4b15db7f820d8d0c1993990',
        'Blacktheon paired-feet extension from Tools/SlimeVR/paired-feet-v21.1.0.patch',
        'Project https://github.com/blacktheon/VRBodyTracking/tree/develop/Tools/SlimeVR',
        "Source patch SHA-256 $patchHash",
        "Stock/slimevr.jar SHA-256 $((Get-FileHash -LiteralPath $stockJar).Hash)",
        "slimevr.jar SHA-256 $((Get-FileHash -LiteralPath $pairedJar).Hash)",
        "Official SlimeVR GUI $guiUrl",
        "GUI archive SHA-256 $guiHash",
        "Eclipse Temurin 17.0.21.1+2 runtime $runtimeUrl",
        "Runtime archive SHA-256 $runtimeHash"
    )
    [IO.File]::WriteAllLines((Join-Path $portable 'BUILD-INFO.txt'), $buildInfo)
    $test = Join-Path $PSScriptRoot 'Test-Server.mjs'
    $javaExe = Join-Path $portable 'jre/bin/java.exe'
    & node $test --stock $stockJar $javaExe
    if ($LASTEXITCODE -ne 0) { throw 'Stock server integration test failed.' }
    & node $test $pairedJar $javaExe
    if ($LASTEXITCODE -ne 0) { throw 'Paired server integration test failed.' }
    & node (Join-Path $PSScriptRoot 'Test-Launchers.mjs') $portable
    if ($LASTEXITCODE -ne 0) { throw 'Windows GUI launcher test failed.' }
    Write-ReleaseZip $portable $newZip
    $installed = Get-ChildPath $output 'Paired'
    if ([IO.Directory]::Exists($installed)) {
        Get-ChildItem -LiteralPath $portable -Force | Copy-Item -Destination $installed -Recurse -Force
    } else {
        [IO.Directory]::Move($portable, $installed)
    }
    if ([IO.File]::Exists($releaseZip)) { [IO.File]::Replace($newZip, $releaseZip, [NullString]::Value) }
    else { [IO.File]::Move($newZip, $releaseZip) }
    Write-Output "Built $releaseZip"
    Get-FileHash -LiteralPath $releaseZip
} finally {
    $cleanup = Get-ChildPath $output $stagingName
    if ([IO.Directory]::Exists($cleanup)) { Remove-Item -LiteralPath $cleanup -Recurse -Force }
}
