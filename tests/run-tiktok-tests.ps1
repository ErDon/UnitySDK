param([Parameter(Mandatory=$true)][string]$OfficialPluginPath,
    [string]$NetStandardFacade="${env:ProgramFiles(x86)}\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.7.1\Facades\netstandard.dll")
$ErrorActionPreference='Stop'
Push-Location (Split-Path $PSScriptRoot -Parent)
try {
    $metadata=Get-Content (Join-Path $OfficialPluginPath 'package.json') -Raw | ConvertFrom-Json
    if($metadata.version -ne '1.1.6-Release'){throw 'Expected TikTok plugin 1.1.6-Release'}
    New-Item -ItemType Directory -Force '.tests\tiktok' | Out-Null
    $compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    foreach($binary in @('ttsdk.dll','ttsdk-editor.dll')) {
        $dll=Join-Path $OfficialPluginPath ('TTSDK\'+$binary)
        Copy-Item -LiteralPath $dll -Destination '.tests\tiktok'
        & $compiler /nologo "/reference:$dll" "/reference:$NetStandardFacade" /out:.tests\tiktok\TikTokCredentialTests.exe .\Packages\com.erdon.unitysdk\Runtime\SDK.cs .\Packages\com.erdon.unitysdk\Runtime\VerifiedAuth.cs .\Packages\com.erdon.unitysdk.tiktok\Runtime\TikTokCredentialProvider.cs .\tests\TikTokCredentialTests.cs
        if($LASTEXITCODE -ne 0){throw "TikTok compile failed: $binary"}
        & .\.tests\tiktok\TikTokCredentialTests.exe
        if($LASTEXITCODE -ne 0){throw "TikTok tests failed: $binary"}
        & $compiler /nologo "/reference:$dll" "/reference:$NetStandardFacade" /out:.tests\tiktok\TikTokStorageTests.exe .\Packages\com.erdon.unitysdk\Runtime\SDK.cs .\Packages\com.erdon.unitysdk.tiktok\Runtime\TikTokStorage.cs .\tests\TikTokStorageTests.cs
        if($LASTEXITCODE -ne 0){throw "TikTok storage compile failed: $binary"}
        & .\.tests\tiktok\TikTokStorageTests.exe
        if($LASTEXITCODE -ne 0){throw "TikTok storage tests failed: $binary"}
    }
} finally {Pop-Location}
