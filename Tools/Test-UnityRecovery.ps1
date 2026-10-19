param([string]$Unity = 'C:/Program Files/Unity/Hub/Editor/6000.6.6f1/Editor/Unity.exe')
$ErrorActionPreference = 'Stop'
$project = Split-Path $PSScriptRoot -Parent
$reports = Join-Path $project 'Recovery/0.24.4'
$captureRelative = 'Recovery/0.24.4/render-checks/' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ')
$env:GG_RECOVERY_CAPTURE_DIR = Join-Path $project $captureRelative
function Invoke-RecoveryUnity([string]$Name, [string[]]$Extra) {
    $arguments = @('-batchmode', '-force-d3d11', '-projectPath', ('"' + $project + '"'), '-logFile', ('"' + (Join-Path $reports ($Name + '.log')) + '"')) + $Extra
    $process = Start-Process -FilePath $Unity -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw "$Name failed (exit $($process.ExitCode)); see its log in $reports." }
}
function Get-RecoveryFingerprint {
    $paths = @('Assets/GuateGeeks', 'Packages', 'ProjectSettings')
    return (($paths | ForEach-Object { Get-ChildItem (Join-Path $project $_) -Recurse -File } |
        Where-Object { $_.Extension -in @('.cs', '.shader', '.json', '.asset', '.txt') } |
        Sort-Object FullName | ForEach-Object { $_.FullName + ':' + (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }) -join "`n")
}
$before = Get-RecoveryFingerprint
foreach ($mode in @('EditMode', 'PlayMode')) {
    $name = 'unity-' + $mode.ToLowerInvariant() + '-verified'
    $result = Join-Path $reports ($name + '.xml')
    if (Test-Path -LiteralPath $result) { Remove-Item -LiteralPath $result }
    Invoke-RecoveryUnity $name @('-runTests', '-testPlatform', $mode, '-testCategory', '!LiveOpenAI', '-testResults', ('"' + $result + '"'))
    [xml]$xml = Get-Content -LiteralPath $result
    if ([int]$xml.'test-run'.failed -ne 0 -or $xml.'test-run'.result -ne 'Passed') { throw "$mode tests failed." }
    Write-Output "$mode passed: $($xml.'test-run'.passed)"
}
Invoke-RecoveryUnity 'desktop-build' @('-quit', '-executeMethod', 'GuateGeeks.AwsVr.Editor.RecoveryValidation.BuildDesktop')
if ($before -cne (Get-RecoveryFingerprint)) { throw 'Source/settings changed during validation; review and rerun.' }
@{ verifiedUtc = [DateTime]::UtcNow.ToString('O'); unity = $Unity; sourceUnchangedDuringValidation = $true;
    tests = @('unity-editmode-verified.xml', 'unity-playmode-verified.xml');
    desktopBuild = 'Builds/Recovery/GuateGeeksAWSVR-Recovered.exe'; quetzalPreview = $captureRelative + '/recovery-quetzal-perch.png';
    headsetAcceptance = 'pending'; originalSourceRecoveredVerbatim = $false } |
    ConvertTo-Json -Depth 4 | Set-Content (Join-Path $reports 'unity-reconstruction-validation.json')
Write-Output 'Unity recovery validation and desktop build complete.'
