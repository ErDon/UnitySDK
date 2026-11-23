$ErrorActionPreference = 'Stop'
Push-Location (Split-Path $PSScriptRoot -Parent)
try {
    New-Item -ItemType Directory -Force .tests | Out-Null
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    & $compiler /nologo /out:.tests\CoreTests.exe .\Packages\com.erdon.unitysdk\Runtime\SDK.cs .\tests\CoreTests.cs .\Packages\com.erdon.unitysdk\Runtime\Orders.cs .\Packages\com.erdon.unitysdk\Runtime\VerifiedPayment.cs
    if ($LASTEXITCODE -ne 0) { throw 'Core compilation failed' }
    & .\.tests\CoreTests.exe
    if ($LASTEXITCODE -ne 0) { throw 'Core tests failed' }
} finally { Pop-Location }
