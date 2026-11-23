$ErrorActionPreference='Stop'
Push-Location (Split-Path $PSScriptRoot -Parent)
try {
    & ./tests/run-core-tests.ps1
    $compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    & $compiler /nologo /out:.tests\LifecycleTests.exe .\Packages\com.erdon.unitysdk\Runtime\SDK.cs .\tests\LifecycleTests.cs
    if($LASTEXITCODE -ne 0){throw 'Lifecycle compile failed'}
    & .\.tests\LifecycleTests.exe
    if($LASTEXITCODE -ne 0){throw 'Lifecycle tests failed'}
    & $compiler /nologo /out:.tests\SessionResponseTests.exe .\Packages\com.erdon.unitysdk\Runtime\SDK.cs .\Packages\com.erdon.unitysdk.transport.http\Runtime\SessionResponsePolicy.cs .\tests\SessionResponseTests.cs
    if($LASTEXITCODE -ne 0){throw 'Response policy compile failed'}
    & .\.tests\SessionResponseTests.exe
    if($LASTEXITCODE -ne 0){throw 'Response policy tests failed'}
    & $compiler /nologo /out:.tests\AuthPipelineTests.exe .\Packages\com.erdon.unitysdk\Runtime\SDK.cs .\Packages\com.erdon.unitysdk\Runtime\VerifiedAuth.cs .\tests\AuthPipelineTests.cs
    if($LASTEXITCODE -ne 0){throw 'Auth compile failed'}
    & .\.tests\AuthPipelineTests.exe
    if($LASTEXITCODE -ne 0){throw 'Auth tests failed'}
    & $compiler /nologo /out:.tests\IsolationTests.exe .\Packages\com.erdon.unitysdk\Editor\PlatformIsolationPolicy.cs .\tests\IsolationTests.cs
    if($LASTEXITCODE -ne 0){throw 'Isolation compile failed'}
    & .\.tests\IsolationTests.exe
    if($LASTEXITCODE -ne 0){throw 'Isolation tests failed'}
} finally {Pop-Location}
