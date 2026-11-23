param([Parameter(Mandatory=$true)][string]$OfficialPackagePath,
    [Parameter(Mandatory=$true)][string]$CompiledWxAssembly,
    [string]$UnityMono='C:\Program Files\Unity\Hub\Editor\2022.3.14f1c1\Editor\Data\MonoBleedingEdge\bin\mono.exe',
    [string]$NetStandardFacade="${env:ProgramFiles(x86)}\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.7.1\Facades\netstandard.dll")
$ErrorActionPreference='Stop'
Push-Location (Split-Path $PSScriptRoot -Parent)
try {
    $metadata=Get-Content (Join-Path $OfficialPackagePath 'package.json') -Raw | ConvertFrom-Json
    if($metadata.name -ne 'com.qq.weixin.minigame' -or $metadata.version -ne '0.1.1'){throw 'Expected separately pinned string-analytics SDK baseline 0.1.1'}
    $output='.tests\wechat-string-analytics'
    New-Item -ItemType Directory -Force $output | Out-Null
    $dll=Join-Path $OfficialPackagePath 'Runtime\Plugins\wx-runtime-editor.dll'
    Copy-Item -LiteralPath $dll,$CompiledWxAssembly -Destination $output
    $compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    $sources=@(Get-ChildItem 'Packages\com.erdon.unitysdk\Runtime' -Filter '*.cs' | Where-Object {$_.Name -ne 'UnitySdkHost.cs'} | ForEach-Object {$_.FullName})
    & $compiler /nologo /define:ERDON_WECHAT_STRING_ANALYTICS "/reference:$dll" "/reference:$CompiledWxAssembly" "/reference:$NetStandardFacade" "/out:$output\WeChatStringAnalyticsTests.exe" $sources .\Packages\com.erdon.unitysdk.wechat\Runtime\WeChatAnalytics.cs .\tests\WeChatStringAnalyticsTests.cs
    if($LASTEXITCODE -ne 0){throw 'String analytics compile failed'}
    & $UnityMono "$output\WeChatStringAnalyticsTests.exe"
    if($LASTEXITCODE -ne 0){throw 'String analytics tests failed'}
} finally {Pop-Location}
