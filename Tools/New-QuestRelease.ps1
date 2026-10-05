param(
    [string]$Unity = 'C:/Program Files/Unity/Hub/Editor/6000.6.3f1/Editor/Unity.exe',
    [string]$Backend = (Join-Path $PSScriptRoot '../../GuateGeeksAWS2026'),
    [string]$Python,
    [int]$TimeoutMinutes = 40
)
$ErrorActionPreference = 'Stop'
$project = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$backendRoot = (Resolve-Path $Backend).Path
$validation = Join-Path $project 'Validation'
$command = Join-Path $validation 'editor-command.txt'
if (-not (Test-Path -LiteralPath $Unity)) { throw 'Install the pinned Unity editor and Android modules first.' }
if ((Get-Content (Join-Path $project 'ProjectSettings/ProjectVersion.txt') -Raw) -notmatch 'm_EditorVersion: 6000.6.3f1') { throw 'Unexpected Unity version.' }
if ((Get-Item -LiteralPath $Unity).VersionInfo.ProductVersion -notmatch '^6000\.6\.3') { throw 'The supplied editor must be Unity 6000.6.3f1.' }
if (-not (Get-Process Unity -ErrorAction SilentlyContinue)) { throw 'Open this project in Unity 6000.6.3f1, exit Play Mode, and retry.' }
if (Test-Path -LiteralPath $command) { throw 'An editor command is already queued.' }
New-Item -ItemType Directory -Path $validation -Force | Out-Null
$lockPath = Join-Path $validation 'release.lock'
$releaseLock = [IO.File]::Open($lockPath, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
function Invoke-EditorCommand([string]$Name, [string]$Report, [string]$Expected) {
    $started = [DateTime]::UtcNow
    Set-Content -LiteralPath $command -Value $Name -Encoding ascii
    $reportPath = Join-Path $validation $Report
    $deadline = $started.AddMinutes($TimeoutMinutes)
    while ([DateTime]::UtcNow -lt $deadline) {
        Start-Sleep -Seconds 2
        $errorPath = Join-Path $validation 'command-error.txt'
        if ((Test-Path $errorPath) -and (Get-Item $errorPath).LastWriteTimeUtc -ge $started) { throw (Get-Content $errorPath -Raw) }
        if ((Test-Path $reportPath) -and (Get-Item $reportPath).LastWriteTimeUtc -ge $started) {
            $result = Get-Content $reportPath -Raw
            if ($result -match 'PENDING:') { continue }
            if ($result -notmatch $Expected) { throw "$Name failed: $result" }
            Write-Output "$Name verified"
            return
        }
    }
    throw "Timed out waiting for $Name. Check Unity's Console and Validation reports before retrying."
}
function Get-CodeFingerprint {
    $files = @((Get-ChildItem (Join-Path $project 'Assets/GuateGeeks') -Recurse -File), (Get-ChildItem (Join-Path $backendRoot 'src') -Filter '*.py' -File)) | ForEach-Object { $_ }
    return (($files | Where-Object Extension -ne '.meta' | Sort-Object FullName | ForEach-Object { $_.FullName + ':' + (Get-FileHash -LiteralPath $_.FullName).Hash }) -join "`n")
}
try {
    $sourceBefore = Get-CodeFingerprint
    $backendPython = if ($Python) { (Resolve-Path -LiteralPath $Python).Path } else { Join-Path $backendRoot '.venv/Scripts/python.exe' }
    if (-not (Test-Path $backendPython)) { throw 'Install backend requirements in its .venv first, or pass -Python with a compatible runtime.' }
    Push-Location $backendRoot
    try {
        & $backendPython -m unittest discover -s tests -q *> (Join-Path $validation 'backend-tests.txt')
        if ($LASTEXITCODE -ne 0) { throw 'Backend tests failed; see Validation/backend-tests.txt.' }
        & $backendPython tools/validate_templates.py *> (Join-Path $validation 'backend-templates.txt')
        if ($LASTEXITCODE -ne 0) { throw 'Backend template validation failed.' }
    } finally { Pop-Location }
    Invoke-EditorCommand 'edit-tests' 'EditMode-summary.txt' '^Passed \| passed=[1-9][0-9]* failed=0'
    Invoke-EditorCommand 'play-tests' 'PlayMode-summary.txt' '^Passed \| passed=[1-9][0-9]* failed=0'
    Invoke-EditorCommand 'build-quest' 'quest3-build.txt' 'Succeeded \| errors=0 '
    if ((Get-CodeFingerprint) -cne $sourceBefore) { throw 'Source changed during validation. Rerun before creating a release.' }
    $apk = Join-Path $project 'Builds/Quest3/GuateGeeksAWSVR-Quest3.apk'
    $android = Join-Path (Split-Path $Unity) 'Data/PlaybackEngines/AndroidPlayer'
    $buildTools = Get-ChildItem (Join-Path $android 'SDK/build-tools') -Directory | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
    $apkInfo = & (Join-Path $buildTools.FullName 'aapt.exe') dump badging $apk
    if ($LASTEXITCODE -ne 0) { throw 'APK metadata could not be read.' }
    $info = $apkInfo -join "`n"
    if ($info -notmatch "package: name='com.guategeeks.awsarchitectlab' versionCode='([0-9]+)' versionName='([^']+)'" ) { throw 'Wrong APK package identity.' }
    $versionCode = $Matches[1]; $version = $Matches[2]
    if ($info -notmatch "native-code: 'arm64-v8a'") { throw 'Expected ARM64 APK.' }
    $signature = & (Join-Path $android 'OpenJDK/bin/java.exe') -jar (Join-Path $buildTools.FullName 'lib/apksigner.jar') verify --verbose --print-certs $apk
    if ($LASTEXITCODE -ne 0) { throw 'APK signature verification failed.' }
    $release = Join-Path $project ('Releases/' + $version + '-' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ'))
    New-Item -ItemType Directory -Path $release | Out-Null
    Copy-Item -LiteralPath $apk -Destination $release
    $reports = New-Item -ItemType Directory -Path (Join-Path $release 'Validation')
    foreach ($name in @('EditMode-results.xml','PlayMode-results.xml','EditMode-summary.txt','PlayMode-summary.txt','quest3-preflight.txt','quest3-build.txt','backend-tests.txt','backend-templates.txt')) {
        Copy-Item -LiteralPath (Join-Path $validation $name) -Destination $reports.FullName
    }
    $signature | Set-Content (Join-Path $reports.FullName 'apk-signature.txt')
    $apkInfo | Set-Content (Join-Path $reports.FullName 'apk-metadata.txt')
    # Allowlist excludes machine state, credential profiles, Library, caches and deployment state.
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [IO.Compression.ZipFile]::Open((Join-Path $release 'source.zip'), [IO.Compression.ZipArchiveMode]::Create)
    $inputs = @()
    try {
        foreach ($tree in @(@{Root=$project;Name='GuateGeeksAWSVR';Paths=@('Assets','Packages','ProjectSettings','Tools','Documentation','README.md')}, @{Root=$backendRoot;Name='GuateGeeksAWS2026';Paths=@('src','tools','tests','examples','docs','template.json','requirements-dev.txt','README.md')})) {
            foreach ($relative in $tree.Paths) {
                $path = Join-Path $tree.Root $relative
                if (-not (Test-Path -LiteralPath $path)) { continue }
                foreach ($file in Get-ChildItem -LiteralPath $path -File -Recurse) {
                    if ($file.FullName -match '[\\/]__pycache__[\\/]|\.pyc$') { continue }
                    $entry = $tree.Name + '/' + $file.FullName.Substring($tree.Root.Length + 1).Replace('\','/')
                    [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,$file.FullName,$entry,[IO.Compression.CompressionLevel]::Optimal) | Out-Null
                    $inputs += @{path=$entry;sha256=(Get-FileHash -LiteralPath $file.FullName).Hash.ToLowerInvariant()}
                }
            }
        }
    } finally { $zip.Dispose() }
    $inputs | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $release 'source-hashes.json')
    $artifacts = Get-ChildItem -LiteralPath $release -Recurse -File | ForEach-Object { @{path=$_.FullName.Substring($release.Length+1);bytes=$_.Length;sha256=(Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant()} }
    @{version=$version;versionCode=[int]$versionCode;createdUtc=[DateTime]::UtcNow.ToString('O');unity='6000.6.3f1';platform='Quest 3 / Android ARM64 / Vulkan';signing='local debug certificate';headsetAcceptance='pending';backendDeployment='separate; not performed by release script';artifacts=@($artifacts)} | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $release 'manifest.json')
    Write-Output "Release ready: $release"
} finally { $releaseLock.Dispose() }
