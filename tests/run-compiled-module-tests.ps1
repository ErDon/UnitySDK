param([Parameter(Mandatory=$true)][string]$OfficialPluginPath)
$ErrorActionPreference='Stop'
Push-Location (Split-Path $PSScriptRoot -Parent)
try {
 $out=Join-Path (Get-Location) '.tests/compiled-modules'
 New-Item -ItemType Directory -Force $out | Out-Null
 $plugin=(Resolve-Path $OfficialPluginPath).Path
 $dll=Join-Path $plugin 'TTSDK/ttsdk.dll'
 $json=Join-Path $plugin 'LitJson/TTLitJson.dll'
 Copy-Item $dll,$json -Destination $out
 $editor='C:\Program Files\Unity\Hub\Editor\2022.3.14f1c1\Editor'
 $mono=Join-Path $editor 'Data/MonoBleedingEdge/bin/mono.exe'
 $csc=Join-Path $editor 'Data/MonoBleedingEdge/lib/mono/4.5/csc.exe'
 $sources=@(Get-ChildItem Packages/com.erdon.unitysdk/Runtime/*.cs | Where-Object Name -ne 'UnitySdkHost.cs' | ForEach-Object FullName)
 $sources+=@(Get-ChildItem Packages/com.erdon.unitysdk.tiktok/Runtime/*.cs | ForEach-Object FullName)
 & $mono $csc /nologo /define:ERDON_EXCLUDE_LOGIN,ERDON_EXCLUDE_STORAGE,ERDON_EXCLUDE_SHARE,ERDON_EXCLUDE_ADS,ERDON_EXCLUDE_ANALYTICS "/reference:$dll" "/reference:${env:ProgramFiles(x86)}\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.7.1\Facades\netstandard.dll" "/reference:$json" "/out:$out/CompiledModuleTests.exe" $sources tests/CompiledModuleTests.cs
 if($LASTEXITCODE -ne 0){throw 'Excluded capability compilation failed'}
 & $mono "$out/CompiledModuleTests.exe"
 if($LASTEXITCODE -ne 0){throw 'Excluded capability tests failed'}
} finally {Pop-Location}

