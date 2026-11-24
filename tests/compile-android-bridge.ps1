param(
    [string]$UnityData = 'C:/Program Files/Unity/Hub/Editor/2022.3.14f1c1/Editor/Data',
    [string]$CoreAssembly = '.tests/AndroidBridgeProject/Library/ScriptAssemblies/ErDon.UnitySDK.dll'
)
$ErrorActionPreference='Stop'
Push-Location (Split-Path $PSScriptRoot -Parent)
try {
    if(!(Test-Path $CoreAssembly)){throw 'Compile the current core in Unity first and pass -CoreAssembly'}
    $sources=@(Get-ChildItem Packages/com.erdon.unitysdk.android/Runtime -Filter *.cs | ForEach-Object FullName)
    $compiler=Join-Path $UnityData 'MonoBleedingEdge/lib/mono/4.5/csc.exe'
    $mono=Join-Path $UnityData 'MonoBleedingEdge/bin/mono.exe'
    $references=@(
        "/reference:$UnityData/NetStandard/ref/2.1.0/netstandard.dll",
        "/reference:$UnityData/NetStandard/compat/2.1.0/shims/netfx/mscorlib.dll",
        "/reference:$UnityData/Managed/UnityEngine/UnityEngine.CoreModule.dll",
        "/reference:$UnityData/Managed/UnityEngine/UnityEngine.JSONSerializeModule.dll",
        "/reference:$UnityData/Managed/UnityEngine/UnityEngine.AndroidJNIModule.dll",
        "/reference:$CoreAssembly"
    )
    foreach($mode in @('full','disabled')) {
        $defines='UNITY_5_3_OR_NEWER,UNITY_ANDROID,ERDON_PLATFORM_ANDROID'
        if($mode -eq 'disabled'){$defines+=',ERDON_EXCLUDE_LOGIN,ERDON_EXCLUDE_STORAGE,ERDON_EXCLUDE_SHARE,ERDON_EXCLUDE_ADS,ERDON_EXCLUDE_ANALYTICS,ERDON_EXCLUDE_NETWORK,ERDON_EXCLUDE_PAYMENTS'}
        & $mono $compiler /noconfig /nologo /target:library /nostdlib "/define:$defines" @references "/out:.tests/android-$mode-compile.dll" @sources
        if($LASTEXITCODE -ne 0){throw "Android $mode Player branch compilation failed"}
        Write-Output "ANDROID_PLAYER_COMPILE_PASS $mode (C# compilation only, not APK/device validation)"
    }
} finally {Pop-Location}
