# 配置面板（开发预览）

Unity 菜单：Tools → ErDon SDK → 配置与预览。

设置存储在接入游戏的 ProjectSettings/ErDonSdk.asset，可随游戏版本管理。只填写 Game ID / App ID，禁止填写服务端密钥。平台只能选择一个，模块可以勾选；界面实时显示缺失参数与未实现能力。

配置现已连接 Editor Mock：进入 Play Mode，选择模拟结果，点击“按配置初始化 Mock”，再点击“调用 SDK.Login”。登录未勾选则返回 MODULE.DISABLED。真实平台配置仍未装配，不能认为已接入。可以模拟成功、取消、拒绝、限流和超时；不生成真实账号或订单。

当前开发版本主动阻止所有 Player 构建：Mock 只支持 Editor，其余平台适配尚未实现。后续接入真实适配后才开放对应目标。阻断发生在 IPreprocessBuildWithReport，不修改其他项目的编译宏、插件导入设置或资源。

此版本不宣称已完成平台裁剪。后续要补：独立平台程序集/依赖包、运行时配置生成、模块注册、插件资源过滤、构建后产物检查。未知平台能力不能静默降级为成功。


## 按配置创建运行时对象

UnitySdkHost.InitializeWithFactory(config, factory) 接收 ISdkProviderFactory。核心按 LoginEnabled / StorageEnabled 决定是否调用 CreateAuth / CreateStorage；未开启模块不调用工厂。工厂必须返回全新对象，不得复用旧实例或在创建过程中重入 SDK。平台官方初始化应在此之前完成。

工厂 CreateAuth 接收主线程派发委托，创建 VerifiedAuth 时传给它，确保授权与会话回调回到 Unity 主线程。可自行组合 IPlatformCredentialProvider 与 ISessionExchange；不强制 HTTP 实现。CreateStorage 返回目标平台 IStorage。

启用模块返回 null 会抛 InvalidOperationException。工厂抛异常时，SDK 回收本次已返回对象，旧运行实例继续有效；工厂内部未返回的局部对象由工厂回收。每次创建必须提供全新的后端 exchange，交付后由 VerifiedAuth 拥有，随后由 SDK 在 Shutdown/重新初始化时释放。

最小工厂示例（接入游戏提供两个创建函数）：

```csharp
using System;
using ErDon.UnitySDK;

sealed class GameSdkFactory : ISdkProviderFactory
{
    readonly Func<Action<Action>, IAuth> createAuth;
    readonly Func<IStorage> createStorage;
    public GameSdkFactory(Func<Action<Action>, IAuth> auth, Func<IStorage> storage)
    { createAuth = auth; createStorage = storage; }
    public IAuth CreateAuth(Action<Action> post) { return createAuth(post); }
    public IStorage CreateStorage() { return createStorage(); }
}
```

调用 UnitySdkHost.InitializeWithFactory(config, new GameSdkFactory(authFactory, storageFactory))。关闭的能力对应函数可为空，因为不会被调用。工厂不要预先创建 provider，否则无法达到关闭模块不创建对象的效果。Editor 预览已走此入口。tests/FactoryTests.cs 覆盖开关、失败回收、旧实例保护、重入拒绝及共享对象只释放一次。

此入口负责运行时装配，不等于官方包、JS 或资源已从构建产物裁剪；自动配置生成及真实平台包体核验仍待完成。
