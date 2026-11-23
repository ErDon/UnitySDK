# 平台依赖隔离（实现中）

平台适配包根目录提供 `erdon-platform.json`，字段如下（示例，不是实际官方依赖版本）：

```json
{
  "Platform": "WeChat",
  "Package": "com.example.wechat",
  "Version": "1.0.0",
  "Assemblies": ["Example.WeChat.Runtime"],
  "AssetRoots": ["Packages/com.example.wechat/Runtime"]
}
```

清单版本必须与已安装 UPM 包一致，禁止 latest 或通配版本。程序集名保留点号，不把 Runtime 等后缀误当扩展名。资源路径必须位于 Assets 或 Packages 内，禁止父目录跳转。

构建前读取已安装包的清单，检查 Player 源码程序集及其 compiledAssemblyReferences 中是否有其他平台程序集，覆盖没有 asmdef 的预编译 SDK DLL。构建后核查 BuildReport 输出文件和打包资源来源，写入 Library/ErDonSdk/last-artifact-audit.txt。发现登记的非目标平台内容则构建失败。

该检查只验证已登记内容，不能证明未登记官方插件、原生二进制、合并 JS 或 StreamingAssets 已被排除。后续适配包需提供覆盖这些资源的清单和实际产物验证；三平台已有独立开发构建与官方导出记录，见 development-build.md，但这些记录不覆盖全部最终产物隔离。不能仅凭审计空错误宣称完整裁剪。

程序集与文件本身应通过平台专用包、asmdef 定义约束、插件导入配置及构建装配实现排除，审计只是最后防线，不负责删除游戏文件。

首批实际平台：微信、抖音、TikTok。Android/iOS 仅保留能力接口，不纳入本轮具体渠道验收（用户已确认）。

## 构建宏检查

当前真实小游戏适配只面向 Unity WebGL。配置窗口与构建前检查会读取当前构建目标组的 Scripting Define Symbols，并要求只启用所选平台的宏：

| 平台 | 必需宏 |
| --- | --- |
| WeChat | ERDON_PLATFORM_WECHAT |
| Douyin | ERDON_PLATFORM_DOUYIN |
| TikTok | ERDON_PLATFORM_TIKTOK |
| Editor Mock | 不启用任何 ERDON_PLATFORM_ 宏 |

未知 ERDON_PLATFORM_ 宏、缺失目标宏、同时启用多个平台、非 WebGL 目标均报错。其他项目自有宏不受影响。此功能只检查，不自动改写 Player Settings 或删除包；选择目标平台并不代表官方 SDK 已装配。当前真实平台初始化/官方依赖装配未完成，其他发布阻断仍然有效。


预编译引用检查使用 Unity 实际 Player 编译图，文件名比较同时规范化 Windows/Unix 分隔符和 DLL 扩展名。tests/SdkEditorChecks.cs.txt 将真实编译引用临时登记为外部平台依赖，确认检查会拦截；该测试仅构造内存清单，不修改插件或业务工程。它不覆盖未被引用但仍被打包的原生插件，后者仍需导入设置和产物核验。

## 已知官方依赖目录检查

仅靠已安装适配包的 erdon-platform.json 不够：游戏可能单独导入某个官方 SDK，而未安装该平台适配。现在面板、构建前检查和构建后审计额外查看已知官方目录：微信 UPM、TikTok 的 Assets/Plugins/com.tiktok.minigame、BGDT 的 Assets/Plugins/ByteGame/com.bytedance.starksdk，以及字节同名 UPM 目录。

发行工程采用单目标官方依赖：发现明确属于其他平台的上述目录即阻止构建，即使导入器暂时关闭也不自动放行。工具不会删除或移动 SDK；应在独立发行工程保留目标依赖。此规则比单纯“最终产物不包含它”更严格，用来避免同名程序集/桥接的导入冲突。

Packages/com.bytedance.starksdk 的包名本身不能区分抖音和 TikTok，因此只把它作为微信/Mock 的外平台依赖；抖音/TikTok 间仍须结合版本快照及官方安装来源核对。任意改名目录和未登记第三方资源尚不能靠此规则识别，完整导出产物检查仍待完成。不能把目录检查通过当作裁剪已证明。

## 能力开关与代码裁剪的区别

当前工厂按运行时配置创建能力。关闭登录意味着不创建登录实例、不允许聚合入口执行登录，并不自动删除工厂方法中的类型引用。2026-11-23 检查 TikTok 带资源导出的 ManagedStripped，关闭登录时 TikTokCredentialProvider 和 VerifiedAuth 仍保留，不能将其声称为模块代码裁剪通过。

