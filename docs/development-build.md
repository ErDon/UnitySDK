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
