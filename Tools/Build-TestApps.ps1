[CmdletBinding()]
param(
    [ValidateSet('Both', 'Quest', 'Tablet')][string]$Role = 'Both',
    [string]$EditorPath,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '../Builds'),
    [switch]$KeepStage
)

$ErrorActionPreference = 'Stop'
$project = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$output = [IO.Directory]::CreateDirectory($OutputDirectory).FullName
$versionFile = Join-Path $project 'ProjectSettings/ProjectVersion.txt'
if (-not $EditorPath) {
    $version = (Select-String -LiteralPath $versionFile -Pattern '^m_EditorVersion: (.+)$').Matches[0].Groups[1].Value.Trim()
    $EditorPath = Join-Path $env:ProgramFiles "Unity/Hub/Editor/$version/Editor/Unity.exe"
}
$editor = (Resolve-Path -LiteralPath $EditorPath).Path
$sdk = Join-Path (Split-Path -Parent $editor) 'Data/PlaybackEngines/AndroidPlayer/SDK'
$aapt = Get-ChildItem -LiteralPath (Join-Path $sdk 'build-tools') -Directory | Sort-Object Name -Descending |
    ForEach-Object { Join-Path $_.FullName 'aapt.exe' } | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $aapt) { throw 'Install Android SDK/build tools with this Unity Editor to verify the APK.' }

function Copy-FromProject([string]$RelativePath, [string]$Stage) {
    $target = Join-Path $Stage $RelativePath
    $null = [IO.Directory]::CreateDirectory((Split-Path -Parent $target))
    Copy-Item -LiteralPath (Join-Path $project $RelativePath) -Destination $target -Recurse
}

function New-TabletProject([string]$Stage) {
    # The tablet uses Unity Transport and the demo rendering code. A clean project
    # gives it an ordinary Android launcher and omits headset-only native plugins.
    foreach ($item in @('ProjectSettings/ProjectVersion.txt', 'ProjectSettings/ProjectSettings.asset', 'Packages/com.blacktheon.body-tracking',
        'Assets/BodyTrackingDemo/Runtime', 'Assets/BodyTrackingDemo/Editor',
        'Assets/BodyTrackingDemo/Scenes/Tablet.unity', 'Assets/BodyTrackingDemo/Scenes/Tablet.unity.meta')) {
        Copy-FromProject $item $Stage
    }
    foreach ($item in @('Assets/BodyTrackingCharacters', 'Assets/BodyTrackingCharacters.meta')) {
        if (Test-Path -LiteralPath (Join-Path $project $item)) { Copy-FromProject $item $Stage }
    }
    $lock = (Get-Content -Raw -LiteralPath (Join-Path $project 'Packages/packages-lock.json') | ConvertFrom-Json).dependencies
    $vrnet = $lock.'com.dsta.vrnet'
    if ($vrnet.source -ne 'git' -or $vrnet.hash -notmatch '^[0-9a-f]{40}$') { throw 'Missing VRNet Git revision in project lockfile.' }
    $deps = [ordered]@{'com.dsta.vrnet' = $vrnet.version.Split('#')[0] + '#' + $vrnet.hash}
    foreach ($id in @('com.unity.inputsystem', 'com.unity.netcode.gameobjects', 'com.unity.test-framework',
        'com.unity.ugui', 'com.unity.modules.animation', 'com.unity.modules.audio', 'com.unity.modules.imgui', 'com.unity.modules.physics',
        'com.unity.modules.uielements', 'com.unity.modules.unitywebrequest', 'com.unity.modules.xr')) {
        $entry = $lock.$id
        if (-not $entry.version) { throw "Missing $id version in project lockfile." }
        $deps[$id] = $entry.version
    }
    $json = @{dependencies = $deps} | ConvertTo-Json
    [IO.File]::WriteAllText((Join-Path $Stage 'Packages/manifest.json'), $json)
}

function Confirm-Apk([string]$Path, [string]$Variant) {
    $info = (& $aapt dump badging $Path) -join "`n"
    $expected = "package: name='com.blacktheon.bodytracking." + $Variant.ToLowerInvariant() + "'"
    if ($LASTEXITCODE -ne 0 -or !$info.Contains($expected) -or !$info.Contains("native-code: 'arm64-v8a'") -or
        !$info.Contains("uses-permission: name='android.permission.INTERNET'")) { throw "Invalid $Variant APK identity, ABI or network permission." }
    $xml = (& $aapt dump xmltree $Path AndroidManifest.xml) -join "`n"
    if ($LASTEXITCODE -ne 0 -or ($xml.Contains('com.oculus.intent.category.VR') -ne ($Variant -eq 'Quest')) -or
        (($info -match "uses-feature: name='android.hardware.vr.headtracking'") -ne ($Variant -eq 'Quest'))) {
        throw "Incorrect Android launcher or headset requirements in $Variant APK."
    }
    Write-Output "Verified $Path"
}

$variants = if ($Role -eq 'Both') { @('Quest', 'Tablet') } else { @($Role) }
foreach ($variant in $variants) {
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\', '/')
    $stage = [IO.Directory]::CreateDirectory((Join-Path $tempRoot ('bodytracking-' + [guid]::NewGuid().ToString('N').Substring(0, 10)))).FullName
    $log = Join-Path $output "BodyTracking-$variant-build.log"
    $apk = Join-Path $output "BodyTracking-$variant.apk"
    $complete = $false
    try {
        if ($variant -eq 'Tablet') {
            New-TabletProject $stage
        } else {
            foreach ($dir in 'Assets', 'Packages', 'ProjectSettings') { Copy-FromProject $dir $stage }
            # The editor Assistant starts its network relay even in batch mode.
            $packagesPath = Join-Path $stage 'Packages/manifest.json'
            $packages = Get-Content -Raw -LiteralPath $packagesPath | ConvertFrom-Json
            if ($packages.dependencies.PSObject.Properties['com.unity.ai.assistant']) {
                $packages.dependencies.PSObject.Properties.Remove('com.unity.ai.assistant')
                [IO.File]::WriteAllText($packagesPath, ($packages | ConvertTo-Json -Depth 100))
            }
            $manifestPath = Join-Path $stage 'Assets/Plugins/Android/AndroidManifest.xml'
            [xml]$androidManifest = Get-Content -Raw -LiteralPath $manifestPath
            $null = $androidManifest.manifest.application.SetAttribute('usesCleartextTraffic', 'http://schemas.android.com/apk/res/android', 'true')
            $androidManifest.Save($manifestPath)
        }
        [IO.File]::WriteAllText((Join-Path $stage '.bodytracking-build-stage'), $variant)
        $argsLine = '-batchmode -nographics -quit -buildTarget Android -projectPath "{0}" -executeMethod Blacktheon.BodyTracking.Demo.Editor.BodyTrackingBuild.Build{1} -bodyTrackingOutput "{2}" -logFile "{3}"' -f $stage,$variant,$apk,$log
        $process = Start-Process -FilePath $editor -ArgumentList $argsLine -WindowStyle Hidden -Wait -PassThru
        if ($process.ExitCode -ne 0) { throw "Unity exited with $($process.ExitCode). See $log." }
        Confirm-Apk $apk $variant
        $complete = $true
    } finally {
        if ($complete -and !$KeepStage -and
            (Split-Path -Parent (Resolve-Path -LiteralPath $stage).Path) -ieq $tempRoot -and
            (Split-Path -Leaf $stage) -match '^bodytracking-[0-9a-f]{10}$') {
            Remove-Item -LiteralPath $stage -Recurse -Force
        } else { Write-Output "Build project at $stage" }
    }
}
