param([Parameter(Mandatory=$true)][string]$OfficialPackagePath,
    [Parameter(Mandatory=$true)][string]$CompiledWxAssembly,
    [string]$NetStandardFacade="${env:ProgramFiles(x86)}\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.7.1\Facades\netstandard.dll")
$ErrorActionPreference='Stop'
Push-Location (Split-Path $PSScriptRoot -Parent)
try {
    $metadata=Get-Content (Join-Path $OfficialPackagePath 'package.json') -Raw | ConvertFrom-Json
    if($metadata.name -ne 'com.qq.weixin.minigame' -or $metadata.version -ne '0.1.23'){throw 'Expected official com.qq.weixin.minigame 0.1.23'}
    $dll=Join-Path $OfficialPackagePath 'Runtime\Plugins\wx-runtime-editor.dll'
    New-Item -ItemType Directory -Force '.tests\wechat-share' | Out-Null
    Copy-Item -LiteralPath $dll -Destination '.tests\wechat-share'
    $compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    & $compiler /nologo "/reference:$dll" "/reference:$CompiledWxAssembly" "/reference:$NetStandardFacade" /out:.tests\wechat-share\WeChatShareTests.exe .\Packages\com.erdon.unitysdk\Runtime\SDK.cs .\Packages\com.erdon.unitysdk.wechat\Runtime\WeChatShare.cs .\tests\WeChatShareTests.cs
    if($LASTEXITCODE -ne 0){throw 'WeChat share compile failed'}
    & .\.tests\wechat-share\WeChatShareTests.exe
    if($LASTEXITCODE -ne 0){throw 'WeChat share tests failed'}
} finally {Pop-Location}
