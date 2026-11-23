$ErrorActionPreference='Stop'
Push-Location (Split-Path $PSScriptRoot -Parent)
try {
    & ./tests/run-inventory-tests.ps1
    & ./tests/run-core-tests.ps1
    $compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    & $compiler /nologo /out:.tests\NetworkContractTests.exe .\Packages\com.erdon.unitysdk\Runtime\Network.cs .\Packages\com.erdon.unitysdk\Runtime\SDK.cs .\Packages\com.erdon.unitysdk\Runtime\Orders.cs .\Packages\com.erdon.unitysdk\Runtime\VerifiedPayment.cs .\Packages\com.erdon.unitysdk\Runtime\RewardedAds.cs .\Packages\com.erdon.unitysdk.transport.http\Runtime\BoundedResponseBuffer.cs .\tests\NetworkContractTests.cs .\Packages\com.erdon.unitysdk\Runtime\SDKNetwork.cs .\Packages\com.erdon.unitysdk\Runtime\Analytics.cs .\Packages\com.erdon.unitysdk\Runtime\SDKAnalytics.cs
    if($LASTEXITCODE -ne 0){throw 'Network contract compile failed'}
    & .\.tests\NetworkContractTests.exe
    if($LASTEXITCODE -ne 0){throw 'Network contract tests failed'}
    $registryCore=@(Get-ChildItem 'Packages/com.erdon.unitysdk/Runtime' -Filter '*.cs' | Where-Object {$_.Name -ne 'UnitySdkHost.cs'} | ForEach-Object {$_.FullName})
    & $compiler /nologo /out:.tests\AnalyticsContractTests.exe $registryCore .\tests\AnalyticsContractTests.cs
    if($LASTEXITCODE -ne 0){throw 'Analytics contract compile failed'}
    & .\.tests\AnalyticsContractTests.exe
    if($LASTEXITCODE -ne 0){throw 'Analytics contract tests failed'}
    & $compiler /nologo /out:.tests\AnalyticsFacadeTests.exe $registryCore .\tests\AnalyticsFacadeTests.cs
    if($LASTEXITCODE -ne 0){throw 'Analytics facade compile failed'}
    & .\.tests\AnalyticsFacadeTests.exe
    if($LASTEXITCODE -ne 0){throw 'Analytics facade tests failed'}
    & $compiler /nologo /out:.tests\AnalyticsAsyncTests.exe $registryCore .\tests\AnalyticsAsyncTests.cs
    if($LASTEXITCODE -ne 0){throw 'Async analytics compile failed'}
    & .\.tests\AnalyticsAsyncTests.exe
    if($LASTEXITCODE -ne 0){throw 'Async analytics tests failed'}
    & $compiler /nologo /out:.tests\NetworkFacadeTests.exe $registryCore .\tests\NetworkFacadeTests.cs
    if($LASTEXITCODE -ne 0){throw 'Network facade compile failed'}
    & .\.tests\NetworkFacadeTests.exe
    if($LASTEXITCODE -ne 0){throw 'Network facade tests failed'}
    & $compiler /nologo /out:.tests\PlatformRegistryTests.exe $registryCore .\tests\PlatformRegistryTests.cs
    if($LASTEXITCODE -ne 0){throw 'Platform registry compile failed'}
    & .\.tests\PlatformRegistryTests.exe
    if($LASTEXITCODE -ne 0){throw 'Platform registry tests failed'}
    & $compiler /nologo /out:.tests\RewardedFacadeTests.exe .\Packages\com.erdon.unitysdk\Runtime\SDK.cs .\Packages\com.erdon.unitysdk\Runtime\Orders.cs .\Packages\com.erdon.unitysdk\Runtime\VerifiedPayment.cs .\Packages\com.erdon.unitysdk\Runtime\RewardedAds.cs .\tests\RewardedFacadeTests.cs .\Packages\com.erdon.unitysdk\Runtime\Network.cs .\Packages\com.erdon.unitysdk\Runtime\SDKNetwork.cs .\Packages\com.erdon.unitysdk\Runtime\Analytics.cs .\Packages\com.erdon.unitysdk\Runtime\SDKAnalytics.cs
    if($LASTEXITCODE -ne 0){throw 'Rewarded facade compile failed'}
    & .\.tests\RewardedFacadeTests.exe
    if($LASTEXITCODE -ne 0){throw 'Rewarded facade tests failed'}
    & $compiler /nologo /out:.tests\StartupTests.exe .\Packages\com.erdon.unitysdk\Runtime\SDK.cs .\Packages\com.erdon.unitysdk\Runtime\Orders.cs .\Packages\com.erdon.unitysdk\Runtime\VerifiedPayment.cs .\Packages\com.erdon.unitysdk\Runtime\IPlatformRuntime.cs .\Packages\com.erdon.unitysdk\Runtime\SdkStartup.cs .\tests\StartupTests.cs .\Packages\com.erdon.unitysdk\Runtime\RewardedAds.cs .\Packages\com.erdon.unitysdk\Runtime\Network.cs .\Packages\com.erdon.unitysdk\Runtime\SDKNetwork.cs .\Packages\com.erdon.unitysdk\Runtime\Analytics.cs .\Packages\com.erdon.unitysdk\Runtime\SDKAnalytics.cs
    if($LASTEXITCODE -ne 0){throw 'Startup compile failed'}
    & .\.tests\StartupTests.exe
    if($LASTEXITCODE -ne 0){throw 'Startup tests failed'}
    & $compiler /nologo /out:.tests\RuntimeConfigurationTests.exe .\Packages\com.erdon.unitysdk\Runtime\SDK.cs .\Packages\com.erdon.unitysdk\Runtime\Orders.cs .\Packages\com.erdon.unitysdk\Runtime\VerifiedPayment.cs .\Packages\com.erdon.unitysdk\Runtime\SdkRuntimeConfiguration.cs .\tests\RuntimeConfigurationTests.cs .\Packages\com.erdon.unitysdk\Runtime\RewardedAds.cs .\Packages\com.erdon.unitysdk\Runtime\Network.cs .\Packages\com.erdon.unitysdk\Runtime\SDKNetwork.cs .\Packages\com.erdon.unitysdk\Runtime\Analytics.cs .\Packages\com.erdon.unitysdk\Runtime\SDKAnalytics.cs
    if($LASTEXITCODE -ne 0){throw 'Runtime configuration compile failed'}
    & .\.tests\RuntimeConfigurationTests.exe
    if($LASTEXITCODE -ne 0){throw 'Runtime configuration tests failed'}
    & $compiler /nologo /out:.tests\ResultSnapshotTests.exe .\Packages\com.erdon.unitysdk\Runtime\SDK.cs .\Packages\com.erdon.unitysdk\Runtime\Orders.cs .\Packages\com.erdon.unitysdk\Runtime\VerifiedPayment.cs .\tests\ResultSnapshotTests.cs .\Packages\com.erdon.unitysdk\Runtime\RewardedAds.cs .\Packages\com.erdon.unitysdk\Runtime\Network.cs .\Packages\com.erdon.unitysdk\Runtime\SDKNetwork.cs .\Packages\com.erdon.unitysdk\Runtime\Analytics.cs .\Packages\com.erdon.unitysdk\Runtime\SDKAnalytics.cs
    if($LASTEXITCODE -ne 0){throw 'Result snapshot compile failed'}
    & .\.tests\ResultSnapshotTests.exe
    if($LASTEXITCODE -ne 0){throw 'Result snapshot tests failed'}
    & $compiler /nologo /out:.tests\DispatchTests.exe .\Packages\com.erdon.unitysdk\Runtime\SDK.cs .\Packages\com.erdon.unitysdk\Runtime\Orders.cs .\Packages\com.erdon.unitysdk\Runtime\VerifiedPayment.cs .\tests\DispatchTests.cs .\Packages\com.erdon.unitysdk\Runtime\RewardedAds.cs .\Packages\com.erdon.unitysdk\Runtime\Network.cs .\Packages\com.erdon.unitysdk\Runtime\SDKNetwork.cs .\Packages\com.erdon.unitysdk\Runtime\Analytics.cs .\Packages\com.erdon.unitysdk\Runtime\SDKAnalytics.cs
    if($LASTEXITCODE -ne 0){throw 'Dispatch compile failed'}
    & .\.tests\DispatchTests.exe
    if($LASTEXITCODE -ne 0){throw 'Dispatch tests failed'}
    & $compiler /nologo /out:.tests\PayTests.exe .\Packages\com.erdon.unitysdk\Runtime\SDK.cs .\Packages\com.erdon.unitysdk\Runtime\Orders.cs .\Packages\com.erdon.unitysdk\Runtime\VerifiedPayment.cs .\tests\PayTests.cs .\Packages\com.erdon.unitysdk\Runtime\RewardedAds.cs .\Packages\com.erdon.unitysdk\Runtime\Network.cs .\Packages\com.erdon.unitysdk\Runtime\SDKNetwork.cs .\Packages\com.erdon.unitysdk\Runtime\Analytics.cs .\Packages\com.erdon.unitysdk\Runtime\SDKAnalytics.cs
    if($LASTEXITCODE -ne 0){throw "Pay compile failed"}
    & .\.tests\PayTests.exe
    if($LASTEXITCODE -ne 0){throw "Pay tests failed"}
    & $compiler /nologo /out:.tests\PaymentPipelineTests.exe .\Packages\com.erdon.unitysdk\Runtime\SDK.cs .\Packages\com.erdon.unitysdk\Runtime\Orders.cs .\Packages\com.erdon.unitysdk\Runtime\VerifiedPayment.cs .\tests\PaymentPipelineTests.cs .\Packages\com.erdon.unitysdk\Runtime\RewardedAds.cs .\Packages\com.erdon.unitysdk\Runtime\Network.cs .\Packages\com.erdon.unitysdk\Runtime\SDKNetwork.cs .\Packages\com.erdon.unitysdk\Runtime\Analytics.cs .\Packages\com.erdon.unitysdk\Runtime\SDKAnalytics.cs
    if($LASTEXITCODE -ne 0){throw 'Payment pipeline compile failed'}
    & .\.tests\PaymentPipelineTests.exe
    if($LASTEXITCODE -ne 0){throw 'Payment pipeline tests failed'}
    & $compiler /nologo /out:.tests\OrderResponseTests.exe .\Packages\com.erdon.unitysdk\Runtime\SDK.cs .\Packages\com.erdon.unitysdk\Runtime\Orders.cs .\Packages\com.erdon.unitysdk.transport.http\Runtime\OrderResponsePolicy.cs .\tests\OrderResponseTests.cs .\Packages\com.erdon.unitysdk\Runtime\VerifiedPayment.cs .\Packages\com.erdon.unitysdk\Runtime\RewardedAds.cs .\Packages\com.erdon.unitysdk\Runtime\Network.cs .\Packages\com.erdon.unitysdk\Runtime\SDKNetwork.cs .\Packages\com.erdon.unitysdk\Runtime\Analytics.cs .\Packages\com.erdon.unitysdk\Runtime\SDKAnalytics.cs
    if($LASTEXITCODE -ne 0){throw 'Order response compile failed'}
    & .\.tests\OrderResponseTests.exe
    if($LASTEXITCODE -ne 0){throw 'Order response tests failed'}
    & $compiler /nologo /out:.tests\ShareTests.exe .\Packages\com.erdon.unitysdk\Runtime\SDK.cs .\tests\ShareTests.cs .\Packages\com.erdon.unitysdk\Runtime\Orders.cs .\Packages\com.erdon.unitysdk\Runtime\VerifiedPayment.cs .\Packages\com.erdon.unitysdk\Runtime\RewardedAds.cs .\Packages\com.erdon.unitysdk\Runtime\Network.cs .\Packages\com.erdon.unitysdk\Runtime\SDKNetwork.cs .\Packages\com.erdon.unitysdk\Runtime\Analytics.cs .\Packages\com.erdon.unitysdk\Runtime\SDKAnalytics.cs
    if($LASTEXITCODE -ne 0){throw 'Share compile failed'}
    & .\.tests\ShareTests.exe
    if($LASTEXITCODE -ne 0){throw 'Share tests failed'}
    & $compiler /nologo /out:.tests\FactoryTests.exe .\Packages\com.erdon.unitysdk\Runtime\SDK.cs .\tests\FactoryTests.cs .\Packages\com.erdon.unitysdk\Runtime\Orders.cs .\Packages\com.erdon.unitysdk\Runtime\VerifiedPayment.cs .\Packages\com.erdon.unitysdk\Runtime\RewardedAds.cs .\Packages\com.erdon.unitysdk\Runtime\Network.cs .\Packages\com.erdon.unitysdk\Runtime\SDKNetwork.cs .\Packages\com.erdon.unitysdk\Runtime\Analytics.cs .\Packages\com.erdon.unitysdk\Runtime\SDKAnalytics.cs
    if($LASTEXITCODE -ne 0){throw 'Factory compile failed'}
    & .\.tests\FactoryTests.exe
    if($LASTEXITCODE -ne 0){throw 'Factory tests failed'}
    & $compiler /nologo /out:.tests\DouyinCredentialTests.exe .\Packages\com.erdon.unitysdk\Runtime\SDK.cs .\Packages\com.erdon.unitysdk\Runtime\VerifiedAuth.cs .\Packages\com.erdon.unitysdk.douyin\Runtime\DouyinCredentialProvider.cs .\tests\DouyinCredentialTests.cs .\Packages\com.erdon.unitysdk\Runtime\Orders.cs .\Packages\com.erdon.unitysdk\Runtime\VerifiedPayment.cs .\Packages\com.erdon.unitysdk\Runtime\RewardedAds.cs .\Packages\com.erdon.unitysdk\Runtime\Network.cs .\Packages\com.erdon.unitysdk\Runtime\SDKNetwork.cs .\Packages\com.erdon.unitysdk\Runtime\Analytics.cs .\Packages\com.erdon.unitysdk\Runtime\SDKAnalytics.cs
    if($LASTEXITCODE -ne 0){throw 'Douyin documented-contract compile failed'}
    & .\.tests\DouyinCredentialTests.exe
    if($LASTEXITCODE -ne 0){throw 'Douyin documented-contract tests failed'}
    & $compiler /nologo /out:.tests\StorageTests.exe .\Packages\com.erdon.unitysdk\Runtime\SDK.cs .\tests\StorageTests.cs .\Packages\com.erdon.unitysdk\Runtime\Orders.cs .\Packages\com.erdon.unitysdk\Runtime\VerifiedPayment.cs .\Packages\com.erdon.unitysdk\Runtime\RewardedAds.cs .\Packages\com.erdon.unitysdk\Runtime\Network.cs .\Packages\com.erdon.unitysdk\Runtime\SDKNetwork.cs .\Packages\com.erdon.unitysdk\Runtime\Analytics.cs .\Packages\com.erdon.unitysdk\Runtime\SDKAnalytics.cs
    if($LASTEXITCODE -ne 0){throw 'Storage compile failed'}
    & .\.tests\StorageTests.exe
    if($LASTEXITCODE -ne 0){throw 'Storage tests failed'}
    & $compiler /nologo /out:.tests\LifecycleTests.exe .\Packages\com.erdon.unitysdk\Runtime\SDK.cs .\tests\LifecycleTests.cs .\Packages\com.erdon.unitysdk\Runtime\Orders.cs .\Packages\com.erdon.unitysdk\Runtime\VerifiedPayment.cs .\Packages\com.erdon.unitysdk\Runtime\RewardedAds.cs .\Packages\com.erdon.unitysdk\Runtime\Network.cs .\Packages\com.erdon.unitysdk\Runtime\SDKNetwork.cs .\Packages\com.erdon.unitysdk\Runtime\Analytics.cs .\Packages\com.erdon.unitysdk\Runtime\SDKAnalytics.cs
    if($LASTEXITCODE -ne 0){throw 'Lifecycle compile failed'}
    & .\.tests\LifecycleTests.exe
    if($LASTEXITCODE -ne 0){throw 'Lifecycle tests failed'}
    & $compiler /nologo /out:.tests\SessionResponseTests.exe .\Packages\com.erdon.unitysdk\Runtime\SDK.cs .\Packages\com.erdon.unitysdk.transport.http\Runtime\SessionResponsePolicy.cs .\tests\SessionResponseTests.cs .\Packages\com.erdon.unitysdk\Runtime\Orders.cs .\Packages\com.erdon.unitysdk\Runtime\VerifiedPayment.cs .\Packages\com.erdon.unitysdk\Runtime\RewardedAds.cs .\Packages\com.erdon.unitysdk\Runtime\Network.cs .\Packages\com.erdon.unitysdk\Runtime\SDKNetwork.cs .\Packages\com.erdon.unitysdk\Runtime\Analytics.cs .\Packages\com.erdon.unitysdk\Runtime\SDKAnalytics.cs
    if($LASTEXITCODE -ne 0){throw 'Response policy compile failed'}
    & .\.tests\SessionResponseTests.exe
    if($LASTEXITCODE -ne 0){throw 'Response policy tests failed'}
    & $compiler /nologo /out:.tests\AuthPipelineTests.exe .\Packages\com.erdon.unitysdk\Runtime\SDK.cs .\Packages\com.erdon.unitysdk\Runtime\VerifiedAuth.cs .\tests\AuthPipelineTests.cs .\Packages\com.erdon.unitysdk\Runtime\Orders.cs .\Packages\com.erdon.unitysdk\Runtime\VerifiedPayment.cs .\Packages\com.erdon.unitysdk\Runtime\RewardedAds.cs .\Packages\com.erdon.unitysdk\Runtime\Network.cs .\Packages\com.erdon.unitysdk\Runtime\SDKNetwork.cs .\Packages\com.erdon.unitysdk\Runtime\Analytics.cs .\Packages\com.erdon.unitysdk\Runtime\SDKAnalytics.cs
    if($LASTEXITCODE -ne 0){throw 'Auth compile failed'}
    & .\.tests\AuthPipelineTests.exe
    if($LASTEXITCODE -ne 0){throw 'Auth tests failed'}
    & $compiler /nologo /out:.tests\IsolationTests.exe .\Packages\com.erdon.unitysdk\Editor\PlatformIsolationPolicy.cs .\tests\IsolationTests.cs
    if($LASTEXITCODE -ne 0){throw 'Isolation compile failed'}
    & .\.tests\IsolationTests.exe
    if($LASTEXITCODE -ne 0){throw 'Isolation tests failed'}
} finally {Pop-Location}
