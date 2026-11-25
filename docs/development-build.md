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

### 全部能力关闭的构建验证

构建探针现在序列化 SdkProjectSettings.CreateRuntimeConfiguration() 的完整快照，避免关闭模块后场景仍硬编码开启它们。已有测试工程须更新 MiniGameBuildProbe.cs 和 MiniGameDevelopmentBuild.cs，并重新运行 Build 生成场景；不要直接沿用旧场景做新的裁剪验收。

隔离工程先运行 `MiniGameDevelopmentBuild.ConfigureAllCapabilitiesOff`，进程结束后再运行 Build。配置方法关闭所有运行时能力，并应用七类能力编译排除。网络/支付检查覆盖自有实现类，不代表真实支付或所有第三方代码。构建成功后：

```powershell
./tools/check-linked-platform.ps1 -ManagedDirectory '<隔离工程>/Library/Bee/artifacts/WebGL/ManagedStripped' -Platform TikTok -ExcludedCapabilities Login,Storage,Share,Ads,Analytics,Network,Payments
```

检查器扫描所有链接程序集的类型定义和类型引用，拒绝所选平台的排除能力残留，并继续要求平台注册方法存在。它不是官方 SDK 内部代码或最终 JS/Wasm 的完整审计。本轮 TikTok 实际构建通过全部五项检查，且保存的场景开关全部为关闭；同一检查器对未裁剪的抖音旧构建要求 Login 排除时正确失败。

七类关闭增量：TikTok 实际构建已通过，检查额外确认 UnityNetwork、NetworkProviderFactory、BoundedResponseBuffer、VerifiedPayment、UnityOrderClient、OrderResponsePolicy 均无类型或引用残留。登录会话兑换不受 NETWORK 排除符号控制；部分启用组合仍需独立验证。

### 登录开启、通用网络关闭

构建探针现在可在登录开启时装配 UnitySessionExchange，因此隔离工程必须安装 `com.erdon.unitysdk.transport.http`（本轮三平台工程均已安装）。它使用保留的 `https://example.invalid/session` 占位地址，不调用 SDK.Login，也不发送会话兑换请求；不要将它当作可登录的业务示例。

先执行 `MiniGameDevelopmentBuild.ConfigureLoginOnly`，退出后执行 Build。场景保存 login=true、network=false、payments=false。检查命令：

```powershell
./tools/check-linked-platform.ps1 -ManagedDirectory '<隔离工程>/Library/Bee/artifacts/WebGL/ManagedStripped' -Platform TikTok -ExcludedCapabilities Storage,Share,Ads,Analytics,Network,Payments -RequiredTypes ErDon.UnitySDK.TikTok.TikTokCredentialProvider,ErDon.UnitySDK.VerifiedAuth,ErDon.UnitySDK.UnitySessionExchange
```

RequiredTypes 只接受真实类型定义作为保留证据，单纯类型引用不算保留。TikTok 实际 Development Build 和上述检查通过，证明网络能力裁剪没有删除登录 HTTP 依赖；不证明真实平台登录、HTTPS 接口或后端联调成功。负例使用已移除登录的微信构建要求 CredentialProvider 保留，检查器正确拒绝。


### 绑定产物哈希的逐文件审查

`tools/review-export-isolation.py` 读取完整导出库存，重新扫描实际目录的全部文件并核对路径、大小和 SHA-256。新增、删除或修改文件会拒绝使用旧库存。输出必须在导出目录外，不能覆盖输入文件。

```powershell
python tools/review-export-isolation.py --inventory compatibility/wechat-export-files-2026-11-24.json --root '<对应导出根目录>' --output '<审查报告.json>'
```

未提供 `--reviews` 时生成待审清单。可另建 JSON，包含 schemaVersion=1、platform 和 reviews 数组；每项填写 path、sha256、verdict（target/shared/game-content/foreign）、reviewer、evidenceReference、reason。只有完整审查记录及当前哈希吻合才记为 review-recorded。旧哈希记为 stale-review；库存中的 foreign-only-exact-match 不允许用简单人工结论覆盖，仍阻断，需补充目标平台来源证据并重新整理库存。

退出码 0 表示审查记录完整，2 表示待审或阻断，输入无效会失败。记录完整不是平台来源认证、运行验收或语义级不存在外平台代码的证明。缺少字符串标记不作为通过依据。

