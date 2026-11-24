param([Parameter(Mandatory=$true)][string]$WeChatPackage)
$ErrorActionPreference='Stop'
$repo=Split-Path $PSScriptRoot -Parent
$vendor=(Resolve-Path -LiteralPath $WeChatPackage).Path.Replace('\','/')
if(!(Test-Path -LiteralPath "$vendor/package.json")){throw 'Provide the official WeChat UPM directory'}
$project=Join-Path $repo '.tests/SecondGameWebGLProject'
if(Test-Path -LiteralPath $project){throw 'Independent project already exists; reuse it instead of overwriting'}
New-Item -ItemType Directory -Force "$project/Assets/Editor","$project/Packages","$project/ProjectSettings" | Out-Null
$deps=[ordered]@{}
foreach($name in @('imageconversion','jsonserialize','assetbundle','unitywebrequest','unitywebrequestassetbundle','audio')){$deps["com.unity.modules.$name"]='1.0.0'}
$deps['com.unity.ugui']='1.0.0'
foreach($name in @('com.erdon.unitysdk','com.erdon.unitysdk.wechat','com.erdon.unitysdk.transport.http','com.erdon.unitysdk.android')){$deps[$name]="file:../../../Packages/$name"}
$deps['com.qq.weixin.minigame']="file:$vendor"
@{dependencies=$deps} | ConvertTo-Json -Depth 5 | Set-Content "$project/Packages/manifest.json" -Encoding UTF8
'm_EditorVersion: 2022.3.14f1c1' | Set-Content "$project/ProjectSettings/ProjectVersion.txt" -Encoding UTF8
Copy-Item "$PSScriptRoot/MiniGameBuildProbe.cs.txt" "$project/Assets/MiniGameBuildProbe.cs"
Copy-Item "$PSScriptRoot/MiniGameDevelopmentBuild.cs.txt" "$project/Assets/Editor/MiniGameDevelopmentBuild.cs"
Copy-Item "$PSScriptRoot/SecondGameBuildChecks.cs.txt" "$project/Assets/Editor/SecondGameBuildChecks.cs"
Write-Output "Prepared $project; run Unity batchmode WebGL -executeMethod SecondGameBuildChecks.Build"
