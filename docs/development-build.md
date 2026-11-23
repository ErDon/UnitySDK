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

`tests/WeChatDevelopmentConversion.cs.txt` 是隔离工程的官方转换探针，复制到 `Assets/Editor/WeChatDevelopmentConversion.cs` 后执行 `WeChatDevelopmentConversion.Run`。转换器会修改官方插件导入设置，必须先复制完整官方包并将隔离工程 manifest 指向副本，不得直接指向游戏项目正在使用的包。输出目录为 `Builds/WeChatConversion-UTC时间戳`，必须尚不存在；脚本不删除旧产物。占位 App ID 只用于构建，不用于上传或设备验收。

本地 0.1.23 包缺少 `wx_perf_2022.a` 和 `wx_perf_2021.a`，实际调用在 `ProcessWxPerfBinaries` 空引用失败。探针增加这两个文件及桥接导入器的前置诊断，避免继续报告难定位的空引用。

随后从官方仓库 https://github.com/wechat-miniprogram/minigame-tuanjie-transform-sdk 克隆提交 `915d4bf4f60ef0cd1c29316a8d67257185d609a7`。该提交包标识为 0.1.1，包含两份性能库；它不能与本地 0.1.23 视为同一基线。使用完整副本导入时，现有 WeChatAnalytics 编译失败：官方 `WX.ReportEvent` 要求 `Dictionary<string,string>`，本地旧包接受泛型对象字典。现通过显式 `ERDON_WECHAT_STRING_ANALYTICS` 宏适配该签名并拒绝数字参数；见 analytics.md。仍不能声明该官方提交整体受支持，转换与真机分别验收。禁止只复制新版二进制进旧包，或静默把数字参数转成字符串冒充一致语义。

新版本转换配置还需要 `ProjectConf.relativeDST`，探针通过 SerializedObject 在字段存在时写入隔离相对目录，同时保留绝对 DST；旧版本无该字段时不写入。

启用上述接口兼容宏并配置 relativeDST 后，固定提交915d4bf已完成官方开发构建及转换，输出 SDK_WECHAT_CONVERSION_FILES_PASS，正常退出码0；game.json和project.config.json可解析。此证据不包含最终隔离审计、小游戏启动或真机功能。

## 链接后验证自动注册

构建前发现适配器程序集不保证其在 IL2CPP 链接后仍存在。游戏仅调用核心注册表时，平台包需要 `[assembly: UnityEngine.Scripting.AlwaysLinkAssembly]`，以便链接器处理 `RuntimeInitializeOnLoadMethod` 自动注册入口。该标记不会把未参与编译的平台包加入构建，也不是保留全部类型的 link.xml。

构建完成后执行：

```powershell
./tools/check-linked-platform.ps1 -ManagedDirectory '<隔离工程>/Library/Bee/artifacts/WebGL/ManagedStripped' -Platform WeChat
```

工具读取程序集元数据，不执行其代码；检查目标适配器、带 Unity 启动标记和方法体的注册入口，并拒绝其他 ErDon 平台适配器及引用、Editor SDK。目录必须来自本次构建；空目录不会通过。它不检查全部官方二进制、合并后的 JS/Wasm 或资源，不能替代最终产物审计。

此前第一轮成功构建与转换的 ManagedStripped 未包含微信适配器，不能作为自动注册可用的证据。修复后重新构建确认程序集与注册方法保留。开发者工具/真机是否实际初始化成功仍需运行验收。

## 抖音与 TikTok 的同类构建探针

相同模板提供 `MiniGameDevelopmentBuild.ConfigureDouyin` 与 `ConfigureTikTok`，分别保存目标平台及专用编译宏；构建场景中的探针平台从 SdkProjectSettings 读取。只在对应平台的独立工程使用，先安装匹配的官方插件与适配包。配置方法用于测试工程，会替换 WebGL 自定义编译宏，不适合直接用于已有游戏。

配置进程退出后，重开同一工程执行 `-buildTarget WebGL -executeMethod MiniGameDevelopmentBuild.Build`。仍须遵守同项目无并发进程、输出文件可能来自旧运行、成功标记与链接器检查共同核对的规则。抖音/TikTok 的 Unity WebGL 构建与官方小游戏导出是不同阶段；不得把其中一个当作另一个的通过证明。