2026-11-25 对已有导出库存进行实物哈希复核：微信84项、抖音13项、TikTok17项。初始待审报告保存在 compatibility/*-review-2026-11-25.json，尚未放行。微信库存根目录包括 minigame 子目录；抖音/TikTok 库存根目录是 tt-minigame 本身。

抖音 plugin-config.js 仅匹配 TikTok 文件基线，但此前已在抖音官方工具 DLL 中找到完整文本，见 compatibility/douyin-plugin-config-review-2026-11-24.json。这不是确认混入 TikTok SDK；生成来源和最终包完整隔离仍需结合其他证据审核，暂不自动消除阻断。


来源核对增量：`--vendor-inventory` 可重复传入导出报告使用的全部平台基线。工具首先验证每个库存文件本身的 SHA-256 与导出报告一致，拒绝遗漏、重复或被修改的基线；再给目标平台完全一致的文件附上 sourcePaths 和 inventorySha256。该证据仅证明目标平台基线包含相同字节，不证明生成路径或所有模块裁剪。

2026-11-25 已保存三份 `compatibility/*-review-decisions-2026-11-25.json`：微信63项、抖音2项、TikTok5项完成目标字节来源审查；对应报告剩余21/11/12项待审或阻断。共70项已核对、44项未放行。所有结论仍绑定文件哈希；没有用“未发现字符串”填充任何审查结论。

### 导出配置与单线程边界检查

在接入游戏的每次正式候选导出后运行：

```powershell
python tools/inspect-runtime-config.py --root '<包含 game.json 的小游戏目录>' --output '<导出目录外的报告.json>'
```

微信这里应传 `minigame`，其他两平台传 `tt-minigame`。检查器只读取配置，不执行第三方脚本；记录文件哈希，检查占位 App ID、分包目录、重复/越界路径，以及调试连接、Worker 和 Brotli MT 字面配置。报告不输出 App ID 或调试连接地址。退出 0 只表示检查成功完成，应查看 findings；即使没有发现问题，productionReadiness 仍为 not-certified，不能代替构建、来源审查和真机验收。

现有三份开发导出均有占位 App ID 和调试连接配置，报告见 `compatibility/*-runtime-config-2026-11-25.json`。不应作为发布包上传。真实 App ID 通过工程配置填写，再重新导出；不要改旧包来沿用旧哈希证据。

SDK 自有运行时不依赖 C# 工作线程，WebGL 构建检查拒绝开启 `PlayerSettings.WebGL.threadsSupport`。这不等于删除官方运行环境的所有 Worker 文件。固定版本微信官方源码中：

- `CompileOptions.brotliMT` 控制编辑器端 `MultiThreadBrotliCompress`，并输出 `useBrotliMT` 元数据；它不是游戏 C# 多线程开关。
- 官方 `workers/response/index.js` 含文件写入处理；配置声明或模板存在不能证明设备端已调用。
- 官方 `ConvertForThread` 另受 `EnableRenderThread` 控制，升级官方 SDK 时也要核查该选项，不能仅检查 Brotli 标记。

对应源码文件哈希与结论见 `compatibility/wechat-thread-source-review-2026-11-25.json`。不自动移除官方 Worker 或修改其脚本；需在目标版本、设备和配置上确认实际运行行为。以上审查不改变剩余真机验收和完整包体裁剪状态。

### 内容审查增量（2026-11-25）

最新逐文件记录为微信72项、抖音6项、TikTok11项，共89项；待审或阻断分别12/7/6项，共25项。此前70/44为历史进度。

`compatibility/export-content-review-2026-11-25.json` 补充14项：7个零字节入口不包含可执行内容；6份 JSON 经过完整内容检查，属于声明式配置；1个 TikTok StreamingAssets 探针与工程源文件逐字节相等。配置引用的外部 UnityPlugin 仍需独立验证，配置审查不等于插件代码通过，原占位 App ID 和开发配置问题也未解除。

`compatibility/wechat-template-review-2026-11-25.json` 补充5个脚本：index、storage、texture、font/index、check-version。源文件先核对固定官方库存哈希，再按报告中的 replacementMap 替换；以 outputEncoding 指定的 UTF-8 BOM 规则编码后，完整字节与导出一致，不忽略任何区域或规范化换行。报告包含源/目标哈希和全部替换值，可按相同方式复核。该结论证明目标模板可以还原导出内容，不证明实际执行了哪个生成器，也不替代完整包体和设备验收。
