param([Parameter(Mandatory=$true)][string]$ManagedDirectory,
    [Parameter(Mandatory=$true)][ValidateSet('WeChat','Douyin','TikTok')][string]$Platform,
    [ValidateSet('Login','Storage','Share','Ads','Analytics','Network','Payments')][string[]]$ExcludedCapabilities=@(),
    [string]$UnityEditor='C:\Program Files\Unity\Hub\Editor\2022.3.14f1c1\Editor')
$ErrorActionPreference='Stop'
$managed=(Resolve-Path -LiteralPath $ManagedDirectory).Path
$repo=Split-Path $PSScriptRoot -Parent
$output=Join-Path $repo '.tests\linked-platform-tool'
New-Item -ItemType Directory -Force $output | Out-Null
$cecil=Join-Path $UnityEditor 'Data\Tools\ScriptUpdater\Unity.Cecil.dll'
Copy-Item -LiteralPath $cecil -Destination $output
$mono=Join-Path $UnityEditor 'Data\MonoBleedingEdge\bin\mono.exe'
$compiler=Join-Path $UnityEditor 'Data\MonoBleedingEdge\lib\mono\4.5\csc.exe'
$exe=Join-Path $output 'CheckLinkedPlatform.exe'
& $mono $compiler /nologo "/reference:$cecil" "/out:$exe" (Join-Path $PSScriptRoot 'CheckLinkedPlatform.cs')
if($LASTEXITCODE -ne 0){throw 'Linked-platform inspector compile failed'}
if ($ExcludedCapabilities.Count) { & $mono $exe $managed $Platform ($ExcludedCapabilities -join ',') }
else { & $mono $exe $managed $Platform }
if($LASTEXITCODE -ne 0){throw 'Linked-platform inspection failed'}