### Windows 中文环境的构建输出编码

抖音 TTSDK 6.9.7 在 Unity 2022.3.14f1c1 首轮 WebGL 构建中，Emscripten 自带 Python 在 subprocess.communicate 解码输出时触发 GBK UnicodeDecodeError。若日志确认同一错误，在启动 Unity 的 PowerShell 进程内设置以下变量，然后重新执行构建；不需要修改系统区域或官方 SDK。

```powershell
$env:PYTHONUTF8 = "1"
$env:PYTHONIOENCODING = "utf-8"
# 在这个终端启动隔离工程的 Unity batchmode 构建
```

本次重试得到 Succeeded errors=0、SDK_DEVELOPMENT_WEBGL_BUILD_PASS 和退出码 0；对本次 ManagedStripped 的 Douyin 注册检查也通过。环境变量只影响当前终端及其子进程。该结果为 Unity WebGL 开发构建验证，官方小游戏导出与真机仍待验收。

### 抖音官方导出探针

将 `tests/DouyinDevelopmentConversion.cs.txt` 复制到隔离工程的 `Assets/Editor/DouyinDevelopmentConversion.cs`。先完成 ConfigureDouyin 及普通开发构建，再以同样的 UTF-8 环境运行 `-buildTarget WebGL -executeMethod DouyinDevelopmentConversion.Run`。探针面向已验证的 TTSDK 6.9.7 公开导出 API，使用新包体格式及 Development，输出到新建的 `Builds/DouyinConversion-UTC时间戳/tt-minigame`。不调用发布、上传或登录接口；占位 App ID 必须在实际联调前替换为自己的应用配置。

本机官方导出已成功，game.js、game.json、project.config.json 和 Wasm 已生成，两个 JSON 可解析，链接后的 Douyin 注册检查通过。此结果仍不能代表开发者工具/真机运行通过，也不能替代完整平台产物隔离审计。

### TikTok 官方命令行导出探针

将 `tests/TikTokDevelopmentConversion.cs.txt` 放到独立 TikTok 工程的 `Assets/Editor/TikTokDevelopmentConversion.cs`。先完成 ConfigureTikTok 和普通开发构建，再依次运行三个独立 Unity 进程（每个进程结束后才启动下一个），都使用 `-batchmode -nographics -quit -projectPath <隔离工程> -buildTarget WebGL` 和独立 `-logFile`：

1. `-executeMethod TikTokDevelopmentConversion.Configure`
2. `-executeMethod TTSDK.Tool.StarkBuilder.BuildWebGLFromCommandLine`
3. `-executeMethod TikTokDevelopmentConversion.Verify`

官方 1.1.6 的 StarkBuilder 类为 internal，不能直接在业务或测试脚本引用；第二步使用官方提供给 Unity 的命令行入口。配置阶段固定 Development，关闭 bulk memory，使用占位 App ID，并选择工程外同级的新目录 `TikTokConversion-UTC时间戳`，检查父级不含重解析点。仅限隔离工程，此脚本会保存官方构建配置和场景列表。不能跳过第二步，仅凭 Verify 文件存在就认定本轮构建成功；必须同时检查第二步成功日志和退出码，再验证 JSON、分包和链接后注册。所有步骤均不上传。

本次 1.1.6 官方导出成功退出 0，Verify 亦通过。两个 JSON 可解析，wasmcode/data-package 目录包含非空压缩 Wasm 和数据文件，链接后 TikTok 注册检查通过。当前空工程没有 StreamingAssets 内容，实际 game.json 仅列出这两个分包；后续带标记资源的构建已生成 StreamingAssets 第三个分包，Verify 内容一致性检查通过；设备端下载与读取仍待验证。文件生成不代表客户端可启动或全部平台功能通过。

要验证资源分包，将第一步替换为 `TikTokDevelopmentConversion.ConfigureWithResource`。它在隔离工程生成固定内容的 `Assets/StreamingAssets/erdon-sdk-probe.txt`，遇到同名不同内容的文件会拒绝覆盖。Verify 会解析 game.json、检查分包目录及重复根目录，并在源测试资源存在时要求 StreamingAssets 分包及同内容目标文件；源工程无测试资源时仍可验证无资源构建。测试只证明文件打包保留，不证明设备端读取成功。
