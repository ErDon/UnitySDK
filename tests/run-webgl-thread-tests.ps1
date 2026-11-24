param([string]$UnityEditor='C:\Program Files\Unity\Hub\Editor\2022.3.14f1c1\Editor')
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
$output=Join-Path $repo '.tests\webgl-thread-fixtures'
New-Item -ItemType Directory -Force $output | Out-Null
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$source=Join-Path $output 'Fixture.cs'
$dll=Join-Path $output 'ErDon.UnitySDK.Fixture.dll'
# Compile forbidden calls but never execute them.
@'
using System;
using System.Threading;
using System.Threading.Tasks;
public class Fixture {
 public void Bad() { Task.Run(()=>{}); new Thread(()=>{}).Start(); Thread.Sleep(1); }
 public int Block(Task<int> task) { task.Wait(); return task.Result; }
 public void Timer() { new System.Threading.Timer(_=>{},null,1,1); }
}
'@ | Set-Content -LiteralPath $source -Encoding UTF8
& $compiler /nologo /target:library "/out:$dll" $source
if($LASTEXITCODE -ne 0){throw 'Negative fixture compile failed'}
$rejected=$false
try { & (Join-Path $repo 'tools/check-webgl-threads.ps1') -ManagedDirectory $output -UnityEditor $UnityEditor }
catch { if($_.Exception.Message -ne 'WebGL thread audit failed'){throw}; $rejected=$true }
if(!$rejected){throw 'Threaded fixture was accepted'}
@'
using System.Threading;
using System.Collections.Concurrent;
public class Fixture {
 int state; readonly ConcurrentQueue<int> queue=new ConcurrentQueue<int>();
 public int Safe() { queue.Enqueue(1); Interlocked.Exchange(ref state,1); return Thread.CurrentThread.ManagedThreadId; }
}
'@ | Set-Content -LiteralPath $source -Encoding UTF8
& $compiler /nologo /target:library "/out:$dll" $source
if($LASTEXITCODE -ne 0){throw 'Positive fixture compile failed'}
& (Join-Path $repo 'tools/check-webgl-threads.ps1') -ManagedDirectory $output -UnityEditor $UnityEditor
$android=Join-Path $output 'ErDon.UnitySDK.Android.dll'
& $compiler /nologo /target:library "/out:$android" $source
if($LASTEXITCODE -ne 0){throw 'Android fixture compile failed'}
try {
    $rejected=$false
    try { & (Join-Path $repo 'tools/check-webgl-threads.ps1') -ManagedDirectory $output -UnityEditor $UnityEditor }
    catch { if($_.Exception.Message -ne 'WebGL thread audit failed'){throw}; $rejected=$true }
    if(!$rejected){throw 'Foreign Android assembly was accepted'}
} finally { Remove-Item -LiteralPath $android }
Write-Output 'WEBGL_THREAD_TESTS_PASS'
