param([Parameter(Mandatory=$true)][string]$OfficialPluginPath,
    [string]$UnityMono='C:\Program Files\Unity\Hub\Editor\2022.3.14f1c1\Editor\Data\MonoBleedingEdge\bin\mono.exe',
    [string]$NetStandardFacade="${env:ProgramFiles(x86)}\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.7.1\Facades\netstandard.dll")
$ErrorActionPreference='Stop'
Push-Location (Split-Path $PSScriptRoot -Parent)
try {
    $metadata=Get-Content (Join-Path $OfficialPluginPath 'package.json') -Raw | ConvertFrom-Json
    if($metadata.version -ne '1.1.6-Release'){throw 'Expected TikTok plugin 1.1.6-Release'}
    if(!(Test-Path -LiteralPath $UnityMono)){throw 'Provide -UnityMono pointing to the Unity Mono runtime for netstandard 2.1 share callbacks'}
    New-Item -ItemType Directory -Force '.tests\tiktok' | Out-Null
    $litJson=Join-Path $OfficialPluginPath 'LitJson\TTLitJson.dll'
    Copy-Item -LiteralPath $litJson -Destination '.tests\tiktok'
    $compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    foreach($binary in @('ttsdk.dll','ttsdk-editor.dll')) {
        $dll=Join-Path $OfficialPluginPath ('TTSDK\'+$binary)
        Copy-Item -LiteralPath $dll -Destination '.tests\tiktok'
        & $compiler /nologo "/reference:$dll" "/reference:$litJson" "/reference:$NetStandardFacade" /out:.tests\tiktok\TikTokShareTests.exe .\Packages\com.erdon.unitysdk\Runtime\SDK.cs .\Packages\com.erdon.unitysdk.tiktok\Runtime\TikTokShare.cs .\tests\TikTokShareTests.cs .\Packages\com.erdon.unitysdk\Runtime\Orders.cs .\Packages\com.erdon.unitysdk\Runtime\VerifiedPayment.cs .\Packages\com.erdon.unitysdk\Runtime\RewardedAds.cs .\Packages\com.erdon.unitysdk\Runtime\Network.cs .\Packages\com.erdon.unitysdk\Runtime\SDKNetwork.cs .\Packages\com.erdon.unitysdk\Runtime\Analytics.cs .\Packages\com.erdon.unitysdk\Runtime\SDKAnalytics.cs
        if($LASTEXITCODE -ne 0){throw "TikTok share compile failed: $binary"}
        & $UnityMono .\.tests\tiktok\TikTokShareTests.exe
        if($LASTEXITCODE -ne 0){throw "TikTok share tests failed: $binary"}
        & $compiler /nologo "/reference:$dll" "/reference:$litJson" "/reference:$NetStandardFacade" /out:.tests\tiktok\TikTokCredentialTests.exe .\Packages\com.erdon.unitysdk\Runtime\SDK.cs .\Packages\com.erdon.unitysdk\Runtime\VerifiedAuth.cs .\Packages\com.erdon.unitysdk.tiktok\Runtime\TikTokCredentialProvider.cs .\tests\TikTokCredentialTests.cs .\Packages\com.erdon.unitysdk\Runtime\Orders.cs .\Packages\com.erdon.unitysdk\Runtime\VerifiedPayment.cs .\Packages\com.erdon.unitysdk\Runtime\RewardedAds.cs .\Packages\com.erdon.unitysdk\Runtime\Network.cs .\Packages\com.erdon.unitysdk\Runtime\SDKNetwork.cs .\Packages\com.erdon.unitysdk\Runtime\Analytics.cs .\Packages\com.erdon.unitysdk\Runtime\SDKAnalytics.cs
        if($LASTEXITCODE -ne 0){throw "TikTok compile failed: $binary"}
        & .\.tests\tiktok\TikTokCredentialTests.exe
        if($LASTEXITCODE -ne 0){throw "TikTok tests failed: $binary"}
        & $compiler /nologo "/reference:$dll" "/reference:$litJson" "/reference:$NetStandardFacade" /out:.tests\tiktok\TikTokStorageTests.exe .\Packages\com.erdon.unitysdk\Runtime\SDK.cs .\Packages\com.erdon.unitysdk.tiktok\Runtime\TikTokStorage.cs .\tests\TikTokStorageTests.cs .\Packages\com.erdon.unitysdk\Runtime\Orders.cs .\Packages\com.erdon.unitysdk\Runtime\VerifiedPayment.cs .\Packages\com.erdon.unitysdk\Runtime\RewardedAds.cs .\Packages\com.erdon.unitysdk\Runtime\Network.cs .\Packages\com.erdon.unitysdk\Runtime\SDKNetwork.cs .\Packages\com.erdon.unitysdk\Runtime\Analytics.cs .\Packages\com.erdon.unitysdk\Runtime\SDKAnalytics.cs
        if($LASTEXITCODE -ne 0){throw "TikTok storage compile failed: $binary"}
        & .\.tests\tiktok\TikTokStorageTests.exe
        if($LASTEXITCODE -ne 0){throw "TikTok storage tests failed: $binary"}
        $coreSources=@(Get-ChildItem 'Packages\com.erdon.unitysdk\Runtime' -Filter '*.cs' | Where-Object {$_.Name -ne 'UnitySdkHost.cs'} | ForEach-Object {$_.FullName})
        $platformSources=@(Get-ChildItem 'Packages\com.erdon.unitysdk.tiktok\Runtime' -Filter '*.cs' | ForEach-Object {$_.FullName})
        & $compiler /nologo "/reference:$dll" "/reference:$litJson" "/reference:$NetStandardFacade" /out:.tests\tiktok\TikTokPaymentGatewayTests.exe $coreSources $platformSources .\tests\TikTokPaymentGatewayTests.cs
        if($LASTEXITCODE -ne 0){throw "TikTok payment gateway compile failed: $binary"}
        & $UnityMono .\.tests\tiktok\TikTokPaymentGatewayTests.exe
        if($LASTEXITCODE -ne 0){throw "TikTok payment gateway tests failed: $binary"}
        & $compiler /nologo "/reference:$dll" "/reference:$litJson" "/reference:$NetStandardFacade" /out:.tests\tiktok\TikTokFactoryTests.exe $coreSources $platformSources .\tests\TikTokFactoryTests.cs
        if($LASTEXITCODE -ne 0){throw "TikTok factory compile failed: $binary"}
        & $UnityMono .\.tests\tiktok\TikTokFactoryTests.exe
        if($LASTEXITCODE -ne 0){throw "TikTok factory tests failed: $binary"}
        & $compiler /nologo "/reference:$dll" "/reference:$litJson" "/reference:$NetStandardFacade" /out:.tests\tiktok\TikTokAnalyticsTests.exe $coreSources $platformSources .\tests\TikTokAnalyticsTests.cs
        if($LASTEXITCODE -ne 0){throw "TikTok analytics compile failed: $binary"}
        & $UnityMono .\.tests\tiktok\TikTokAnalyticsTests.exe
        if($LASTEXITCODE -ne 0){throw "TikTok analytics tests failed: $binary"}
        & $compiler /nologo "/reference:$dll" "/reference:$litJson" "/reference:$NetStandardFacade" /out:.tests\tiktok\TikTokAdsTests.exe $coreSources $platformSources .\tests\TikTokAdsTests.cs
        if($LASTEXITCODE -ne 0){throw "TikTok ads compile failed: $binary"}
        & $UnityMono .\.tests\tiktok\TikTokAdsTests.exe
        if($LASTEXITCODE -ne 0){throw "TikTok ads tests failed: $binary"}
        & $compiler /nologo "/reference:$dll" "/reference:$litJson" "/reference:$NetStandardFacade" /out:.tests\tiktok\TikTokRuntimeTests.exe $coreSources $platformSources .\tests\TikTokRuntimeTests.cs
        if($LASTEXITCODE -ne 0){throw "TikTok runtime compile failed: $binary"}
        & $UnityMono .\.tests\tiktok\TikTokRuntimeTests.exe
        if($LASTEXITCODE -ne 0){throw "TikTok runtime tests failed: $binary"}
    }
} finally {Pop-Location}
