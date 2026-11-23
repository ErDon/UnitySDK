param([Parameter(Mandatory=$true)][string]$OfficialPackagePath,
    [string]$NetStandardFacade="${env:ProgramFiles(x86)}\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.7.1\Facades\netstandard.dll")
$ErrorActionPreference='Stop'
Push-Location (Split-Path $PSScriptRoot -Parent)
try {
    $metadata=Get-Content (Join-Path $OfficialPackagePath 'package.json') -Raw | ConvertFrom-Json
    if($metadata.name -ne 'com.qq.weixin.minigame' -or $metadata.version -ne '0.1.23'){throw 'Expected official com.qq.weixin.minigame 0.1.23'}
    $dll=Join-Path $OfficialPackagePath 'Runtime\Plugins\wx-runtime-editor.dll'
    $testDirectory='.tests\wechat'
    New-Item -ItemType Directory -Force $testDirectory | Out-Null
    Copy-Item -LiteralPath $dll -Destination $testDirectory
    $compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    if(!(Test-Path -LiteralPath $NetStandardFacade)){throw 'Provide -NetStandardFacade for the .NET Framework netstandard facade'}
    & $compiler /nologo "/reference:$dll" "/reference:$NetStandardFacade" /out:.tests\wechat\WeChatCredentialTests.exe .\Packages\com.erdon.unitysdk\Runtime\SDK.cs .\Packages\com.erdon.unitysdk\Runtime\VerifiedAuth.cs .\Packages\com.erdon.unitysdk.wechat\Runtime\WeChatCredentialProvider.cs .\tests\WeChatCredentialTests.cs
    if($LASTEXITCODE -ne 0){throw 'WeChat compile failed'}
    & .\.tests\wechat\WeChatCredentialTests.exe
    if($LASTEXITCODE -ne 0){throw 'WeChat tests failed'}
} finally {Pop-Location}
