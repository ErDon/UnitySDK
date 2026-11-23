param([Parameter(Mandatory=$true)][string]$ManagedDirectory,
    [Parameter(Mandatory=$true)][string[]]$AssemblyNames,
    [string]$UnityEditor='C:\Program Files\Unity\Hub\Editor\2022.3.14f1c1\Editor')
$ErrorActionPreference='Stop'
$managed=(Resolve-Path -LiteralPath $ManagedDirectory).Path
$output=Join-Path (Split-Path $PSScriptRoot -Parent) '.tests\vendor-retention-tool'
New-Item -ItemType Directory -Force $output | Out-Null
$cecil=Join-Path $UnityEditor 'Data\Tools\ScriptUpdater\Unity.Cecil.dll'
Copy-Item -LiteralPath $cecil -Destination $output
$mono=Join-Path $UnityEditor 'Data\MonoBleedingEdge\bin\mono.exe'
$compiler=Join-Path $UnityEditor 'Data\MonoBleedingEdge\lib\mono\4.5\csc.exe'
$exe=Join-Path $output 'InspectVendorRetention.exe'
& $mono $compiler /nologo "/reference:$cecil" "/out:$exe" (Join-Path $PSScriptRoot 'InspectVendorRetention.cs')
if($LASTEXITCODE -ne 0){throw 'Vendor retention inspector compile failed'}
& $mono $exe $managed ($AssemblyNames -join ',')
if($LASTEXITCODE -ne 0){throw 'Vendor retention inspection failed'}
