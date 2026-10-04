param([string]$Serial = '2G0YC5ZG9J06XL')
$ErrorActionPreference = 'Stop'
$android = 'C:/Program Files/Unity/Hub/Editor/6000.6.3f1/Editor/Data/PlaybackEngines/AndroidPlayer'
$sdk = Join-Path $android 'SDK'
$build = Join-Path $sdk 'build-tools/36.0.0'
$java = Join-Path $android 'OpenJDK/bin'
$jar = Join-Path $sdk 'platforms/android-36/android.jar'
$adb = Join-Path $sdk 'platform-tools/adb.exe'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$output = Join-Path $root 'Validation/VaultAndroidTest'
New-Item -ItemType Directory -Force -Path "$output/classes", "$output/dex" | Out-Null
@'
<manifest xmlns:android="http://schemas.android.com/apk/res/android" package="com.guategeeks.vaultvalidation" android:versionCode="1" android:versionName="1">
<uses-sdk android:minSdkVersion="32" android:targetSdkVersion="36"/>
<application android:label="GuateGeeks Vault Validation" android:allowBackup="false"/>
<instrumentation android:name="com.guategeeks.vaultvalidation.VaultInstrumentation" android:targetPackage="com.guategeeks.vaultvalidation"/>
</manifest>
'@ | Set-Content -LiteralPath "$output/AndroidManifest.xml"
& "$java/javac.exe" -encoding UTF-8 -source 8 -target 8 -classpath $jar -d "$output/classes" "$root/Assets/Plugins/Android/CredentialVault.java" "$PSScriptRoot/VaultInstrumentation.java"
if ($LASTEXITCODE) { throw 'Java compilation failed' }
$classes = @(Get-ChildItem -LiteralPath "$output/classes" -Recurse -Filter '*.class' | ForEach-Object FullName)
& "$java/java.exe" -cp "$build/lib/d8.jar" com.android.tools.r8.D8 --lib $jar --min-api 32 --output "$output/dex" $classes
if ($LASTEXITCODE) { throw 'DEX compilation failed' }
& "$build/aapt2.exe" link -I $jar --manifest "$output/AndroidManifest.xml" -o "$output/unsigned.apk"
if ($LASTEXITCODE) { throw 'Test packaging failed' }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::Open("$output/unsigned.apk", [IO.Compression.ZipArchiveMode]::Update)
try { [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, "$output/dex/classes.dex", 'classes.dex') | Out-Null } finally { $zip.Dispose() }
& "$build/zipalign.exe" -f 4 "$output/unsigned.apk" "$output/aligned.apk"
if ($LASTEXITCODE) { throw 'Alignment failed' }
& "$java/java.exe" -jar "$build/lib/apksigner.jar" sign --ks "$env:USERPROFILE/.android/debug.keystore" --ks-pass pass:android --out "$output/test.apk" "$output/aligned.apk"
if ($LASTEXITCODE) { throw 'Test signing failed' }
& $adb -s $Serial install -r -t "$output/test.apk"
if ($LASTEXITCODE) { throw 'Test install failed' }
try {
    $result = (& $adb -s $Serial shell am instrument -w com.guategeeks.vaultvalidation/com.guategeeks.vaultvalidation.VaultInstrumentation | Out-String)
    $result | Set-Content -LiteralPath "$root/Validation/android-vault-tests.txt"
    Write-Output $result
    if ($result -notmatch 'PASS: real Android Keystore') { throw 'Vault device tests failed' }
} finally { & $adb -s $Serial uninstall com.guategeeks.vaultvalidation }
