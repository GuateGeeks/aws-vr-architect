param(
    [string]$Apk,
    [string]$Output,
    [string]$AndroidTools = 'C:/Program Files/Unity/Hub/Editor/6000.6.3f1/Editor/Data/PlaybackEngines/AndroidPlayer'
)
$ErrorActionPreference = 'Stop'
$questProject = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not $Apk) { $Apk = Join-Path $questProject 'Builds/Quest3/GuateGeeksAWSVR-Quest3.apk' }
if (-not $Output) { $Output = Join-Path $questProject 'Builds/Quest3/GuateGeeksAWSVR-Quest3-0.21.0-meta.apk' }
$questSigning = Join-Path $questProject 'Builds/Signing'
$questKey = Join-Path $questSigning 'guategeeks-quest-release.keystore'
$questPasswordFile = Join-Path $questSigning 'keystore-password.dpapi'
if (-not (Test-Path -LiteralPath $questKey) -or -not (Test-Path -LiteralPath $questPasswordFile)) {
    throw 'Existing distribution key and encrypted password are required. Do not generate a replacement key for updates.'
}
$questSecurePassword = (Get-Content -LiteralPath $questPasswordFile -Raw).Trim() | ConvertTo-SecureString
$questCredential = [PSCredential]::new('quest-signing', $questSecurePassword)
$questJava = Join-Path $AndroidTools 'OpenJDK/bin/java.exe'
$questBuildTools = Get-ChildItem (Join-Path $AndroidTools 'SDK/build-tools') -Directory |
    Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
$questApksigner = Join-Path $questBuildTools.FullName 'lib/apksigner.jar'
if (Test-Path Env:QUEST_META_SIGNING_PASSWORD) { throw 'Signing environment variable is already in use.' }
try {
    $env:QUEST_META_SIGNING_PASSWORD = $questCredential.GetNetworkCredential().Password
    & $questJava -jar $questApksigner sign --ks $questKey --ks-key-alias guategeeks-quest `
        --ks-pass env:QUEST_META_SIGNING_PASSWORD --key-pass env:QUEST_META_SIGNING_PASSWORD `
        --out $Output $Apk
    if ($LASTEXITCODE -ne 0) { throw 'Distribution signing failed.' }
    & $questJava -jar $questApksigner verify --verbose --print-certs $Output
    if ($LASTEXITCODE -ne 0) { throw 'Distribution signature verification failed.' }
    Get-FileHash -LiteralPath $Output -Algorithm SHA256
} finally {
    Remove-Item Env:QUEST_META_SIGNING_PASSWORD -ErrorAction SilentlyContinue
    $questCredential = $null
}
