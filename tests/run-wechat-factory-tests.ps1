param([Parameter(Mandatory=$true)][string]$OfficialPackagePath,
    [Parameter(Mandatory=$true)][string]$CompiledWxAssembly,
    [string]$UnityMono='C:\Program Files\Unity\Hub\Editor\2022.3.14f1c1\Editor\Data\MonoBleedingEdge\bin\mono.exe',
    [string]$NetStandardFacade="${env:ProgramFiles(x86)}\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.7.1\Facades\netstandard.dll")
$ErrorActionPreference='Stop'
Push-Location (Split-Path $PSScriptRoot -Parent)
try {
    $metadata=Get-Content (Join-Path $OfficialPackagePath 'package.json') -Raw | ConvertFrom-Json
    if($metadata.name -ne 'com.qq.weixin.minigame' -or $metadata.version -ne '0.1.23'){throw 'Expected official com.qq.weixin.minigame 0.1.23'}
    $dll=Join-Path $OfficialPackagePath 'Runtime\Plugins\wx-runtime-editor.dll'
    New-Item -ItemType Directory -Force '.tests\wechat-factory' | Out-Null
    Copy-Item -LiteralPath $dll -Destination '.tests\wechat-factory'
    $compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    Copy-Item -LiteralPath $CompiledWxAssembly -Destination '.tests\wechat-factory'
    $coreSources=@(Get-ChildItem 'Packages\com.erdon.unitysdk\Runtime' -Filter '*.cs' | Where-Object {$_.Name -ne 'UnitySdkHost.cs'} | ForEach-Object {$_.FullName})
    $platformSources=@(Get-ChildItem 'Packages\com.erdon.unitysdk.wechat\Runtime' -Filter '*.cs' | ForEach-Object {$_.FullName})
    & $compiler /nologo "/reference:$dll" "/reference:$CompiledWxAssembly" "/reference:$NetStandardFacade" /out:.tests\wechat-factory\WeChatFactoryTests.exe $coreSources $platformSources .\tests\WeChatFactoryTests.cs
    if($LASTEXITCODE -ne 0){throw 'WeChat factory compile failed'}
    & $UnityMono .\.tests\wechat-factory\WeChatFactoryTests.exe
    if($LASTEXITCODE -ne 0){throw 'WeChat factory tests failed'}
    $litJson=Join-Path $OfficialPackagePath 'Runtime\Plugins\LitJson.dll'
    Copy-Item -LiteralPath $litJson -Destination '.tests\wechat-factory'
    & $compiler /nologo "/reference:$dll" "/reference:$CompiledWxAssembly" "/reference:$NetStandardFacade" "/reference:$litJson" /out:.tests\wechat-factory\WeChatAnalyticsTests.exe $coreSources $platformSources .\tests\WeChatAnalyticsTests.cs
    if($LASTEXITCODE -ne 0){throw 'WeChat analytics compile failed'}
    & $UnityMono .\.tests\wechat-factory\WeChatAnalyticsTests.exe
    if($LASTEXITCODE -ne 0){throw 'WeChat analytics tests failed'}
    & $compiler /nologo "/reference:$dll" "/reference:$CompiledWxAssembly" "/reference:$NetStandardFacade" /out:.tests\wechat-factory\WeChatAdsTests.exe $coreSources $platformSources .\tests\WeChatAdsTests.cs
    if($LASTEXITCODE -ne 0){throw 'WeChat ads compile failed'}
    & $UnityMono .\.tests\wechat-factory\WeChatAdsTests.exe
    if($LASTEXITCODE -ne 0){throw 'WeChat ads tests failed'}
    & $compiler /nologo "/reference:$dll" "/reference:$CompiledWxAssembly" "/reference:$NetStandardFacade" /out:.tests\wechat-factory\WeChatRuntimeTests.exe $coreSources $platformSources .\tests\WeChatRuntimeTests.cs
    if($LASTEXITCODE -ne 0){throw 'WeChat runtime compile failed'}
    & $UnityMono .\.tests\wechat-factory\WeChatRuntimeTests.exe
    if($LASTEXITCODE -ne 0){throw 'WeChat runtime tests failed'}
} finally {Pop-Location}
