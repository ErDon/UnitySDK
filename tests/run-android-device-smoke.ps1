param(
    [Parameter(Mandatory=$true)][string]$Serial,
    [string]$AndroidSdk = 'C:/Program Files/Unity/Hub/Editor/2022.3.14f1c1/Editor/Data/PlaybackEngines/AndroidPlayer/SDK',
    [string]$Apk = '.tests/android-smoke/SDKSmoke.apk'
)
$ErrorActionPreference='Stop'
Push-Location (Split-Path $PSScriptRoot -Parent)
$installed=$false
try {
    $adb=Join-Path $AndroidSdk 'platform-tools/adb.exe'
    $aapt=Get-ChildItem (Join-Path $AndroidSdk 'build-tools') -Filter aapt.exe -Recurse | Select-Object -First 1
    if(!$aapt -or !(Test-Path $Apk)){throw 'Android build-tools or smoke APK missing'}
    $badging=& $aapt.FullName dump badging $Apk
    if($LASTEXITCODE -ne 0 -or !($badging -match "^package: name='com.erdon.sdk.smoke'")){throw 'Refusing to install an APK outside the dedicated SDK smoke package'}
    & $adb -s $Serial install -r $Apk
    if($LASTEXITCODE -ne 0){throw 'Smoke APK install failed'}
    $installed=$true
    for($run=1;$run -le 2;$run++) {
        # Only restart our fixture. Do not clear data or the device-wide log buffer.
        & $adb -s $Serial shell am force-stop com.erdon.sdk.smoke
        & $adb -s $Serial shell am start -n com.erdon.sdk.smoke/com.unity3d.player.UnityPlayerActivity
        if($LASTEXITCODE -ne 0){throw 'Smoke APK start failed'}
        $deadline=[DateTime]::UtcNow.AddSeconds(30);$passed=$false
        do {
            Start-Sleep -Milliseconds 500
            $deviceProcess=(& $adb -s $Serial shell pidof com.erdon.sdk.smoke)
            if(!$deviceProcess){continue}
            $lines=& $adb -s $Serial logcat -d "--pid=$($deviceProcess.Trim())"
            $lines | Set-Content -Encoding UTF8 ".tests/android-device-run-$run.log"
            if($lines -match 'ANDROID_DEVICE_FAIL:'){throw "Device fixture failed; see run-$run log"}
            if($lines -match 'ANDROID_DEVICE_JNI_PASS') {
                if($run -eq 2 -and !($lines -match 'ANDROID_DEVICE_PERSISTENCE_PASS')){throw 'Cold restart did not preserve storage'}
                $passed=$true;break
            }
        }while([DateTime]::UtcNow -lt $deadline)
        if(!$passed){throw "Device fixture timeout on run $run"}
        Write-Output "ANDROID_DEVICE_RUN_PASS $run"
    }
}finally{
    if($installed){& $adb -s $Serial shell am force-stop com.erdon.sdk.smoke}
    Pop-Location
}
