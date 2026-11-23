param(
    [string]$UnityData = 'C:\Program Files\Unity\Hub\Editor\2022.3.14f1c1\Editor\Data',
    [string]$CoreAssembly = '.tests/UnityProject/Library/ScriptAssemblies/ErDon.UnitySDK.dll',
    [string]$HttpAssembly = '.tests/UnityProject/Library/ScriptAssemblies/ErDon.UnitySDK.Transport.Http.dll'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    $mono = Join-Path $UnityData 'MonoBleedingEdge/bin/mono.exe'
    $compiler = Join-Path $UnityData 'MonoBleedingEdge/lib/mono/4.5/csc.exe'
    $standard = Join-Path $UnityData 'NetStandard/ref/2.1.0/netstandard.dll'
    $engine = Join-Path $UnityData 'Managed/UnityEngine/UnityEngine.CoreModule.dll'
    $json = Join-Path $UnityData 'Managed/UnityEngine/UnityEngine.JSONSerializeModule.dll'
    foreach ($inputFile in @($mono, $compiler, $standard, $engine, $json, $CoreAssembly, $HttpAssembly)) {
        if (!(Test-Path -LiteralPath $inputFile -PathType Leaf)) { throw "Missing compilation input: $inputFile" }
    }
    $output = New-Item -ItemType Directory -Force '.tests/documentation-examples'
    foreach ($name in @('share', 'storage', 'rewarded-ads', 'analytics', 'authentication', 'network', 'payment-pipeline', 'api-reference', 'configuration')) {
        $markdown = Get-Content -Raw -Encoding UTF8 "docs/$name.md"
        $matches = [regex]::Matches($markdown, '(?s)```csharp\r?\n(.*?)\r?\n```')
        if (!$matches.Count) { throw "Missing C# examples: $name" }
        $index = 0
        foreach ($match in $matches) {
            $index++
            $body = $match.Groups[1].Value
            # These examples use namespace imports followed by statements for a game method.
            $imports = [regex]::Matches($body, '(?m)^using [\w.]+;\r?$')
            $header = ($imports | ForEach-Object { $_.Value }) -join "`n"
            $body = [regex]::Replace($body, '(?m)^using [\w.]+;\r?\n', '')
            # Dependencies named in the payment examples are supplied by the game integration.
            $parameters = if ($name -eq 'payment-pipeline') { 'ErDon.UnitySDK.IOrderClient orderClient, ErDon.UnitySDK.IPaymentLauncher platformLauncher, System.Action<System.Action> mainThreadPost, string gameSession, string savedPurchaseRequestId' } else { '' }
            $source = Join-Path $output.FullName "$name-$index.cs"
            $fields = ''
            if ($name -eq 'configuration') {
                if ($index -in @(2, 3)) { $parameters = 'UnityEngine.TextAsset configAsset' }
                if ($index -eq 4) { $fields = 'ErDon.UnitySDK.INetwork network; void OnDestroy() { network?.Dispose(); }' }
            }
            # The first configuration example is a complete factory type, not method statements.
            $compilationUnit = if ($name -eq 'configuration' -and $index -eq 1) { "$header`n$body" } else {
                "$header`npublic class DocumentationExample : UnityEngine.MonoBehaviour { $fields public void Run($parameters) {`n$body`n} }"
            }
            Set-Content -LiteralPath $source -Encoding UTF8 -Value $compilationUnit
            & $mono $compiler /nologo /target:library /nostdlib /warnaserror "/reference:$standard" "/reference:$engine" "/reference:$json" "/reference:$CoreAssembly" "/reference:$HttpAssembly" "/out:$($output.FullName)/$name-$index.dll" $source
            if ($LASTEXITCODE -ne 0) { throw "Documentation example compilation failed: $name" }
            Write-Output "DOCUMENTATION_COMPILE_PASS $name-$index"
        }
    }
} finally { Pop-Location }
