# 配置启动示例

用于微信、抖音或 TikTok 的开发联调。它不会自动登录、展示广告、发起支付或上报事件。需要先安装一个目标平台的官方 SDK 和对应 ErDon 适配包；此示例不会下载安装官方依赖，也不替代真机验收。

## 导入与配置

1. 在 Package Manager 安装核心包和本 HTTP 包，再从本包 Samples 导入 **Configured Platform Startup**。
2. 安装所选平台适配包及官方 SDK。切换 WebGL，配置对应的单个平台宏，按平台接入文档完成官方 App ID 设置。
3. 打开 `Tools → ErDon SDK → 配置与预览`，填入自己的 Game ID/App ID，选择真实平台及能力开关。按开发验证要求保留非生产配置，导出 JSON。
4. 将 JSON 放入游戏的 Assets 目录。创建一个根 GameObject，挂载 `ConfiguredSdkExample`，把 JSON 拖到 `Configuration Json` 字段。
5. 开启登录时，填写自己服务端的 HTTPS 会话兑换地址 `Session Endpoint`。服务端必须实现仓库会话协议并配置对应游戏、应用、环境和平台凭据；不要填写官方 App Secret。关闭登录时无需此地址。
6. 运行并等待 `SDK ready`。把游戏登录按钮连接到组件的 `LoginFromGame()`，成功后在源码标注位置接入游戏自己的会话层。当前示例只显示成功提示，不会替游戏保存会话。

平台初始化不支持 Editor 或缺少官方运行条件时，会返回失败；应按平台文档导出开发包并在宿主中验证。Mock 预览使用核心包的 Mock Login 示例或配置面板。

## 模块与生命周期

- 配置中登录开启才创建会话兑换对象，网络开启才创建 UnityNetwork；HTTP 包引用本身不代表网络模块开启。关闭网络不会禁止登录所需的会话兑换。
- `ERDON_EXCLUDE_LOGIN`、`ERDON_EXCLUDE_NETWORK` 条件编译保护对应构造代码。使用面板应用构建能力选择，并确保导出的 JSON 与构建开关一致；配置不能重新开启已编译排除的能力。
- 本组件应是游戏唯一的 SDK 启动/关闭负责人，不要同时运行 MockLoginExample、配置面板初始化或其他自动初始化脚本。SDK 是游戏内的单个聚合实例。
- 首个有效组件跨场景保留；后续场景的重复组件不会再次初始化或在销毁时关闭原实例。必须挂在根对象上，且保持对象激活，HTTP 协程才会运行。
- 销毁拥有 SDK 的组件会调用 UnitySdkHost.Shutdown，旧请求结果被丢弃。退出或销毁时，游戏也应自行结束等待界面。不要在登录回调中立即销毁此组件后继续使用 SDK。
- 配置在启动时读取；修改 JSON 或地址后需重新开始游戏。示例不自动重试，不覆盖已有游戏的初始化策略。Editor 使用正常场景重载设置；禁用场景重载的生命周期验证由仓库专项测试覆盖，不以该示例宣称支持。

SDK 日志未指定上层回调，因此走默认 Unity 输出；会话和凭证不打印。失败结果显示错误码、SDK 原因与 CallId。该示例组件尚未注入支付工厂。即使抖音/TikTok 支付已提供实现，仅勾选支付也会被缺少工厂的预检拒绝；需按仓库 docs/douyin-payments.md 或 docs/payment-pipeline.md 扩展接入层，不能把示例当作现成收银台。

## 验证范围

本示例已用 Unity 2022.3.14f1c1 的实际核心/HTTP 程序集及 Unity 类型编译，覆盖默认编译和排除登录/网络的编译分支。另在独立 Unity 工程以真实组件运行 Play Mode，使用平台注册替身验证 JSON 游戏/环境绑定、启动前拒绝登录、启动不自动登录、重复组件不抢占或关闭 SDK、成功后按钮登录、会话不写日志及所属组件销毁清理。未将这些结果视为真实平台启动、后端兑换或独立游戏全流程验收。

可将仓库 `tests/ConfiguredSamplePlayChecks.cs.txt` 复制为隔离工程的 `Assets/Editor/ConfiguredSamplePlayChecks.cs`，并导入本示例，以 `-executeMethod ConfiguredSamplePlayChecks.Run` 运行（不要传 `-quit`）。测试会新建空场景、进入 Play Mode 并自行退出 Unity，只能在独立测试工程执行，不能在业务工程运行。成功标记为 `SDK_CONFIGURED_SAMPLE_PLAY_PASS`。
