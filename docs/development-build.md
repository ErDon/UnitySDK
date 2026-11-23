# 隔离工程开发构建验证

正式发布尚未验收。非生产配置的 Unity Development Build 仅用于收集技术证据，不表示小游戏转换、真机功能或最终平台裁剪通过。

## 微信 WebGL 探针

在独立空白 Unity 2022.3 工程导入核心包、微信适配包与微信官方依赖。不要安装其他平台依赖。将 `tests/MiniGameBuildProbe.cs.txt` 复制为 `Assets/MiniGameBuildProbe.cs`，将 `tests/MiniGameDevelopmentBuild.cs.txt` 复制为 `Assets/Editor/MiniGameDevelopmentBuild.cs`。脚本会创建并保存专用场景到 `Assets/DevelopmentBuildProbe.unity`，输出到 `Builds/DevelopmentWebGL`，因此只在隔离工程执行。

先以 Unity 命令行运行 `-batchmode -nographics -quit -projectPath <隔离工程绝对路径> -executeMethod MiniGameDevelopmentBuild.ConfigureWeChat -logFile <配置日志绝对路径>`。等待进程退出且日志出现 `SDK_DEVELOPMENT_BUILD_CONFIGURED`，再启动第二次：

`-batchmode -nographics -quit -buildTarget WebGL -projectPath <同一隔离工程> -executeMethod MiniGameDevelopmentBuild.Build -logFile <构建日志绝对路径>`。

使用两次独立 Unity 进程同时检查配置保存后能否恢复。不要在旧进程仍占用工程时重复启动。成功标记为 `SDK_DEVELOPMENT_WEBGL_BUILD_PASS`，还需检查本次日志、BuildReport、产物时间和 `Builds/development-build-result.txt`；旧结果文件不能代表新运行的状态。

配置使用公开占位标识，无登录后端和支付。探针场景引用真实运行时启动入口、分享、存储、激励广告与上报工厂，避免验证完全不引用 SDK 的空场景。运行场景会尝试平台初始化，但不会自动登录、请求广告、分享、发送事件或支付。占位标识不能用于真机验收；正式接入必须配置有效应用身份及官方导出设置。

此脚本执行 Unity WebGL 构建，不替代微信官方转换小游戏流程。构建通过后还必须进行官方转换，检查最终 JS/Wasm/资源中的平台隔离，并在开发者工具与设备中验收。关闭能力开关的运行时不创建实例，与编译产物完全剔除能力代码是不同的验证项。

## 配置持久化

SdkProjectSettings 必须位于同名脚本文件，以便 Unity 保存稳定脚本引用。早期开发版将该类型放在 SdkSettingsWindow.cs，可能保存 `m_Script: {fileID: 0}` 并在重启后恢复默认值。升级后先核对面板值，再保存并重启确认；早期未恢复的配置需重新填写，不能直接假定旧文件已正确加载。

## 官方转换验证进展

`tests/WeChatDevelopmentConversion.cs.txt` 是隔离工程的官方转换探针，复制到 `Assets/Editor/WeChatDevelopmentConversion.cs` 后执行 `WeChatDevelopmentConversion.Run`。转换器会修改官方插件导入设置，必须先复制完整官方包并将隔离工程 manifest 指向副本，不得直接指向游戏项目正在使用的包。输出目录 `Builds/WeChatConversion` 必须尚不存在；脚本不删除旧产物。占位 App ID 只用于构建，不用于上传或设备验收。

本地 0.1.23 包缺少 `wx_perf_2022.a` 和 `wx_perf_2021.a`，实际调用在 `ProcessWxPerfBinaries` 空引用失败。探针增加这两个文件及桥接导入器的前置诊断，避免继续报告难定位的空引用。

随后从官方仓库 https://github.com/wechat-miniprogram/minigame-tuanjie-transform-sdk 克隆提交 `915d4bf4f60ef0cd1c29316a8d67257185d609a7`。该提交包标识为 0.1.1，包含两份性能库；它不能与本地 0.1.23 视为同一基线。使用完整副本导入时，现有 WeChatAnalytics 编译失败：官方 `WX.ReportEvent` 要求 `Dictionary<string,string>`，本地旧包接受泛型对象字典。现通过显式 `ERDON_WECHAT_STRING_ANALYTICS` 宏适配该签名并拒绝数字参数；见 analytics.md。仍不能声明该官方提交整体受支持，转换与真机分别验收。禁止只复制新版二进制进旧包，或静默把数字参数转成字符串冒充一致语义。

新版本转换配置还需要 `ProjectConf.relativeDST`，探针通过 SerializedObject 在字段存在时写入隔离相对目录，同时保留绝对 DST；旧版本无该字段时不写入。

启用上述接口兼容宏并配置 relativeDST 后，固定提交915d4bf已完成官方开发构建及转换，输出 SDK_WECHAT_CONVERSION_FILES_PASS，正常退出码0；game.json和project.config.json可解析。此证据不包含最终隔离审计、小游戏启动或真机功能。
