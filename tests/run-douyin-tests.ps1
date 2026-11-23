param([Parameter(Mandatory=$true)][string]$OfficialPluginPath,
    [string]$UnityMono='C:\Program Files\Unity\Hub\Editor\2022.3.14f1c1\Editor\Data\MonoBleedingEdge\bin\mono.exe',
    [string]$NetStandardFacade="${env:ProgramFiles(x86)}\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.7.1\Facades\netstandard.dll")
$ErrorActionPreference='Stop'
Push-Location (Split-Path $PSScriptRoot -Parent)
try {
    $metadata=Get-Content (Join-Path $OfficialPluginPath 'package.json') -Raw | ConvertFrom-Json
    if($metadata.name -ne 'com.bytedance.starksdk' -or $metadata.version -ne '6.9.7'){throw 'Expected official Douyin TTSDK 6.9.7'}
    $dll=Join-Path $OfficialPluginPath 'ttsdk.dll'
    New-Item -ItemType Directory -Force '.tests\douyin' | Out-Null
    Copy-Item -LiteralPath $dll -Destination '.tests\douyin'
    $compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    & $compiler /nologo /define:OFFICIAL_DOUYIN "/reference:$dll" "/reference:$NetStandardFacade" /out:.tests\douyin\DouyinCredentialTests.exe .\Packages\com.erdon.unitysdk\Runtime\SDK.cs .\Packages\com.erdon.unitysdk\Runtime\VerifiedAuth.cs .\Packages\com.erdon.unitysdk\Runtime\Orders.cs .\Packages\com.erdon.unitysdk\Runtime\VerifiedPayment.cs .\Packages\com.erdon.unitysdk.douyin\Runtime\DouyinCredentialProvider.cs .\tests\DouyinCredentialTests.cs
    if($LASTEXITCODE -ne 0){throw 'Douyin official binary compile failed'}
    & $UnityMono .\.tests\douyin\DouyinCredentialTests.exe
    if($LASTEXITCODE -ne 0){throw 'Douyin official binary contract tests failed'}
    & $compiler /nologo "/reference:$dll" "/reference:$NetStandardFacade" /out:.tests\douyin\DouyinStorageTests.exe .\Packages\com.erdon.unitysdk\Runtime\SDK.cs .\Packages\com.erdon.unitysdk\Runtime\Orders.cs .\Packages\com.erdon.unitysdk\Runtime\VerifiedPayment.cs .\Packages\com.erdon.unitysdk.douyin\Runtime\DouyinStorage.cs .\tests\DouyinStorageTests.cs
    if($LASTEXITCODE -ne 0){throw 'Douyin storage compile failed'}
    & $UnityMono .\.tests\douyin\DouyinStorageTests.exe
    if($LASTEXITCODE -ne 0){throw 'Douyin storage tests failed'}
} finally {Pop-Location}
