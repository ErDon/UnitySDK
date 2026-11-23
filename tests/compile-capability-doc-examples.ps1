param(
    [string]$UnityData = 'C:\Program Files\Unity\Hub\Editor\2022.3.14f1c1\Editor\Data',
    [string]$CoreAssembly = '.tests/UnityProject/Library/ScriptAssemblies/ErDon.UnitySDK.dll'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    $mono = Join-Path $UnityData 'MonoBleedingEdge/bin/mono.exe'
    $compiler = Join-Path $UnityData 'MonoBleedingEdge/lib/mono/4.5/csc.exe'
    $standard = Join-Path $UnityData 'NetStandard/ref/2.1.0/netstandard.dll'
    $engine = Join-Path $UnityData 'Managed/UnityEngine/UnityEngine.CoreModule.dll'
    foreach ($inputFile in @($mono, $compiler, $standard, $engine, $CoreAssembly)) {
        if (!(Test-Path -LiteralPath $inputFile -PathType Leaf)) { throw "Missing compilation input: $inputFile" }
    }
    $output = New-Item -ItemType Directory -Force '.tests/documentation-examples'
    foreach ($name in @('share', 'storage', 'rewarded-ads', 'analytics')) {
        $markdown = Get-Content -Raw -Encoding UTF8 "docs/$name.md"
        $match = [regex]::Match($markdown, '(?s)```csharp\r?\n(.*?)\r?\n```')
        if (!$match.Success) { throw "Missing first C# example: $name" }
        $body = $match.Groups[1].Value
        # These examples use namespace imports followed by statements for a game method.
        $imports = [regex]::Matches($body, '(?m)^using [\w.]+;\r?$')
        $header = ($imports | ForEach-Object { $_.Value }) -join "`n"
        $body = [regex]::Replace($body, '(?m)^using [\w.]+;\r?\n', '')
        $source = Join-Path $output.FullName "$name.cs"
        Set-Content -LiteralPath $source -Encoding UTF8 -Value "$header`npublic static class DocumentationExample { public static void Run() {`n$body`n} }"
        & $mono $compiler /nologo /target:library /nostdlib /warnaserror "/reference:$standard" "/reference:$engine" "/reference:$CoreAssembly" "/out:$($output.FullName)/$name.dll" $source
        if ($LASTEXITCODE -ne 0) { throw "Documentation example compilation failed: $name" }
        Write-Output "DOCUMENTATION_COMPILE_PASS $name"
    }
} finally { Pop-Location }