后续能力编译裁剪需要独立的构建配置：在编译前生成模块选择、工厂条件注册，构建前核对编译选择与运行时配置一致，并对链接后类型及官方桥接引用进行正反验证。运行时配置不得重新开启构建时未包含的能力。官方单体 SDK 可能保留其内部公共依赖，不能承诺仅靠删除适配器就删除所有官方代码；最终需要实际产物审计。该工作与排除其他平台 SDK 是两个验收项。

后续实现进展：已为三平台五类适配实现提供 ERDON_EXCLUDE_* 条件编译、面板应用入口及配置一致性拒绝。裁剪模式 C# 测试验证五类 TikTok 类型均不编译、工厂和运行时配置不能重新开启；实际 Unity TikTok Development Build 验证登录类型消失、存储类型及注册方法保留。前述未裁剪构建是修复前基线；默认不应用选择仍保留能力。尚不能宣称所有平台/模块、公共核心和官方 SDK 已完成裁剪。

三平台全部关闭组合的实际验证现已补齐：微信官方提交 915d4bf、抖音 6.9.7、TikTok 官方 1.1.6，五类适配定义/引用缺失且注册保留。仍不将此结论扩大为全部模块或官方 SDK 已裁剪。详见 compatibility/README.md。

## 官方 SDK 内部保留边界（2026-11-23 实测）

能力未勾选时，聚合入口拒绝调用，应用构建选择后我们的对应实现可以从 Player 编译中排除。但官方 SDK 的桥接、序列化及内部入口有自己的保留规则。不能为追求体积而直接删掉官方 `link.xml`，否则反射、回调或 JS 桥接可能只在真机失败。

本次读取三份实际 Development Build 的 ManagedStripped 元数据，并重新执行适配器排除与注册保留检查：

| 官方基线与构建选择 | 链接后程序集类型数 / 方法数 | 仍存在的官方方法（有方法体） |
| --- | --- | --- |
| 微信固定提交 915d4bf；五类平台能力全部关闭 | Wx 18 / 456；wx-runtime 643 / 2322 | WeChatWASM.WX.Login、ReportEvent |
| 抖音 6.9.7；五类平台能力全部关闭 | ttsdk 1574 / 7727；TTWebGL 81 / 418 | TTSDK.TT.Login、CreateRewardedVideoAd、ReportAnalytics |
| TikTok 官方 1.1.6；只开启登录，其他六项关闭 | ttsdk 881 / 3801；TTWebGL 88 / 488 | TTSDK.TT.CreateRewardedVideoAd、ReportEvent |

微信官方 `Runtime/Plugins/link.xml` 对 wx-runtime、LitJson、Wx 使用 `preserve="all"`。TikTok 官方 `link.xml` 对 com.tiktok.ttsdk、ttsdk、TTWebGL、TTLitJson 使用 `preserve="all"`。抖音所检查的根 link.xml 只显式保留 GraphicsSettings；本次没有据此推断其广泛保留的具体原因，实际元数据存在性才是本表依据。

表中数量仅统计链接后类型定义和方法定义；不是最终 Wasm 体积、可达方法数量，也不表示这些能力被运行。TikTok 登录依赖 VerifiedAuth、UnitySessionExchange 在“网络和支付关闭”组合中仍被证明保留，六个关闭能力的自有实现类型不存在。官方 SDK 内部仍保留广告入口不代表 SDK.ShowRewardedAd 可以越过模块开关。

可重新生成完整类型、方法、方法体标识及程序集 SHA-256 清单：

```powershell
./tools/inspect-vendor-retention.ps1 `
  -ManagedDirectory '<目标工程>/Library/Bee/artifacts/WebGL/ManagedStripped' `
  -AssemblyNames ttsdk,TTWebGL > vendor-retention.tsv
```

微信参数使用 `Wx,wx-runtime`；抖音可增加 `com.bytedance.ttsdk.ref`。缺少任何指定程序集会失败，不会用空报告当成功。该工具只读输入；输出是待审查清单，没有“裁剪全部通过”的结论。重复构建会覆盖 ManagedStripped，应在每次构建后保留报告及对应配置，不能把后来构建的元数据配到较早导出包。

当前结论：运行时禁用与自有能力实现排除已有证据；官方内部能力代码的进一步精简仍待平台支持及真机验证。排除其他平台 SDK、最终 JS/Wasm/原生/资源审计仍是独立待办，不受本表替代。
