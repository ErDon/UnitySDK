param([Parameter(Mandatory=$true)][string]$CompiledWxAssembly,
    [string]$NetStandardFacade="${env:ProgramFiles(x86)}\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.7.1\Facades\netstandard.dll")
$ErrorActionPreference='Stop'
Push-Location (Split-Path $PSScriptRoot -Parent)
try {
    if(!(Test-Path -LiteralPath $CompiledWxAssembly)){throw 'Provide Wx.dll compiled by an official-SDK Unity project'}
    New-Item -ItemType Directory -Force '.tests\wechat-storage' | Out-Null
    $compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    & $compiler /nologo "/reference:$CompiledWxAssembly" "/reference:$NetStandardFacade" /out:.tests\wechat-storage\WeChatStorageTests.exe .\Packages\com.erdon.unitysdk\Runtime\SDK.cs .\Packages\com.erdon.unitysdk.wechat\Runtime\WeChatStorage.cs .\tests\WeChatStorageTests.cs
    if($LASTEXITCODE -ne 0){throw 'WeChat storage compile failed'}
    & .\.tests\wechat-storage\WeChatStorageTests.exe
    if($LASTEXITCODE -ne 0){throw 'WeChat storage tests failed'}
} finally {Pop-Location}
