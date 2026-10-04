param(
    [string]$Apk = (Join-Path $PSScriptRoot '../Builds/Quest3/GuateGeeksAWSVR-Quest3.apk'),
    [string]$Serial,
    [string]$Adb = 'C:/Program Files/Unity/Hub/Editor/6000.6.3f1/Editor/Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb.exe'
)
$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $Apk -PathType Leaf)) { throw "Primero genera el APK: $Apk" }
if (-not (Test-Path -LiteralPath $Adb -PathType Leaf)) { throw 'Falta adb. Instala Android SDK & NDK Tools con Unity Hub, o pasa -Adb con su ruta.' }
$deviceRows = @(& $Adb devices)
if ($LASTEXITCODE -ne 0) { throw 'No se pudo consultar adb.' }
$ready = @($deviceRows | Where-Object { $_ -match '^\S+\s+device$' } | ForEach-Object { ($_ -split '\s+')[0] })
if ($Serial) {
    if ($ready -notcontains $Serial) { throw 'El visor indicado no está conectado y autorizado para depuración USB.' }
} elseif ($ready.Count -eq 1) {
    $Serial = $ready[0]
} else { throw 'Conecta y autoriza un solo Quest por USB, o indica -Serial. La autorización se acepta dentro del visor.' }
$model = (& $Adb -s $Serial shell getprop ro.product.model | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or $model -notmatch '^Quest 3$') { throw "El dispositivo no es Meta Quest 3: $model" }
& $Adb -s $Serial install -r (Resolve-Path -LiteralPath $Apk).Path
if ($LASTEXITCODE -ne 0) { throw 'Falló la instalación. Revisa el mensaje de adb; no se desinstaló ni se borraron datos automáticamente.' }
$launch = @(& $Adb -s $Serial shell am start -W -n com.guategeeks.awsarchitectlab/com.unity3d.player.UnityPlayerGameActivity)
$launchExitCode = $LASTEXITCODE
$launch | Write-Output
if ($launchExitCode -ne 0 -or ($launch -match '^Error').Count -gt 0) { throw 'APK instalado, pero no se pudo solicitar el arranque. Ábrelo desde Orígenes desconocidos en el visor.' }
if (($launch -match 'LaunchCheckControllerRequiredDialogActivity').Count -gt 0) {
    Write-Host 'Demo instalada. Meta requiere despertar los controles Touch Plus y continuar dentro del visor.'
} else {
    Write-Host 'Demo instalada; apertura solicitada en Meta Quest 3. Comprueba el laboratorio dentro del visor. CONEXIÓN AWS permite configurar la API desde VR y recordar la credencial cifrada opcionalmente.'
}
