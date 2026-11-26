[CmdletBinding()]
param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '../../Builds/SlimeVR'),
    [string]$JavaHome = $env:JAVA_HOME,
    [switch]$Paired,
    [string]$SourceDirectory
)

$ErrorActionPreference = 'Stop'
$revision = 'c11dc54ed1a745ffc4b15db7f820d8d0c1993990'
$tag = 'v21.1.0'
$upstream = 'https://github.com/SlimeVR/SlimeVR-Server.git'
$output = [IO.Directory]::CreateDirectory($OutputDirectory).FullName
$sourceName = if ($Paired) { 'paired-source' } else { 'upstream-source' }
$source = if ($SourceDirectory) { [IO.Path]::GetFullPath($SourceDirectory) } else { Join-Path $output $sourceName }
$patch = Join-Path $PSScriptRoot 'paired-feet-v21.1.0.patch'

if (-not $JavaHome) {
    $editor = Get-Content -LiteralPath (Join-Path $PSScriptRoot '../../ProjectSettings/ProjectVersion.txt') |
        Select-String '^m_EditorVersion: (.+)$' | Select-Object -First 1
    $javaHomeCandidate = Join-Path $env:ProgramFiles ('Unity/Hub/Editor/{0}/Editor/Data/PlaybackEngines/AndroidPlayer/OpenJDK' -f $editor.Matches[0].Groups[1].Value.Trim())
    if (Test-Path -LiteralPath (Join-Path $javaHomeCandidate 'bin/javac.exe')) { $JavaHome = $javaHomeCandidate }
}
if (-not $JavaHome -or -not (Test-Path -LiteralPath (Join-Path $JavaHome 'bin/javac.exe'))) {
    throw 'Set JAVA_HOME or pass -JavaHome with the location of a JDK 17 installation.'
}

function Invoke-Checked([string]$Program, [string[]]$Arguments) {
    & $Program @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Program exited with code $LASTEXITCODE." }
}

if (-not (Test-Path -LiteralPath $source)) {
    Invoke-Checked git @('-c', 'core.longpaths=true', 'clone', '--config', 'core.longpaths=true', '--branch', $tag, '--depth', '1', '--recurse-submodules',
        '--shallow-submodules', $upstream, $source)
}
$actualRevision = Invoke-Checked git @('-C', $source, 'rev-parse', 'HEAD')
if ($actualRevision -cne $revision) { throw "Expected upstream revision $revision at $source." }
$dirty = Invoke-Checked git @('-C', $source, 'status', '--porcelain', '--untracked-files=all')
if ($dirty) {
    if (-not $Paired) { throw "Local changes in stock source at $source. Supply a clean pinned checkout." }
    # Compare the whole worktree with the exact patch using a temporary index.
    # This neither stages user files nor discards changes on repeat builds.
    $previousIndex = $env:GIT_INDEX_FILE
    $index = Join-Path ([IO.Path]::GetTempPath()) ('feet-build-index-' + [Guid]::NewGuid())
    try {
        $env:GIT_INDEX_FILE = $index
        Invoke-Checked git @('-C', $source, 'read-tree', 'HEAD')
        Invoke-Checked git @('-C', $source, 'apply', '--cached', $patch)
        & git -C $source diff --quiet --ignore-submodules=none
        if ($LASTEXITCODE -ne 0 -or (Invoke-Checked git @('-C', $source, 'ls-files', '--others', '--exclude-standard'))) {
            throw "Changes at $source differ from the published paired patch. Supply a separate checkout."
        }
    } finally {
        $env:GIT_INDEX_FILE = $previousIndex
        if (Test-Path -LiteralPath $index) { Remove-Item -LiteralPath $index }
    }
} elseif ($Paired) {
    Invoke-Checked git @('-C', $source, 'apply', '--check', $patch)
    Invoke-Checked git @('-C', $source, 'apply', $patch)
}
Invoke-Checked git @('-C', $source, 'submodule', 'update', '--init', '--recursive')
if ((Invoke-Checked git @('-C', $source, 'submodule', 'status', '--recursive')) -match '^[-+U]') {
    throw "A submodule at $source differs from its pinned revision."
}

$previousJavaHome = $env:JAVA_HOME
$previousAndroidHome = $env:ANDROID_HOME
Push-Location -LiteralPath $source
try {
    $env:JAVA_HOME = $JavaHome
    $unitySdk = Join-Path (Split-Path -Parent $JavaHome) 'SDK'
    if (-not $env:ANDROID_HOME -and (Test-Path -LiteralPath $unitySdk)) { $env:ANDROID_HOME = $unitySdk }
    Invoke-Checked '.\gradlew.bat' @('--console=plain', '--init-script', (Join-Path $PSScriptRoot 'PinnedDesktop.gradle'),
        ':server:desktop:shadowJar', ':server:core:test')
} finally {
    $env:JAVA_HOME = $previousJavaHome
    $env:ANDROID_HOME = $previousAndroidHome
    Pop-Location
}

$variant = if ($Paired) { 'PairedJar' } else { 'Stock' }
$jarOutput = [IO.Directory]::CreateDirectory((Join-Path $output $variant)).FullName
Copy-Item -LiteralPath (Join-Path $source 'server/desktop/build/libs/slimevr.jar') -Destination $jarOutput -Force
foreach ($notice in 'LICENSE-MIT', 'LICENSE-APACHE', 'TRADEMARK.md') {
    # Upstream stores these UTF-8 files with LF. Preserve their canonical contents
    # regardless of the checkout's core.autocrlf setting.
    $noticeText = [IO.File]::ReadAllText((Join-Path $source $notice)).Replace("`r`n", "`n")
    [IO.File]::WriteAllText((Join-Path $jarOutput $notice), $noticeText, [Text.UTF8Encoding]::new($false))
}
Write-Output "Built $variant $tag at $jarOutput\slimevr.jar"
