# 工作计划

更新时间：2026-11-23。以 Unity 2022.3 为首轮验证基线，不依赖 RS2 和第三方异步框架。当前已推进多个阶段；此前“当前执行第一阶段”的描述已过时。

**看进度以本页顶部清单为准。** `[x]` 表示该子项已实现并完成列明范围的验证；`[ ]` 表示仍待完成或验收。阶段整体需要所有交付与验证完成后才打勾。编译、Mock 和注入回调测试不等于真实平台或真机验收。下方执行记录保留历史状态，其中旧的“待完成”可能已被后续记录解决。

## 进度总览

已完成并按列明范围验证 **63 / 78 个子项**，待完成或验收 **15 个**。这是清单数量统计，不代表整体工作量完成百分比；正式发布验收、真机与支付闭环仍未完成。

| 阶段 | 已勾选 | 待完成/验收 |
| --- | ---: | ---: |
| 设计基线与仓库骨架 | 1 | 0 |
| 第一阶段：聚合核心与生命周期 | 12 | 0 |
| 第二阶段：配置面板、装配与裁剪 | 11 | 3 |
| 第三阶段：微信登录与服务端 | 4 | 1 |
| 第四阶段：广告、分享、存储、网络与上报 | 22 | 4 |
| 第五阶段：支付与订单 | 4 | 2 |
| 第六阶段：抖音、TikTok 与升级 | 7 | 2 |
| 第七阶段：跨游戏接入与发布 | 2 | 3 |

后续每次实现并验证一个子项，同步更新下方勾选与此表；只有代码或 Mock 验证的项目，会保留真实环境验收项。

- [x] 设计基线、独立 UnitySDK 仓库和 UPM 包骨架。

## 第一阶段：聚合核心与生命周期（已完成列明范围验证）

- [x] SDK 聚合入口、按能力接口多态与平台工厂，工厂和配置的平台身份校验。
- [x] 初始化/释放、模块关闭不创建实例、部分创建失败回收和旧实例保护。
- [x] 统一结果、错误原因、平台码、重试信息、CallId/TraceId 和结果快照隔离。
- [x] 错误诊断文档按实际 SdkError 字段、登录分层失败和各能力结果语义校正，删除未实现的设计字段/承诺。
- [x] SDK 格式化日志、上层日志回调、Unity 默认输出及日志回调异常回退。
- [x] 登录/分享/支付的本地超时与取消、重复/迟到回调抑制、主线程派发异常恢复。
- [x] 平台启动编排、等待就绪、超时/取消及 Unity 宿主自动驱动；隔离 Play Mode 检查通过。
- [x] Editor Mock 登录示例及公共核心回归测试。
- [x] 禁用域重载和场景重载后连续两轮 Play Mode 生命周期检查；宿主队列异常统一日志。
- [x] 网络/异步上报终态句柄引用释放、取消回调内关闭 SDK 的顺序保护与专项回归。
- [x] UnityNetwork 底层句柄终态回调/宿主引用清理；隔离 Unity 取消、释放、拒绝及真实超时检查。
- [x] 本 SDK 聚合输出/Startup 日志及 Unity 默认输出一致性复核；已列生命周期边界、初始化失败保留旧启动、成功替换与迟到回调检查。

## 第二阶段：配置面板、装配与裁剪（进行中）

- [x] 独立配置面板、模块勾选、保存和配置预览。
- [x] 登录/存储/分享/支付的 Editor Mock 预览与 Play Mode 检查。
- [x] 面板导出运行时 JSON、配置校验和序列化往返测试。
- [x] 平台独立程序集、宏校验、依赖清单与生产 Mock 阻断。
- [x] Player 程序集/预编译引用和 BuildReport 资源检查；已知官方目录混装检测及目标适配器存在性检查。
- [x] 运行时配置选择平台、自动注册与按需装配入口；公共预检测试及微信/抖音自动注册检查。
- [x] 面板平台启动/取消/关闭按钮及后端工厂注入；Play Mode 逻辑回归。
- [x] TikTok 干净官方 1.1.6 工程注册、开发构建与官方小游戏导出、链接后注册保留验证；最终隔离与真机另行验收。
- [ ] 面板交互与导出操作的视觉验收。
- [x] 微信本地 0.1.23 隔离 WebGL Development Build，配置跨进程恢复及现有 BuildReport 审计通过；不含小游戏转换/真机。
- [x] 微信官方固定提交 915d4bf 的开发构建与小游戏转换，输出文件及 JSON 检查通过；不含最终隔离和真机。
- [x] 抖音 TTSDK 6.9.7 隔离开发构建与官方小游戏导出、配置 JSON 检查及链接后自动注册保留验证；不含最终隔离和真机。
- [ ] 能力模块编译裁剪：构建选择与运行时配置一致、关闭能力实现从链接产物移除，验证官方单体 SDK 依赖边界。
- [ ] 实际目标平台导出及最终 JS/Wasm/原生/资源包裁剪证明；仅开放非生产 Development Build 技术验证，正式构建仍阻断。

## 第三阶段：微信登录与服务端（适配实现完成，联调待办）

- [x] 微信登录凭证适配、存储/分享实现及按需能力工厂。
- [x] 本地 0.1.23 包在隔离 Unity 工程重新编译与适配加载；排除旧 Wx.dll 版本混用问题。
- [x] 服务端微信凭证兑换实现、游戏/应用绑定、一次性凭证防重放及会话管理测试。
- [x] 微信本地 0.1.23 初始化成功码 200 核对与初始化桥接；官方程序集和注入返回码验证。
- [ ] 官方包来源完整性核验、真实凭证兑换、Unity HTTPS 联调和真机登录。

## 第四阶段：广告、分享、存储、网络与上报（部分完成）

- [x] 分享与存储统一接口、模块开关、错误处理、日志及接口示例。
- [x] 微信/抖音/TikTok 分享与存储适配的官方入口编译和注入行为测试。
- [x] 激励广告底层接口和抖音单视频适配；官方 DLL 编译及注入生命周期测试。
- [x] 激励广告聚合入口、开关、超时/取消、日志关联与抖音按需工厂。
- [x] 激励广告配置开关、Editor Mock 预览和运行时 JSON 映射；Play Mode 行为验证。
- [x] 微信激励广告接口、按需工厂与配置映射；官方程序集编译和注入行为测试。
- [x] TikTok 激励广告接口、工厂及配置映射；Runtime/Editor DLL 编译与注入行为测试。
- [ ] 三平台激励广告真机验证及 TikTok 干净官方插件集成。
- [x] 网络请求/响应契约、数据快照和接收大小限制测试；Unity HTTP 底层实现编译。
- [x] Unity 网络取消、释放抑制回调、宿主不可用和连接超时的本机 Play Mode 检查。
- [x] SDK.Request 并发聚合、独立取消/超时、所有权和脱敏日志；公共及 Unity 回归。
- [x] 网络 Mock 配置工厂/面板接入与 Play Mode 验证。
- [x] 微信可选网络工厂配置装配；平台就绪后按需创建及缺工厂预检。
- [x] Unity Editor 实际 HTTPS GET/HEAD 响应及接收限额检查。
- [x] 网络请求接入说明、GET/JSON POST、参数限额、状态语义、错误/取消与平台验证边界文档。
- [x] 抖音/TikTok 可选网络工厂配置接入及平台身份、所有权测试。
- [ ] 三平台小游戏网络行为与 TikTok 干净插件工程验证。
- [x] 数据上报事件快照、参数约束及仅表示调用完成的结果契约；公共契约回归。
- [x] SDK.ReportEvent 聚合、配置开关、按需工厂、所有权、错误关联与日志；公共回归和接口说明。
- [x] 微信数据上报适配、按需工厂与配置映射；官方程序集编译、官方序列化与注入行为测试。
- [x] 抖音数据上报适配、保留字段保护、工厂与配置映射；官方 DLL 编译和注入行为测试。
- [x] TikTok 数据上报异步底层适配；Runtime/Editor DLL 编译、参数序列化与回调生命周期测试。
- [x] 异步上报聚合、独立取消/超时、主线程派发与 TikTok 工厂配置；公共及双 DLL 回归。
- [x] 数据上报 Mock 配置/预览和 Unity Play Mode 成功、失败、超时、取消、关闭/迟到回调检查。
- [ ] 数据上报三平台真机与后台入库验收；面板视觉交互仍待整体验收。
- [ ] 三个平台分享/存储的真机行为、配额及重启持久性验收。

## 第五阶段：支付与订单（公共链路完成，真实支付待办）

- [x] SDK.Pay 聚合、开关、超时/取消、错误关联及 Editor Mock 预览。
- [x] 客户端订单接口、HTTPS 通信层和 VerifiedPayment 编排。
- [x] 服务端创建/查询/确认订单、会话认证、服务端定价和跨账号保护。
- [x] 内存/SQLite 订单存储、幂等请求、交易防重复与发货恢复测试。
- [ ] 真实平台收银台、支付凭据管理、验签/通知/查单及部署接入。
- [ ] 真正支付到幂等发货的端到端闭环；Mock 不视为支付完成。

## 第六阶段：抖音、TikTok 与升级（部分完成）

- [x] 官方 BGDT 安装抖音 TTSDK 6.9.7，隔离工程编译与版本快照。
- [x] 抖音登录/存储/分享/工厂和官方初始化桥接；真实 DLL 与注入行为验证。
- [x] TikTok 登录/存储/分享/工厂的 Runtime/Editor DLL 编译与行为验证。
- [x] 抖音、TikTok 服务端登录验证器及模拟上游回归。
- [x] 官方 SDK 全文件哈希快照、升级差异报告、版本矩阵及升级说明。
- [x] TikTok 初始化桥接、本地 Runtime/Editor DLL 返回码核对与注入测试。
- [ ] TikTok 干净官方插件核验与工程集成。
- [ ] 三平台升级后的真机回归、账号授权及后端联调。
- [x] Android/iOS 扩展接入说明及自定义身份、装配、所有权和配置拒绝边界测试；本轮不接原生供应商。

## 第七阶段：跨游戏接入与发布（验收待办）

- [x] 空白隔离 Unity 工程导入、编译、Editor 检查与 Mock Play Mode 验证。
- [x] 持续提交并推送实现、接口说明、工作记录和测试脚本。
- [ ] 第二个独立游戏接入与环境/数据隔离的完整验收。
- [ ] 新项目按文档完成真实平台构建与运行的全流程验收。
- [ ] 全部必需接口文档/示例复核，完成最终兼容矩阵并发布稳定标签。

## 验证记录怎么看

公共回归入口为 `tests/run-all-tests.ps1`；平台测试与版本边界见 [兼容矩阵](compatibility/README.md)。Unity 检查模板在 `tests/*Checks.cs.txt`，具体执行结果见下面对应记录。还未完成的真机、真实支付和正式构建不会标为已完成。

## 2026-11-23 执行记录

### 面板 Play Mode 视觉验收与错误原因补全

在独立 UnityProject 的实际 SDK 面板刷新最新代码，验证进入 Play Mode 后初始化 Mock、登录成功、数据上报完成和关闭网络模块的拒绝结果；退出后操作按钮恢复禁用。发现登录、网络、分享、广告、支付和存储预览只显示错误码，遗漏 SDK 已提供的 Reason；现统一显示 Code/Reason，并给上报成功及失败结果补充 CallId。对 Reason 与 Code 相同的 MODULE.DISABLED 增加勾选模块后重新初始化的提示，避免重复显示同一个码。修改后重新编译并实际点击验证网络禁用提示与上报 CallId。此处只调整面板诊断显示，不改变上层错误契约或平台结果。其他模拟分支、全部按钮、跨平台选择和构建选择的完整视觉验收仍待补，因此顶部清单仍为 63/78。


### 修复平台自动注册被链接器裁剪

实际检查官方转换的 ManagedStripped 发现 ErDon.UnitySDK.WeChat 缺失，虽然构建前 Player 程序集列表包含该程序集；此前“构建/转换成功”不能证明注册入口保留。为三个平台注册文件加入 AlwaysLinkAssembly，确保只引用核心注册表的游戏也让链接器处理平台 RuntimeInitializeOnLoadMethod。该标记不会导入未参与编译的平台，也不等于保留所有代码。

微信重新执行开发构建，wechat-linker-registration.log 显示成功与exit0。新增 tools/check-linked-platform.ps1/CheckLinkedPlatform.cs，使用Cecil读取链接后元数据，确认微信适配器与带启动属性/方法体的Register保留，拒绝其他ErDon平台程序集/引用和Editor SDK。实际微信结果通过；错目标与空目录拒绝测试通过。随后官方转换输出到新的时间戳目录，wechat-conversion-linked.log出现SDK_WECHAT_CONVERSION_FILES_PASS，exit0，本次ManagedStripped检查再次通过。抖音/TikTok标记尚未经过各自实际构建，不沿用微信证据。最终JS/Wasm/官方资源完整隔离与真机仍待验收。

### 微信官方包七项契约回归

新增 run-wechat-official-tests.ps1，使用固定提交915d4bf的完整官方插件及同包Unity编译Wx.dll，显式启用字符串上报分支；登录测试不再用WX入口替身，改为编译真实WX入口（行为仍注入）。发现新版本失败DTO为LoginFailCallbackErr，旧版本为RequestFailCallbackErr，按测试基线选用实际类型。七项凭证/工厂/存储/分享/广告/初始化/字符串上报回归全部通过；旧0.1.23登录回归也通过。官方克隆git status为空。兼容矩阵拆分本地和官方提交，明确来源、构建/转换证据与最终裁剪/真机待验范围；没有把注入初始化成功码视为真实初始化证明。

### 微信官方字符串上报兼容与转换通过

新增 ERDON_WECHAT_STRING_ANALYTICS 显式编译选项，适配官方固定提交915d4bf的字符串字典上报签名；数值参数返回 ANALYTICS.PARAMETER_TYPE_UNSUPPORTED，零平台派发，不偷偷转成文本。原本地0.1.23仍用泛型路径。新专项使用官方 Wx.dll 编译并验证数字拒绝、字符串/空事件、快照及释放，WECHAT_STRING_ANALYTICS_PASS；旧基线工厂/上报/广告/初始化四项回归继续通过。

转换探针增加新版本 relativeDST 字段兼容。wechat-official-conversion-path.log 输出 Build Finished Success、SDK_WECHAT_CONVERSION_FILES_PASS，正常退出码0。生成小游戏 game.js/game.json/project.config.json、framework、data-package、wasmcode 等目录；game.json与项目配置解析通过。使用完整官方副本、占位App ID，没有登录、上传或真机运行。此次可标记固定提交的开发构建与转换完成，不能标记最终平台/模块裁剪或真实运行完成；后续需更新兼容矩阵并继续全能力回归及最终产物验收。

### 微信官方转换与完整包核验

在隔离 SDK 副本运行官方 WXConvertCore.DoExport(true)，发现本地 0.1.23 缺少 wx_perf_2022.a/2021.a，ProcessWxPerfBinaries 抛空引用，退出1。新增隔离转换探针，约束输出到测试工程新目录，预检所需插件导入器；不修改原游戏包。随后克隆官方仓库提交 915d4bf4f60ef0cd1c29316a8d67257185d609a7 并保留干净原件，使用另一个副本导入。完整官方包包含缺失库，package.json 版本为0.1.1；重新编译发现 ReportEvent 参数从本地基线的泛型字典变为 Dictionary<string,string>，WeChatAnalytics CS1503。记录两个不同基线，不拼接包、不静默改变数字事件语义。后续需实现显式版本兼容策略并验证，再继续官方转换；转换/真机清单未勾选。

### 微信 WebGL 开发构建与配置持久化修复

新增可重跑的 MiniGameDevelopmentBuild/MiniGameBuildProbe 模板及 docs/development-build.md，在全新 WeChatBuildProject 导入核心、微信适配和本地 0.1.23 包。配置和构建分两次 Unity 进程执行。首次构建发现 SdkProjectSettings 与 SdkSettingsWindow 放在同一脚本文件，保存的 m_Script 为 0，重开后读取默认 Mock，正确被构建检查阻断。将配置类移入同名 SdkProjectSettings.cs 并提交稳定 meta；重新保存后有有效脚本 GUID，重开工程正确读取微信配置。

修复后的 wechat-development-build-fixed.log 输出 SDK_DEVELOPMENT_WEBGL_BUILD_PASS，Build Finished Success，正常 exit 0；本次结果 Succeeded errors=0。生成 DevelopmentWebGL.data 2314541 字节、framework.js 654141 字节、loader.js 46217 字节、wasm 15769598 字节。last-artifact-audit.txt 的目标为 WeChat，登记程序集与 BuildReport 来源检查无错误。探针引用真实启动和按需工厂，未调用登录、展示广告、分享、事件上报或支付。

这只证明本地包的 Unity WebGL 开发编译与现有范围审计，不证明官方包来源、微信转换后的最终产物裁剪、SDK 初始化成功或真机行为。旧开发版无效脚本引用配置需核对、重新填写保存并重启确认，未实现自动迁移。完整平台验收项保持未勾选。

### 开发构建与正式发布校验分离

原统一阻断同时禁止了用于收集产物证据的开发构建。现 SdkBuildGuard 只在实际 BuildReport 包含 Development 标志时采用开发配置校验，且必须 production=false；空报告、正式构建和生产配置继续阻断。目标宏、WebGL、目标适配器存在性、官方目录及外平台程序集检查全部保留，Mock 仍禁止进入 Player。配置校验增加便携配置约束，开发模式不能放行尚未支持的原生平台。面板默认检查仍展示正式构建限制，不自动修改 Unity 构建选项。

新增 Editor 检查覆盖有效微信开发配置、正式构建拒绝、生产配置拒绝、缺少 App ID 和原生平台拒绝。新建隔离 BuildChecksProject 运行 SdkEditorChecks.Run，输出 SDK_EDITOR_CHECKS_PASS 并进入正常退出流程；不据此宣称实际 WebGL 或官方小游戏导出成功。下一步在隔离平台工程执行开发构建并检查产物。

### 构建目标适配器存在性检查

排查构建前置条件发现原隔离策略只拒绝外平台内容，没有确认当前目标适配器进入 Player 编译。新增 CheckTargetAdapter：微信/抖音/TikTok 必须有唯一的对应 UPM 清单、有效版本/路径声明及正确适配程序集声明，并且 CompilationPipeline 的 Player 源程序集包含该适配器；Editor DLL 或宏存在不能替代。SdkBuildGuard 接入此检查。专项测试覆盖有效微信、缺清单、缺 Player 程序集、重复清单、错平台、Mock 和声明错误，通过；隔离 Unity Editor 重新编译生成程序集，日志无 C# 编译错误。原真实构建阻断仍保留，此检查不证明官方导出成功或最终包裁剪。

### 配置面板视觉检查增量

导出边界修复：原按钮未校验便携配置，会将缺失标识或生产 Mock 直接写成 JSON 并报告成功。现先创建配置快照并执行运行时配置校验，拒绝时显示 CONFIG 错误码；取消保存明确提示未写入，常见文件写入异常显示目录/占用/权限提示。该校验不等于依赖或真实构建验收。Unity 实际操作确认生产 Mock 被 CONFIG.PRODUCTION_MOCK 弹窗拒绝，恢复测试环境后可进入保存对话框，取消后显示“已取消导出；未写入文件”。写入异常分支尚未做故障注入，不宣称已验证。

在隔离 UnityProject 实际打开面板，修正默认浮窗过窄、激励广告/分享/存储仍标为 Editor Mock，以及预览只显示登录开关的问题。面板最小尺寸设为 480×600，预览列出全部勾选能力，并提示修改后重新初始化。Unity 重编译后实际观察标签与预览；勾选数据上报后预览立即变为“登录、数据上报”，填写测试 Game ID 后缺失提示消失。通过保存对话框导出 .tests/panel-visual-export.json，解析核对平台、Game ID、七个模块及 production 与面板一致，输出 PANEL_EXPORT_MATCH_PASS。恢复浮窗后检查文字换行与滚动可达。未观察到 C# 编译错误；日志仍有 Unity 授权刷新与包注册表网络问题，不将其描述为完整无错误运行。

本次只验收编辑态预览、输入校验展示与一次有效 JSON 导出。全部 Play Mode 按钮、取消导出/无效配置导出及跨平台面板交互仍待补验，因此顶部整项视觉验收保持未勾选。

### 错误诊断文档实现核对

重写 docs/errors.md 的草案部分，逐项对齐 SdkError 实际字段，去掉 MessageKey/Message/Platform 等不存在属性与 PAYMENT.STATUS_PENDING 旧设计码；区分本地 AUTH.CANCELLED、平台授权失败、服务端凭证兑换失败、会话无效和限流。明确 Mock 错误不是生产平台精确映射，TikTok 上报 fail 不提供细分原因，配置异常和业务结果是不同通道。补上层登录失败处理示例、各能力错误文档导航及真实面板功能边界。字段存在性和本地链接核对通过，本次无运行时代码变更。

### 网络接口文档与新项目入口复核

新增 docs/network.md，按当前实现说明可选 HTTP 包装配、GET/POST 示例、HTTP 状态与传输失败差异、快照/限额、取消/超时、主线程及 host 生命周期，明确小游戏平台限制与尚未完成的验证范围。修正 getting-started、README 和核心 package.json 中“平台适配尚未实现”等过时描述，增加能力文档导航。修改文档的本地链接与 package.json 解析检查通过；本次只改文档/包描述，不重复运行行为测试，也不将示例片段视为独立工程验收。

### 原生渠道扩展边界验收

新增 docs/native-extension.md，明确现有能力工厂与 IPlatformRuntime 的手动启动路径、原生渠道配置尚未开放、后续供应商包/原生插件/构建隔离与真机验收要求。新增 NativeExtensionTests，以 Android.ExampleChannel 和 iOS.ExampleChannel 测试替身验证等待就绪后装配、聚合存储多态、模块关闭不创建、身份不匹配拒绝和所有权释放，同时确认便携配置仍拒绝自定义原生平台。公共回归通过。此项按本轮范围仅验收扩展接口，不含供应商或原生构建。README 同步当前三平台适配、上报及真实构建阻断状态。

### 网络与上报操作句柄生命周期修复

Unity HTTP 后续复核：底层 Operation 原先在完成后仍保留 Callback 和 Owner，长期保存 INetworkOperation 会间接保留业务闭包与宿主。现完成时先断开这两个引用，再通知回调；资源 Cleanup 先清空字段并用 finally 保证接收缓冲清理，已释放实例的拒绝路径也清理句柄。扩展 NetworkPlayChecks，验证取消、Dispose、已释放请求、未激活宿主与真实本机连接超时后 Owner/Callback/Request/Download 均为空。隔离 Unity 输出 SDK_NETWORK_PLAY_PASS，无 C# 编译错误。外部 host 销毁仍需调用 Shutdown 的约定不变。

复核发现终态句柄仍持有底层操作和派发器，且取消结果若同步派发、上层回调立即关闭 SDK，原顺序会先释放平台实例再取消底层操作。现先取消底层观察再派发取消/超时结果；完成、关闭以及同步回调早于句柄返回的路径均清理操作/回调/派发器引用。新增 OperationLifecycleTests，验证回调内关闭的取消顺序、完成句柄惰性、同步完成后附加句柄不残留，以及共享能力实例关闭时各操作只取消一次。公共回归全部通过；此子项不替代剩余生命周期与全部输出路径最终审核。

### 异步上报聚合增量

Mock 与 Unity 增量：新增 EditorMockAnalytics 和面板结果选择/ReportEvent/取消按钮，Mock analytics JSON 和按需工厂已开放；UnitySdkHost 直接初始化增加可选 analyticsProvider。公共回归通过，隔离 Unity 工程输出 SDK_ANALYTICS_PLAY_PASS，覆盖成功、失败、Update 超时、取消、模块关闭及关闭后排队回调抑制。首次导入出现文件导入次序导致的临时类型缺失，后续重编译并完成测试；重开同工程执行 Editor 检查输出 SDK_EDITOR_CHECKS_PASS，未出现 C# 编译错误。面板视觉交互与真机入库不视为通过。

新增 SDK.ReportEvent(event, callback, timeoutMilliseconds) 跨平台入口，微信/抖音返回派发结果，TikTok 等待平台回调；单次独立取消/默认15秒超时、结果快照、主线程派发恢复与 Shutdown 清理。TikTok 工厂现按需创建异步能力，运行时 JSON 允许 analytics=true；旧同步重载在 TikTok 返回 ASYNC_REQUIRED，不虚报成功。公共异步测试和 TikTok Runtime/Editor DLL 工厂/行为测试通过，文档补跨平台示例。Mock 面板与 Unity Play Mode/真机仍待验收。

### 抖音数据上报适配增量

核对官方 6.9.7 ReportAnalytics 泛型接口和 JsonData 转换路径，实现 DouyinAnalytics 与按需工厂，开放抖音 analytics 配置。发现官方桥接内部字段 cp_event_key_name 会被同名业务参数覆盖，因此适配层拒绝该参数。实际 DLL 编译、官方 JsonData 序列化、注入参数映射/异常/释放与平台回归通过；未进行真机入库验证。TikTok API 为不同的 ReportEventParam 形式，仍待单独适配。

TikTok 后续增量：核对本地 1.1.6-Release 两套 DLL 的 ReportEventParam、桥接实现及回调转发。与微信/抖音不同，该接口支持异步 success/fail/complete；新增 IAsyncAnalytics/IAnalyticsOperation 和 TikTokAnalytics 底层实现，等待平台结果、首个信号胜出、支持本地取消与释放抑制、空参数明确编码为对象。两套 DLL 的编译/JSON/注入行为测试通过。fail 回调不携带错误码，平台超时同样转发 fail，不伪造更细的原因。现有同步聚合入口尚未接通此异步能力，TikTok analytics 配置仍阻断；下一步补异步聚合和本地超时。干净官方插件与真机验证仍待办。

### 数据上报契约增量

核对本地微信 0.1.23 的 WX.ReportEvent 泛型入口，其 void 返回值不提供接收确认。新增 AnalyticsEvent 不可变快照、受限标量参数、IAnalytics/可选工厂及 AnalyticsResult.Dispatched，明确不代表服务端入库。新增契约测试并纳入公共回归。此增量尚未开放 analytics 配置，聚合入口、平台实现和真机验证继续待办，说明见 docs/analytics.md。

后续聚合增量：实现 SDK.ReportEvent 和手动初始化 AnalyticsEnabled 开关、按需工厂、启动配置快照及可选网络工厂的能力转发。新增所有权、失败回收、重复实例、重入、结果快照与日志关联测试，公共回归全部通过。三平台实现和面板尚未接通，运行时 JSON 仍拒绝 analytics 开关；不标为真机上报完成。

微信适配增量：新增 WeChatAnalytics，核对官方 ReportEvent 泛型方法使用 LitJson 序列化字典。实际官方程序集编译和序列化、注入参数映射/异常/释放测试通过，微信工厂按需创建，运行时 JSON 现允许微信 analytics=true；其他平台和 Mock 仍拒绝。面板更新支持范围标签，整体真实构建阻断保留。公共回归通过，真机数据送达尚未验收。

第一阶段进行中：已实现 UPM 骨架、聚合登录入口、IAuth 多态、统一结果、格式化日志与回调回退、Unity 主线程桥接、Editor Mock 示例。核心行为测试通过：重复回调、释放后旧回调、凭据脱敏、日志字段、回调异常回退、模块关闭、未初始化。

待补：统一超时/取消、完整诊断字段、回调重入边界、初始化/释放异常日志的一致格式、Unity 实际导入与生命周期回归。当前不发布稳定标签，也不宣称第一阶段验收完成。

### 第一阶段增量：登录完成边界

已补默认15秒超时、自定义超时、CancelLogin、首个完成信号胜出、迟到回调忽略、用户回调异常隔离、日志重入阻断及释放日志。Unity Update 驱动超时，不依赖后台计时线程。新增取消/超时/回调异常测试通过，原核心测试回归通过。

仍待 Unity 实际导入、完整错误字段和配置/裁剪系统；不宣称完成第一阶段验收或真机验证。

### 第一阶段增量：Unity 导入与错误关联

已增加 LoginResult/Error 的 CallId、操作、阶段及平台原始诊断字段，新增错误与日志关联测试。修复 Unity 回调调度捕获可变静态宿主的问题。空白 Unity 2022.3.14f1c1 工程通过本地 UPM 依赖导入 SDK，生成 ErDon.UnitySDK.dll。最终增量与 Mock 示例进行批处理编译验证；不是 PlayMode 或真机功能验证。

剩余：完整重试策略/traceId、运行模式生命周期回归，随后配置面板与构建裁剪。

最终验证结果：SDK 与 MockLoginExample 在空白 Unity 工程批处理编译通过，分别生成 ErDon.UnitySDK.dll 与 Assembly-CSharp.dll，无 C# 编译错误。首个验证进程在退出阶段停留导致第二次启动工程占用，结束本任务的验证进程后重试成功。可用 tests/run-core-tests.ps1 重跑核心测试。未进行 PlayMode 与真机测试。

### 第二阶段启动：配置面板预览

已增加 Editor 专属程序集、项目级配置保存、平台与模块选择、配置检查/预览和 Player 构建阻断。当前配置未连接运行时装配，模块勾选显示计划状态；所有真实平台仍标记未实现。开发版没有可用于 Player 的适配器，因此主动阻断 Player 构建；不宣称完成平台裁剪。

Editor 检查源码保存在 tests/SdkEditorChecks.cs.txt，可复制到空白测试工程 Assets/Editor/SdkEditorChecks.cs，通过 -executeMethod SdkEditorChecks.Run 执行。验证 Editor Mock 合法配置、生产 Mock、未实现模块、缺失真实适配与构建阻断。

验证结果：Unity 2022.3.14f1c1 编译 Editor 包通过，执行 SdkEditorChecks.Run 输出 SDK_EDITOR_CHECKS_PASS；核心回归测试通过。配置界面尚未做交互视觉验收，尚未构建实际平台产物。

### 配置连接与 Play Mode 验证

配置面板已连接 EditorSdkBootstrap：读取登录开关并创建 EditorMockAuth，支持成功、取消、拒绝、限流及无回调超时。新增初始化/调用/取消/释放按钮。Mock 位于 Editor 专属程序集，不参与 Player。

实际 Unity Play Mode 检查输出 SDK_PLAY_CHECKS_PASS，验证成功、模块关闭、Update 超时及拒绝。注意该轮导入期间有一次脚本文件写入顺序触发的瞬时类型缺失，Unity 后续重编译并完成 Play 测试；最终文件还需再次干净导入确认。补齐基础重试元数据与 TraceId（目前同 CallId）；跨服务端传播待实现。修正测试回调内断言被 SDK 隔离的隐患。文档统一 UTF-8。

持续执行中：未完成阶段不打勾；下一步完成依赖隔离和真实平台凭证/服务端协议，平台真机及支付验收仍需有效外部环境。

### 范围确认与平台隔离

用户确认首批真实平台为微信、抖音、TikTok；Android/iOS 本轮只保留扩展接口，不接具体供应商。原计划第六阶段据此调整，后续再单独排原生渠道迭代。

新增独立平台清单策略、UPM 包版本匹配、Player 程序集检查和 BuildReport 来源审计。测试覆盖非目标程序集、JS/资源、路径边界、目录跳转、浮动版本。审计目前仅针对登记内容，原生二进制/最终合并 JS/StreamingAssets 完整覆盖待实现，尚无真实平台裁剪验收证据。

隔离增量验证：核心测试、独立隔离策略测试通过；Unity 2022.3 编辑器批处理输出 SDK_EDITOR_CHECKS_PASS 并正常 exit 0。未执行真实目标平台构建。

### 登录凭证与服务端会话分离

新增 IPlatformCredentialProvider、ISessionExchange、VerifiedAuth：平台 code 必须经服务端交换才返回会话；拒绝空会话、重复授权/交换回调，释放后不派发旧结果。错误区分 PlatformAuthorization 与 SessionExchange。

新增可选 UPM 包 com.erdon.unitysdk.transport.http，使用 HTTPS POST 交换协议，禁止重定向和凭证日志；核心不强制依赖 UnityWebRequest。网络请求未做实际 HTTPS 联调，取消 SDK 本地等待不保证取消服务端处理。

验证：tests/run-all-tests.ps1 的核心、登录编排及隔离测试全部通过。Unity 双包导入/编译、Editor 检查通过并退出码0。官方凭证适配、服务端真实校验、真机登录仍未完成。

### 微信凭证适配增量

新增独立微信 UPM 包，锁定官方 0.1.23，封装 WX.Login；只返回临时凭证，经 VerifiedAuth 组合后端验证才产生登录结果。支持 SDK 超时传参、重复回调隔离、释放后回调忽略和结构化 errno；任意原始 errMsg 不外泄。登记平台程序集/资源来源并使用独立平台宏，官方二进制未提交到仓库。

验证：使用本机官方 wx-runtime-editor.dll 的真实回调类型编译并运行契约测试，输出 WECHAT_CREDENTIAL_TESTS_PASS；公共核心、登录编排、隔离测试全部通过。WX 调度入口在测试中替换，完整官方运行时集成、后端和真机仍待验证。详见 docs/wechat.md。

### TikTok 凭证适配增量

新增独立 TikTok UPM 包及 TT.Login 凭证适配，按官方 1.1.6 Release 文档和本机 DLL 核对。分别引用 Runtime/Editor DLL 编译真实调用签名并执行契约测试，均输出 TIKTOK_CREDENTIAL_TESTS_PASS；公共回归通过。行为测试使用注入调度，不代表真实容器成功。

发现本机 TikTok 插件 package name 仍为 com.bytedance.starksdk 且导入设置含旧项目宏，不把这两个标识当作跨游戏依赖。独立适配不引用 RS 项目，使用专用平台宏，要求安装官方干净插件。补充安装、初始化顺序、错误/超时、升级及验证边界文档，更新 README 避免继续声称纯设计阶段。

### 公共服务端登录增量

新增无第三方依赖的 Node 参考服务：受信绑定、平台验证器扩展、微信固定上游校验、密码学随机会话及摘要存储、过期/撤销、同应用 code 原子重放拦截、HTTP 边界与脱敏日志。默认启动仅允许显式本地开发模式，内存存储仅用于联调；提供共享存储契约。

六项 Node 测试通过，包括真实 loopback HTTP 请求，平台上游使用模拟响应。首次 socket 测试受沙箱 EACCES 限制，授权后同一套测试全部通过。微信官方文档页当前不可读取，平台验收、共享持久化、Unity HTTPS 联调、跨端 trace 和错误细化仍未完成。

### 服务端失败原因与客户端日志关联

新增可独立测试的 SessionResponsePolicy；HTTP 层将已登记的服务端错误码/HTTP 状态映射成具体原因及重试建议，未知正文不回显。LoginResult/SdkError 保留经过 UUID 格式校验的 ServerCallId，SDK 完成日志同时包含客户端 CallId 与服务端编号。新增 HTTP 交换参数检查。

验证：核心、响应策略、登录编排和隔离回归全部通过。Unity 2022.3 独立工程实际编译并输出 SDK_EDITOR_CHECKS_PASS，正常退出码 0。此轮未做 HTTPS 网络联调；只完成安全错误映射与两端编号关联，不宣称完整 tracing 已实现。

### 会话持久化增量

新增 SqliteAuthStore，实现持久会话、撤销、过期清理和事务化 code claim；独立于 AuthService，可通过 SDK_AUTH_DB 显式选择。Node 基线更新为已验证的 24.20.0。四项持久化测试通过，包括四个 worker 的独立数据库连接竞争，以及关闭/重开后的身份与撤销校验。修复测试清理顺序，确保先关闭连接再移除临时数据库。

SQLite 适用于同机持久化，不宣称实现多机共享存储；同步 API 性能、备份恢复、真实平台及部署验收仍待完成。

### 生命周期回归修复

检查发现复用当前 provider 重新初始化会先释放再继续使用，以及 Dispose 可重入改变全局实例的问题。现明确 provider 所有权，拒绝当前实例复用，阻断初始化/释放期间重入，旧回调不再投递，新实例不接收旧队列结果。Dispose 失败输出统一格式错误，不泄露异常正文；普通结果回调中重新初始化仍受支持。公共入口也拒绝空 session 成功结果。

五组独立回归全部通过，包括新增生命周期测试；文档补充初始化/释放约束。此增量为核心行为验证，未扩大到真机验证声明。

### 存储能力与配置预览

新增 IStorage 多态接口和 SDK.SetStorageSync/GetStorageSync/RemoveStorageSync 聚合入口；统一结果、错误阶段/CallId、参数长度脱敏日志、开关和所有权管理。配置面板勾选存储后装配 Editor 内存实现，提供读写删除预览；关闭不创建实现，不调用存储能力。

六组独立回归通过。Unity 2022.3 实际 Play Mode 输出 SDK_PLAY_CHECKS_PASS，验证存储配置读写、删除与关闭，以及既有登录成功/失败/超时。详见 docs/storage.md。真实平台持久化适配及完整裁剪仍待实现，内存 Mock 不冒充平台存档。

### 微信存储适配

新增 WeChatStorage，封装本机官方 SDK 的四个字符串存储方法，按游戏/环境隔离键并给值加版本前缀，读取区分空值/不存在/格式不兼容，写入和删除后验证观察到的状态，Dispose 不擦除持久数据。

测试引用现有 Unity 工程编译的 Wx.dll 校验调用签名，注入字典与失败行为验证隔离、持久语义及错误处理，输出 WECHAT_STORAGE_TESTS_PASS。这不是官方干净工程编译或真机验收；官方 HasKey 帮助方法的底层错误识别限制已写入文档。

### TikTok 存储适配

新增 TikTokStorage，通过 TT.PlayerPrefs 实现同一 IStorage 契约，写入/删除后调用 Save，保持命名空间、格式版本和错误语义与微信一致。Runtime/Editor 两套本机官方 DLL 均通过登录和存储接口编译及注入行为测试。实际 Save 落盘、设备重启与宿主配额待真机验证，未宣称持久化验收完成。

### 抖音文档契约与官方下载

用户确认本机没有抖音 SDK，要求从官网适配。核对官方登录接口的三参数成功回调并实现独立凭证适配，拒绝匿名状态与空 code；文档形状替身测试及公共回归通过。该测试不等价于官方 DLL 编译。

沿官网安装指引下载 BGDT 3.0.271，记录 SHA-256，并开始导入独立 DouyinProject。官方工具不提交仓库。下一步通过官方 BGDT 获取 TTSDK 并锁定版本；抖音真实编译、后端与真机仍待完成。docs/roadmap.md 已修正过时的“仅设计阶段”和原生渠道范围描述。

### BGDT 界面检查与版本证据

独立 DouyinProject 已实际打开 ByteGame Develop Tools 面板，多次观察仍为 Loading，未显示登录入口或 TTSDK 列表。没有操作用户现有游戏工程，也没有将此状态误报为 SDK 安装完成。后续继续排查官方工具加载；其他能力实现不受此项阻塞。

新增 compatibility/README.md，将版本、接口编译、Mock/Play Mode、后端与真机证据分列。新增官方本地包文件快照脚本，记录版本及相对路径 SHA-256，覆盖导入设置等可能的本地修改；在 BGDT 3.0.271 包验证生成六项文件记录。快照不宣称官方真实性或平台兼容性。

### 平台宏装配检查

新增纯策略检查平台宏，接入配置预览和 Unity 构建前检查。拦截缺少目标宏、多个/未知平台宏、Mock 携带真实平台宏，以及当前适配不支持的原生目标。保持其他项目宏不变，不自动修改构建设置。独立回归覆盖各个误配置分支并全部通过。真实平台装配与包体裁剪验收仍未完成。

Unity 2022.3 独立工程编译及 Editor 检查输出 SDK_EDITOR_CHECKS_PASS，退出码 0；本轮未进行真实目标构建。

### 抖音后端校验

根据可读取的官方小游戏 code2Session 文档实现 createDouyinVerifier，固定小游戏上游地址，不混用小程序或 TikTok 接口。强制成功状态及账号身份，拒绝匿名/矛盾响应；平台配置错误、凭证拒绝和上游故障分开处理。抽取共用限额 JSON 读取组件，启动器可按受信绑定选择微信/抖音验证器，客户端增加安全配置错误映射。

十五项服务端测试全部通过（含五项新增抖音模拟响应测试），C# 公共回归通过。仍未使用真实 code 调用平台，不宣称抖音登录闭环验收完成。

### 会话限流与等待时间

实现有容量边界的进程内来源限流，超限在凭证兑换前拒绝，返回 429 与 Retry-After。来源采用带随机盐的摘要，不信任转发头。客户端安全解析整数秒数，非法值不传播，不自动重复提交 code。

十八项后端测试及公共 C# 回归全部通过，覆盖窗口恢复、容量、伪造转发头、错误体和重试等待。多实例/反向代理限流仍需部署配置与验收。

### TikTok 服务端身份交换

官方文档恢复可访问后，核对 Minis OAuth 与 Unity Login 协议，实现独立 TikTok 验证器并接入启动器受信绑定。固定表单 POST，无网页 redirect_uri/code_verifier；测试包含特殊字符 code、错误响应、超限、脱敏和游戏会话隔离。22 项后端测试通过。身份校验不保存平台令牌，尚不支持后续支付授权；真实平台闭环仍待验收。

HTTP 客户端 Retry-After 改动经独立 Unity 2022.3 工程编译与 Editor 检查通过，日志 retry-unity.log，退出码 0。

### 按能力配置创建与失败回收

增加 ISdkProviderFactory、SDK.InitializeWithFactory 及 UnitySdkHost 对应入口，未启用模块不创建 provider。创建失败回收本次对象，旧实例继续服务；拒绝复用旧 provider 与创建阶段重入。配置复制后再创建，避免工厂改变开关影响本次装配。Editor Mock 初始化改用该入口。

公共 C# 回归及新工厂行为测试通过；独立 Unity Play Mode 输出 SDK_PLAY_CHECKS_PASS（factory-play.log），覆盖配置预览初始化与登录/存储开关。尚未完成真实平台自动初始化、配置生成或裁剪验收。

### 预编译 SDK 引用隔离

修复构建前只检查源码程序集而遗漏官方预编译 DLL 的问题，加入 Unity Player 编译图的 compiledAssemblyReferences。跨系统路径和大小写规范化后按已登记平台清单检查。公共回归通过；独立 Unity Editor 用真实编译引用验证外部平台依赖被拦截，precompiled-audit.log 输出 SDK_EDITOR_CHECKS_PASS，退出码 0。未引用原生插件、StreamingAssets 与最终合并 JS 仍须后续覆盖。

### 分享公共流程与配置预览

新增 SDK.Share/CancelShare、IShare、不可变 ShareRequest 与 ShareResult；成功语义仅为 Dispatched，不表示送达或可发奖。加入模块开关、可选 IShareProviderFactory、创建失败回收及跨能力对象单次释放。Editor 面板支持勾选与调用 Mock 分享。

公共回归及分享专项测试通过，覆盖禁用不创建、主线程派发、重复回调、并发调用、超时/取消、释放后结果失效和日志脱敏。详见 docs/share.md。平台真实分享适配仍未完成。

独立 Unity 工程 share-play.log 输出 SDK_PLAY_CHECKS_PASS，实际 Play Mode 验证分享发起及模块关闭。

### 微信分享适配

新增 WeChatShare，对接 WX.ShareAppMessage 与官方 ShareAppMessageOption，按本地 DTO 无结果回调的事实只报告已发起；不推断发送/取消/送达。注入行为测试覆盖映射、可选参数、异常、释放及调用者异常不重复回调。

WECHAT_SHARE_TESTS_PASS；现有工程 Wx.dll 与本机 0.1.23 DTO 编译组合出现其他类型 CS1684 警告，因此只记录所用分享入口编译证据，不算整包兼容通过。干净官方依赖重编译、真实宿主分享仍待完成。

### TikTok 分享适配

对照官方 Sharing 文档和本地 DLL 实现 TikTokShare，保留失败数字码、隐藏任意错误原文；成功/失败回调首个胜出，Complete 不代表成功。通用语义仍为 Dispatched，非送达证明。

Runtime/Editor 两套官方 DLL 均通过分享入口编译与注入测试，原登录/存储与公共回归通过。分享回调依赖 netstandard 2.1，修正测试运行环境为可配置 Unity Mono；不再将旧 Framework 加载失败当作平台接口失败。专属模板/路径参数、真实宿主与设备分享仍待验证。

### 订单与发货服务组件

新增 OrderService/MemoryOrderStore，服务端商品定价、已验证会话绑定、作用域幂等下单、支付归属/金额匹配、交易号唯一占用及 Created/Paid/Fulfilled 状态。发货明确使用稳定 deliveryKey，响应丢失不提前标已发货；接收端必须事务去重。未提供默认成功支付验证器。

新增订单测试覆盖篡价/跨账号环境、会话撤销、并发确认、交易复用、响应丢失和容量。当前仅内存订单，没有真实平台支付验证器/HTTP 路由/持久化订单，不宣称完成支付闭环。详见 docs/orders.md。

本轮完整后端测试 29 项全部通过，包括 7 项订单测试；测试没有调用真实支付平台。

### SQLite 订单与发货恢复

新增 SqliteOrderStore：幂等下单、交易号唯一占用、支付状态更新和发货状态提交使用事务；WAL/FULL、状态约束及 schema v1 校验。Paid 订单持久可扫描，Fulfilled 保留幂等键和交易占用。

后端 34 项测试通过，新增 5 项 SQLite 订单测试包括四个独立 worker 抢同一交易号、事务失败回滚和发货响应丢失后的服务重建恢复。仍需正式平台验证器、HTTP 接入、生产备份/性能/故障测试及游戏端持久化去重，未完成支付闭环。

### 订单 HTTP 接口

新增可显式装配的 createOrderHandler，提供会话认证的创建/查询/确认路由，发货不对客户端开放。正文限额、来源限流、禁止 URL token、错误码/callId、no-store 与脱敏日志。默认启动器不加载模拟支付验证器。

37 项后端测试全部通过，含真实回环 HTTP 的篡价、跨账号、撤销、未验证支付拒绝、发货路由不可访问、格式/大小限制与日志失败隔离。真实平台验签、Unity 支付传输/能力入口和端到端闭环仍待完成。

### Unity 订单通信层

新增引擎无关 IOrderClient/订单结果与状态，及可选 HTTP 包 UnityOrderClient：创建、查询、确认，HTTPS 会话头、限额、禁重定向、取消释放。响应核对订单/商品、金额与状态，安全映射错误及 ServerCallId。Success 不等于付款或发货。

公共回归与订单协议测试通过；尚未接 SDK.Pay 聚合、平台收银台和真实 HTTPS 联调。Confirm 暂使用字符串回执，真实平台验证器仍需实现。

独立 Unity 2022.3 工程 order-client.log 编译与 Editor 检查输出 SDK_EDITOR_CHECKS_PASS，退出码 0。

### 客户端支付编排

新增 IPay、IPaymentLauncher、VerifiedPayment，按服务端订单状态恢复流程。Paid 不重复拉收银台，Fulfilled 才成功；平台取消/失败先查单处理竞争。各阶段首回调胜出，核对确认结果订单/商品/金额，释放后不派发旧结果。

公共回归和支付编排测试通过，覆盖恢复、重复回调、取消竞争、换单和所有权。尚未接 SDK.Pay 聚合、超时/取消、配置面板及真实平台 launcher；详见 docs/payment-pipeline.md。

独立 Unity payment-pipeline.log 输出 SDK_EDITOR_CHECKS_PASS 并以 0 退出，验证编译和 Editor 检查。

### SDK.Pay 聚合与支付配置预览

新增 SDK.Pay/CancelPay、PayEnabled 和可选 IPayProviderFactory；配置未启用时不创建 provider，支持统一生命周期/失败回收。支付等待默认 60 秒，可本地取消，迟到结果不重复派发；不声称撤销平台交易。日志由 SDK 格式化并整体脱敏，结果包含 CallId/TraceId 和服务端关联号。

Editor 面板增加支付开关与 Fulfilled/Pending/Cancelled/Timeout 预览，仍仅为 Mock。公共回归与新增 PayTests 通过，覆盖禁用、并发、超时/取消、旧结果、释放和日志。真实平台支付参数、收银台和验签闭环仍未完成。

独立 Unity Play Mode 的 pay-facade-play.log 输出 SDK_PLAY_CHECKS_PASS，实际验证已发货、待发货和禁用支付配置。

### 抖音官方 TTSDK 安装与真实 DLL 契约验证

通过官网 BGDT 3.0.271 的 cp 模式安装 TTSDK 6.9.7，真实路径 Assets/Plugins/ByteGame/com.bytedance.starksdk。补充平台审计的实际根目录和程序集，记录官方 Any Platform 导入设置风险。安装包不提交；本地生成 142 文件哈希快照。

新增 run-douyin-tests.ps1，直接引用官方 DLL 编译登录适配器，测试中排除同名替身；注入回调覆盖匿名拒绝、空凭证、重复回调、释放及脱敏。官方 DLL 契约与全部公共回归通过。另建独立 DouyinCompileProject 验证完整官方插件集成。未验证真机、初始化及真实后端兑换，真实构建仍阻断。

完整官方插件和适配层在 Unity 2022.3.14f1c1 独立工程编译加载通过，douyin-verify.log 输出 DOUYIN_UNITY_COMPILE_PASS，退出码 0。首次启动的命令行 extraScriptingDefines 没有生成适配程序集；改为显式设置测试工程 Standalone 宏，再重开验证 Type.GetType 成功，避免把未参与编译误记为通过。

### 抖音存储适配

新增 DouyinStorage，以官方 TTSDK 6.9.7 的 TT.PlayerPrefs 实现 IStorage；保持游戏/环境作用域、格式标记、Save 后回读、幂等删除与安全错误映射。模块关闭时不创建实例，Dispose 不删除持久数据。文档说明账号隔离和回读不代表重启持久性。

官方 DLL 编译及注入存储行为测试通过；独立 Unity 工程 douyin-storage-verify.log 输出 DOUYIN_UNITY_COMPILE_PASS 并以 0 退出，检查确认 IStorage 实现已加载。真机宿主配额和重启持久性未验证。

### 抖音分享适配

按 TTSDK 6.9.7 实际 JSON + 成功字典/失败文本/取消回调接口实现 DouyinShare，显式引用 TTLitJson。上层仍为 SDK.Share；添加安全错误映射、首回调胜出、释放抑制与可选字段省略。未解析自由文本错误码，不把成功当送达证据。

官方 DLL/JSON 源码编译与注入行为测试通过；Unity 独立工程 douyin-share-verify.log 输出 DOUYIN_SHARE_UNITY_COMPILE_PASS，退出码 0。官方 JSON 源码仅有 CS3021 注解警告。已更新接口文档；官方网页正文未能读取，不宣称真机字段限制和分享结果已验证。

### 公共层调度异常恢复

修复登录/分享/支付在派发完成回调时调度器抛异常导致 completed 已设置、busy 永远无法清除的问题。新增线程安全恢复队列，在 SDK.Tick 主线程交付原结果；执行门控兼容先入队再抛错，代次校验阻止旧实例回调。日志 SDK.DISPATCH_FAILED 关联原 callId，不泄露异常文本，不重试平台动作。

新增 DispatchTests，覆盖后台线程抛错、入队后抛错、三能力完成、后续请求恢复、去重、Shutdown 和日志脱敏；全部公共回归通过。该保证限于聚合完成派发，不声称修复任意自定义 provider 内部的线程调度。

独立 Unity dispatch-recovery.log 输出 SDK_EDITOR_CHECKS_PASS，退出码 0。

### 结果与错误快照隔离

修复 provider 复用结果或共享 SdkError 时，后续调用覆盖上层历史 CallId/Operation 的问题。登录/分享/支付在接受结果时复制，聚合交付前独立复制错误；存储同步结果同样隔离。不可变订单对象保留引用，无需复制。

新增 ResultSnapshotTests：覆盖异步排队前后的 Retry 数据、重复登录历史 ID、跨能力共享错误、存储结果复用和调用方修改隔离。调整旧测试为检查返回错误的关联 ID，不再要求 SDK 修改 provider 原错误。全部公共回归通过。
独立 Unity result-snapshot.log 输出 SDK_EDITOR_CHECKS_PASS，退出码 0。

### 抖音内置能力工厂

新增 DouyinProviderFactory，聚合已验证的登录/存储/分享实现。登录按需创建后端 exchange 并由 VerifiedAuth 接管，关闭时不创建；每次能力返回新实例。缺失后端工厂/null 结果明确拒绝；不提供未完成的支付实现。

官方 DLL 测试新增工厂配置和所有权场景并通过。接入文档给出 UnitySessionExchange + UnitySdkHost 的装配示例，明确官方初始化前置条件与协程宿主生命周期，未将此视为面板自动装配完成。
独立 Unity douyin-factory.log 完成适配程序集编译，检查通过，退出码 0。

### 微信与 TikTok 内置能力工厂

补齐 WeChatProviderFactory/TikTokProviderFactory，使三平台统一使用配置驱动的工厂装配。各工厂只引用自己的平台实现；登录后端按需创建并移交 VerifiedAuth，存储/分享实例独立，未实现支付明确拒绝。新增初始化示例和宿主生命周期说明。

TikTok Runtime/Editor 两套 DLL 的登录/存储/分享/工厂测试均通过。微信工厂配置/生命周期测试通过，但本机编译 Wx.dll 与 0.1.23 DTO 的其他类型 CS1684 警告仍在，不宣称完整版本兼容或干净工程验收。平台初始化、面板自动装配及真机验证继续待办。

### 微信 0.1.23 隔离重编译

从本地 0.1.23 包源码创建隔离 Unity 工程，未使用旧项目 Library；启用微信宏后完整官方源码及 SDK 适配编译加载通过，wechat-clean-verify.log 输出 WECHAT_SHARE_UNITY_COMPILE_PASS，退出码 0。

使用新生成 Wx.dll 重跑工厂和分享测试通过，旧组合的 CS1684 警告消失。脚本改用 Unity Mono，新增可重跑的 Unity 检查源码。仍不声称本地包官方完整性、初始化、导出或真机完成。

### 官方 SDK 升级快照比较

新增 compare-sdk-inventory.ps1，生成前后版本与新增/删除/修改文件报告，按 DLL、C#、asmdef、meta、jslib 分类引导回归。拒绝混平台/混包、路径碰撞和无效输入，保护输入快照；不以哈希或版本号自动认定兼容。

新增快照工具测试并并入 run-all-tests；测试覆盖三类差异、相同基线、坏路径/哈希/重复项、平台不一致及禁止覆盖。使用本地微信 0.1.23 的 334 文件快照实际比较通过。报告覆盖范围仍为 export-sdk-inventory 选择的文件，非完整包认证或语义兼容性分析。

### 升级快照完整覆盖与初始化资料核对

检查微信初始化：本地 WX.InitSDK 接收 Action<int>，但源码/XML 未解释整数语义；官方 GitHub 仓库查询返回 403，网页搜索未返回结果。未臆造成功码，平台初始化接入仍待继续核对。

独立推进升级验证：export-sdk-inventory 升级 v2 全文件哈希，避免漏掉 JS/Wasm/配置及资源；比较器兼容同版本 v1 输入但拒绝 v1/v2 混比，报告标明覆盖范围。禁止导出到包内部。新增资源覆盖、混范围拒绝和自包含保护测试通过；微信本地包实际导出 497 文件并比较通过（旧选取快照 334 文件）。

### 配置面板到运行时数据

新增引擎无关 SdkRuntimeConfiguration，包含版本、公开应用身份、环境及模块开关，可验证并转换为独立 SdkConfig。面板增加 JSON 导出入口；不自动安装 SDK、不包含密钥、不放宽构建阻断。更新已过时的平台提示。

公共回归新增字段映射、快照独立、未知版本/平台、生产 Mock、未实现能力拒绝测试并通过。Unity 检查增加配置转换与 JsonUtility 往返验证。自动平台启动装配和界面导出视觉验收仍未完成。
首次资源导入阶段出现临时类型未找到信息；重开后的 runtime-config-confirm.log 无 C# 错误，输出 SDK_RUNTIME_CONFIG_PASS 与 SDK_EDITOR_CHECKS_PASS，退出码 0。

### 配置与平台工厂身份校验

新增 IPlatformSdkProviderFactory，三个内置工厂声明固定平台身份。InitializeWithFactory 在生命周期保护区内、创建任何能力之前校验真实平台配置与工厂，缺失或不一致抛 CONFIG.PLATFORM_MISMATCH；失败不破坏正在运行的旧 SDK。自定义工厂文档已更新。

公共回归覆盖错误平台零创建、旧实例保护、缺少身份拒绝和匹配接受。抖音官方 DLL 测试、TikTok Runtime/Editor 两套测试、微信隔离重编译 Wx.dll 工厂测试均通过。身份声明不是第三方实现认证或包体裁剪证明。

### 抖音官方初始化桥接

从 6.9.7 随包 XML 核对 TT.InitSDK 成功码 0、SDK 不支持 1、Unity 不支持 2，反射确认回调 (int, ContainerEnv)。新增引擎无关 IPlatformRuntime 与 DouyinRuntime，映射统一错误、保留未知数字码、管理就绪/等待/释放状态，不以返回回调本身作为成功。

官方 DLL 入口编译及初始化行为测试通过。统一启动超时/日志/主线程编排、其他平台初始化以及真机仍待实现；本次不开放正式构建。没有用抖音返回码推断其他平台。
独立 Unity douyin-runtime.log 输出 DOUYIN_RUNTIME_UNITY_COMPILE_PASS，退出码 0。

### 官方初始化与能力装配编排

新增引擎无关 SdkStartup：校验平台身份、复制配置、等待 runtime 就绪后装配工厂；主线程 Tick 交付后台结果，超时/取消/释放抑制迟到结果。失败保留旧 SDK，按所有权释放初始化桥接，成功能力归 SDK。StartupResult 与 SDK 格式化日志提供关联 ID 及安全错误。

StartupTests 和公共回归通过，覆盖零提前创建、后台派发、成功能力可用、失败保护旧 SDK、错误关联、排队成功取消及超时。UnitySdkHost 便捷入口与 Play Mode 启动流程尚待接入，文档明确当前需独立 Tick。
独立 Unity startup-core.log 无 C# 错误，输出 SDK_EDITOR_CHECKS_PASS，退出码 0。

### Unity 宿主自动驱动启动

UnitySdkHost 新增 BeginStartup / CancelStartup / Shutdown，Update 驱动启动 Tick。新任务替换静默释放旧尝试，直接装配会清理等待任务，销毁时释放启动与业务 SDK。日志回调重入检查沿用 SDK 防护，构造无效新任务不会替换旧任务。

独立 Unity startup-host-play.log 输出 SDK_STARTUP_PLAY_PASS，实际 Play Mode 验证自动就绪后登录、超时、替换、取消、关闭和迟到回调。使用测试 runtime/factory，不代替真机或其他平台初始化验证。更新启动文档和关闭入口边界。

### 官方依赖无适配包时的混装检查

补充核心内置的已知官方目录检查，不再完全依赖对应 ErDon 适配包是否安装。面板、构建前和产物审计接入相同策略；明确外平台目录会阻断，不自动删除或移动文件。记录字节同名 UPM 身份歧义和任意改名目录的覆盖限制。

公共策略测试通过，覆盖无适配官方目录、目标目录、抖音/TikTok 分离、Mock 和路径边界。Unity 检查增加临时空官方目录探针，存在该路径时拒绝覆盖，仅清理自己创建的测试叶目录。
隔离 Unity vendor-roots.log 输出 SDK_EDITOR_CHECKS_PASS，退出码 0，实际 AssetDatabase 检测通过。

### 抖音激励广告底层适配

按官方 6.9.7 XML 与反射签名实现单视频创建/加载/展示/关闭生命周期，Completed 明确区分完整观看和提前关闭。首个终态胜出，重复加载只展示一次；解绑销毁、释放后忽略回调，失败只保留数字码。没有把客户端完成报告当作服务端发奖证明。

新增 IRewardedAds/结果契约、DouyinRewardedAds 与行为测试，官方 DLL 测试通过。顶部清单勾选该底层子项；聚合开关、面板、超时取消、其他平台和真机仍未勾选，现有配置广告阻断保持。
Unity 初次刷新存在旧文件缓存的临时类型错误；重开后 douyin-ads-confirm.log 无 C# 错误，输出 DOUYIN_ADS_UNITY_COMPILE_PASS，退出码 0。

### 激励广告聚合入口

新增 SDK.ShowRewardedAd / CancelRewardedAd、RewardedAdsEnabled、IRewardedAdsProviderFactory，抖音工厂按需创建。纳入初始化快照、所有权释放、异常回收和独立并发状态，结果快照带 CallId/TraceId。默认 120 秒超时，取消仅结束本地等待，不声称关闭平台 UI。

RewardedFacadeTests 和公共回归通过，覆盖禁用零创建、成功/提前关闭、并发、取消/超时、旧结果、释放与脱敏；抖音官方 DLL 回归通过。更新各显式编译脚本的核心依赖。面板与运行时 JSON 尚未开放广告勾选，顶部对应子项仍待办。
隔离 Unity 再次验证：ads-facade-confirm.log 无 C# 编译错误，输出 SDK_EDITOR_CHECKS_PASS，正常退出码 0；公共回归全套再次通过。

### 激励广告配置和 Editor Mock

面板加入广告模拟结果、调用和取消按钮，按配置工厂仅在勾选时创建 EditorMockRewardedAds。运行时 JSON 对 Mock/抖音映射 RewardedAdsEnabled，微信/TikTok 暂无广告适配时仍拒绝，网络/数据上报未实现项保持阻断。

公共回归通过；隔离 Unity ads-mock-play.log 输出 SDK_PLAY_CHECKS_PASS，验证完整观看、提前关闭、加载失败、超时、取消及模块关闭。新增操作步骤和错误码文档。面板视觉交互验收、真实广告展示和平台自动装配仍未完成。
ads-mock-editor.log 同时输出 SDK_EDITOR_CHECKS_PASS，无 C# 错误，退出码 0，配置导出及构建阻断检查通过。

### 微信激励广告适配

基于本地官方 0.1.23 的 DLL 签名和 XML 注释接入创建、Load、Show、OnError/OnClose。isEnded 区分完整观看与提前关闭，空关闭响应拒绝，原始错误文本不传播。工厂按广告开关创建，运行时 JSON 允许微信广告。

微信官方程序集测试和公共回归通过；覆盖并发、加载后展示、重复事件、错误码、提前关闭、完整观看和销毁。真实广告位授权、平台填充及发奖验证仍待真机，不标记完成。
隔离工程首次刷新后检查通过；重开确认 wechat-ads-confirm.log 无 C# 错误，输出 WECHAT_ADS_UNITY_COMPILE_PASS，退出码 0。

### TikTok 激励广告适配

按本地 1.1.6-Release 的实际签名实现广告生命周期，关闭回调单 bool、创建仅 AdUnitId，不复用抖音多视频参数。工厂按需创建，运行时 JSON 映射广告开关。与微信/抖音共用 SDK.ShowRewardedAd 上层契约。

run-tiktok-tests.ps1 的 Runtime/Editor 两套官方接口编译和注入广告测试通过，公共回归通过。完成标记仅限适配与测试：本地插件含项目定制，干净官方包核验、隔离工程、真实客户端能力与广告展示仍待验收。

### 禁用域重载的宿主生命周期

Unity SubsystemRegistration 清理上轮启动任务、宿主队列和 SDK 所有权，避免无域重载时静态配置跨会话。宿主队列异常改走 SDK 格式化日志，上层回调优先，异常内容不透传。

公共回归通过。隔离 Unity reload-play.log 输出 SDK_RELOAD_PLAY_PASS：同时关闭域重载/场景重载，连续两轮进入退出 Play Mode，验证编辑态遗留 provider 回收、旧配置清空、运行态 provider 单次释放、迟到回调抑制；注入宿主队列异常验证 interface/callId/logId/traceId/params/result 和敏感内容不泄漏。测试结束恢复 Editor 原重载选项。整体生命周期与所有输出路径最终复核仍单独待办。

### 微信初始化桥接

核对本地包 JS 初始化入口发送 Inited=200，以及 Runtime DLL 的整数原样转发，新增 WeChatRuntime。仅 200 就绪，其他码为 INIT.PLATFORM_FAILED；不套用抖音的 0/1/2 语义。支持重复初始化保护、已就绪幂等、失败重试、释放后迟到回调抑制。文档提供 BeginStartup 存储示例及单初始化所有者约束。

官方程序集测试通过，覆盖 200 成功、0 不成功、未知码保留、重复/迟到回调与异常脱敏；真实设备初始化和官方包来源核验仍待办。
隔离 Unity wechat-runtime.log 无 C# 编译错误，输出 WECHAT_RUNTIME_UNITY_COMPILE_PASS，退出码 0。

### TikTok 初始化桥接

核对本地 Runtime/Editor DLL 成功路径返回 0，新增 TikTokRuntime；非零统一平台失败并保留数字码，不猜测错误原因。两套 DLL 的初始化和原登录/分享/存储/工厂/广告测试通过。记录二进制哈希与证据路径，补 BeginStartup 示例和本地超时边界。

本地插件含定制；干净官方包真实性、隔离插件工程集成及真机初始化仍未完成，不将本轮签名/注入测试视为这些验收。

### 配置驱动的平台装配入口

增加 SdkPlatformRegistry 和 UnitySdkHost.BeginConfiguredStartup；平台包自行注册构造器，核心不引用平台程序集。根据导出配置等待初始化后创建勾选能力。登录未注入后端工厂、未实现支付和平台未注册均在创建运行时前拒绝；重复注册不覆盖已有项。Play 会话开始清空注册表。

公共回归通过，验证预检零运行时创建、快照隔离、环境传递、禁用登录不传交换工厂、平台错配回收和注册重置。隔离微信/抖音 Play Mode 分别输出 SDK_PLATFORM_REGISTRATION_PASS，只解析真实包构造器、不触发平台 API。面板按钮和真实构建仍待完成，TikTok 干净工程仍待验证。
宿主配置入口回归 configured-startup-retry.log 输出 SDK_STARTUP_PLAY_PASS；首轮因旧 ReloadPlayChecks 批处理停在退出阶段占用工程，核对进程命令行后仅结束该测试进程并重跑。新文件首次刷新有暂态类型错误，微信重开编译 wechat-registration-confirm.log 无 C# 错误。

### 面板平台启动控制

增加 EditorPlatformBootstrap，面板可按当前配置启动平台、取消等待或关闭 SDK；后端登录交换工厂由游戏注入，退出 Play 清空，不保存实例。真实平台缺后端返回明确配置码。构建校验文案更新为依赖/产物仍待验收，阻断保持。

新增 SDK.ActivePlatform 只读诊断，Mock 业务按钮仅在实际运行 Mock 时开放，防止切换面板配置后调用旧的真实 provider。公共回归与 editor-platform-startup.log 的 SDK_STARTUP_PLAY_PASS 通过，覆盖面板调用路径及缺后端预检；使用注入平台 runtime/factory，不代替真机。界面视觉交互验收仍待办。
最终 editor-platform-confirm.log 无 C# 编译错误，输出 SDK_EDITOR_CHECKS_PASS，退出码 0。

### 通用网络底层契约与 Unity HTTP 实现

增加 INetwork/INetworkOperation、不可变请求快照和 HTTP 状态响应模型；HTTP 4xx/5xx 与连接失败分开。UnityNetwork 支持取消、释放抑制回调、不重定向、响应分块大小限制；不把 Unity 原始错误文本带出。暂未开放配置 network 开关，聚合日志和平台网络接入仍待完成。

新增 NetworkContractTests 覆盖 URL/方法/头校验、请求及结果快照、HTTP 状态语义和流式超限；公共回归通过。隔离 Unity network-provider.log 无 C# 错误，SDK_EDITOR_CHECKS_PASS，退出码 0。真实 HTTPS 和请求生命周期联调尚未完成，不能用编译替代。配置文档补底层示例和所有已定义错误。

### 网络请求生命周期的实际 Unity 验证

新增 NetworkPlayChecks，在隔离 Unity Play Mode 启动仅监听本机回环地址的临时 TCP 端口，不访问外部服务、不绕过证书。用不完成 TLS 的连接验证 UnityWebRequest 实际超时；验证立即取消及重复取消只有一次回调、多个在途请求 Dispose 后不回调、Dispose 后新请求拒绝、宿主失活拒绝。

network-play.log 输出 SDK_NETWORK_PLAY_PASS，无 C# 错误。该检查证明请求终止与回调抑制边界，不证明正常 HTTPS 内容、平台域名白名单、真机桥接或响应大小在平台网络栈上的表现。

### 并发网络聚合入口

新增 SDK.Request，返回独立取消句柄；NetworkEnabled 和 INetworkProviderFactory 纳入快照、按需创建及回收。每个请求独立超时/取消，关闭时中止在途请求并抑制迟到结果。结果 CallId/TraceId、统一日志不包含 URL/头/正文。派发失败沿用主线程恢复队列。配置 JSON/面板尚未开放网络，避免勾选后缺少平台网络工厂。

NetworkFacadeTests 和公共回归通过，抖音实际 DLL 回归通过。Unity 初次测试按同步取消断言失败；修正为等待宿主队列后，network-facade-confirm.log 无 C# 错误并输出 SDK_NETWORK_PLAY_PASS，验证实际 UnityNetwork 经过聚合入口的取消与关联 ID，以及原底层超时/释放行为。

### 网络配置与 Mock 预览

EditorMockNetwork 支持本地 200/401、连接失败和无回调超时；按 NetworkEnabled 创建，释放清理挂起操作。配置面板增加网络模拟结果和最近一次请求取消；运行时 JSON 仅对 Mock 放行 network，三平台真实网络工厂尚未实现仍拒绝。

公共回归与 network-mock-play.log 的 SDK_PLAY_CHECKS_PASS 通过：验证 200/401 区分、连接失败、超时、取消及禁用，并回归原登录/存储/分享/支付/广告流程。没有任何真实外部请求。视觉交互、真实平台和正常 HTTPS 仍未验收。
network-mock-editor.log 无 C# 错误，SDK_EDITOR_CHECKS_PASS，退出码 0；配置映射及构建阻断检查通过。

### 微信可选网络通信装配

核对本地微信包 C# 类型和 CHANGELOG：通用网络通过 UnityWebRequest，无可用通用 WX.Request C# 方法。通过 NetworkProviderFactory 装饰平台工厂注入可选通信实现，不让平台包强依赖 HTTP 包。BeginConfiguredStartup 与 EditorPlatformBootstrap 可接收网络构造器；平台就绪才创建，关闭不创建，缺失提前失败。

公共回归通过，新增工厂预检、延迟构建、单次创建/释放及关闭模块零构建测试。JSON 仅放行 Mock/微信网络。未进行微信导出或真机请求，不据此解除构建阻断。
重开后 network-composition-confirm.log 无 C# 错误，SDK_EDITOR_CHECKS_PASS，退出码 0。

### 正常 HTTPS 与接收限额验证

新增 NetworkHttpsPlayChecks，以不携带凭证的 https://example.com/ 为基线。实际通过 SDK.Request -> UnityNetwork -> UnityWebRequest 验证 GET 200、Content-Type 响应头及非空正文，1 字节限额触发 NETWORK.RESPONSE_TOO_LARGE，HEAD 200 正文为空。

network-https.log 输出 SDK_NETWORK_HTTPS_PASS，无 C# 编译错误。未修改证书信任、代理或跳过 TLS 校验；测试只用于联网集成验证，不纳入离线公共回归。此证据限于 Unity Editor，不替代三平台开发工具/真机网络兼容、业务服务授权及支付接口测试。

### 抖音与 TikTok 可选网络装配

核对本地包均含 UnityWebRequestCompatibility，抖音另有 TTRequest 原生接口。当前配置沿用可选 INetwork 工厂，不引入平台 DTO，避免在公共核心依赖 TTSDK。network 对首批三平台放行，但必须显式注入通信工厂；支付/数据上报等未完成项的校验保持。

公共回归通过，新增抖音/TikTok 网络配置映射、缺工厂预检、平台身份保留与 provider 单次释放验证。实际小游戏网络调用仍待真机，配置装配不作为网络兼容认证。
隔离抖音工程加入可选 HTTP 包后，douyin-network-confirm.log 无 C# 错误，输出 SDK_PLATFORM_NETWORK_PASS: Douyin。检查实际 provider 构造和真实平台注册的配置解析，未执行平台初始化或 HTTP 请求。首轮测试的反射调用因 Unity 对象隐式 bool 选错重载，已改为显式 object[] 参数后通过。

### 抖音开发构建与链接后注册验证（2026-11-23）

隔离 DouyinBuildProject 首轮在 Emscripten Python 捕获子进程输出时发生 GBK UnicodeDecodeError。只为构建进程设置 PYTHONUTF8=1、PYTHONIOENCODING=utf-8 后重新构建，douyin-development-build-utf8.log 包含 SDK_DEVELOPMENT_WEBGL_BUILD_PASS 和退出码 0，BuildReport 为 Succeeded errors=0。check-linked-platform.ps1 对本次 ManagedStripped 检查通过，ErDon.UnitySDK.Douyin 及自动注册方法保留；未发现其他 ErDon 平台适配器。此检查范围不包含所有官方 JS/Wasm/资源，也不证明官方小游戏导出或真机运行。

### 抖音官方小游戏导出（2026-11-23）

通过 TTSDK 6.9.7 公开 DouyinBuilder.BuildWebGL(StarkBuilderSettings) 导出新包体，固定 WebGL + Development、占位 App ID、独立新目录，不上传。douyin-conversion.log 显示 Build Finished Success、SDK_DOUYIN_CONVERSION_FILES_PASS、退出码 0。输出位于 DouyinBuildProject/Builds/DouyinConversion-20261123-135336/tt-minigame，game.json 与 project.config.json 可解析，Wasm 非空；本轮 ManagedStripped 注册检查通过。尚未证明开发者工具启动、真机接口及全部最终产物的平台隔离。

### TikTok 干净官方插件工程与构建（2026-11-23）

从官方 Unity 构建文档的 CDN 链接下载完整 1.1.6 Release unitypackage，来源与 SHA-256 记录在 docs/tiktok.md。Runtime/Editor 各七组接口和注入行为测试通过；全新 TikTokBuildProject 采用原始完整插件，未复制旧游戏的导入宏。ConfigureTikTok 初次导入退出 0；tiktok-development-build.log 输出 SDK_DEVELOPMENT_WEBGL_BUILD_PASS，BuildReport 为 Succeeded errors=0，退出 0；本轮 ManagedStripped 注册入口检查通过。空场景 Play Mode 输出 SDK_PLATFORM_REGISTRATION_PASS: TikTok，仅解析工厂而不调用平台 API。官方小游戏导出、最终产物审计与真机仍待验收。

### TikTok 官方小游戏导出（2026-11-23）

完整官方 1.1.6 中 StarkBuilder 是 internal；改用其 Unity 命令行入口 BuildWebGLFromCommandLine，不修改官方 DLL。配置和构建分进程执行，非生产 Development、关闭 bulk memory、占位 App ID，输出在工程外新目录 .tests/TikTokConversion-20261123-141124。tiktok-conversion-official.log 包含 Build Finished Success、build success: True、退出码 0；独立 Verify 输出 SDK_TIKTOK_CONVERSION_FILES_PASS 并退出 0。JSON 可解析，wasmcode/data-package 引用目录存在且包含非空压缩 Wasm/数据文件；本轮链接后注册检查通过。空测试工程没有 StreamingAssets 内容，实际只生成两个分包，与文档示例的三个分包有差异；后续需用真实资源工程验证该路径。尚未通过客户端启动、最终平台隔离与真机功能验收。

### TikTok StreamingAssets 分包验证（2026-11-23）

新增 ConfigureWithResource 和分包结构/内容检查。隔离工程加入固定文本文件后，官方导出在 tiktok-resource-build.log 输出 build success: True 并退出 0，输出 .tests/TikTokConversion-20261123-141938。game.json 现在包含 wasmcode、data-package、StreamingAssets 三个分包；独立验证输出 SDK_TIKTOK_STREAMING_RESOURCE_PASS 和 SDK_TIKTOK_CONVERSION_FILES_PASS，退出 0。之前空工程缺少第三分包并非本次资源丢失：有实际文件时会生成。此证据仅覆盖构建资源保留，不代表设备端读取、下载或全部资源裁剪通过；整体最终产物隔离项继续未勾选。

### 模块裁剪缺口复核（2026-11-23）

直接读取实际 TikTok 资源构建的 ManagedStripped 元数据，关闭登录时 TikTokCredentialProvider 与 VerifiedAuth 仍包含构造器和登录方法。运行时不创建能力的测试不能作为模块代码裁剪证据。为避免隐藏这一验收缺口，在第二阶段新增独立未完成项；已完成项保持 62，总项增加到 78。同步清除配置/平台隔离文档中“真实适配未实现”“所有 Player 均阻止”“TikTok 注册未验证”的旧状态。

### 能力编译裁剪首轮实现（2026-11-23）

三平台登录/存储/分享/广告/上报实现增加 Player 条件编译，工厂不再引用被排除实现。面板提供应用构建能力选择按钮，保留其他宏；构建前检查勾选与排除符号冲突，运行时 JSON/SDK.Initialize/InitializeWithFactory 拒绝未编译能力并保留旧实例。Editor 不裁剪以供预览。run-compiled-module-tests.ps1 验证 TikTok 五类类型缺失及工厂/配置拒绝；三平台默认回归和核心测试通过。实际 tiktok-modules-build.log 构建成功退出 0，链接后 TikTokCredentialProvider 消失，TikTokStorage 和自动注册保留。网络/支付/公共核心与官方内部代码仍待处理，其他平台及模块组合的实际构建验证待补，因此模块裁剪整体不打勾。

### 全部关闭能力的实际构建验证（2026-11-23）

发现测试场景原来硬编码开启存储/分享/广告/上报，改为序列化面板配置完整快照，并新增 ConfigureAllCapabilitiesOff。tiktok-all-off-build.log 构建成功退出 0，场景配置五类开关均关闭；check-linked-platform.ps1 的 ExcludedCapabilities 检查确认五类 TikTok 适配定义及引用均消失、注册方法仍在。工具对未裁剪的抖音旧产物正确拒绝 Login 排除断言。此项不等同于其他平台组合、网络/支付、官方 SDK 内部及真机验收，整体裁剪继续未勾选。

### 微信与抖音全部关闭能力的构建组合（2026-11-23）

更新两份隔离工程的配置快照探针后，分别执行 ConfigureAllCapabilitiesOff 和 Build。WeChatBuildProject-all-off-build.log、DouyinBuildProject-all-off-build.log 均输出 SDK_DEVELOPMENT_WEBGL_BUILD_PASS 并退出 0；链接后五类排除能力类型和引用检查通过，平台自动注册保留。微信兼容宏 ERDON_WECHAT_STRING_ANALYTICS 仍保留。三平台全部关闭组合已有实际证据，矩阵同步更新；完整能力裁剪仍需网络/支付/核心及官方内部代码边界，保持未勾选。

### 网络与支付实现的编译排除（2026-11-23）

面板及构建/运行时配置校验扩展 NETWORK、PAYMENTS；条件排除 UnityNetwork、NetworkProviderFactory、BoundedResponseBuffer 和 VerifiedPayment、UnityOrderClient、OrderResponsePolicy，保留公共接口/DTO，登录 UnitySessionExchange 不受网络排除控制。七项禁用的 C# 专项及 run-all-tests.ps1 默认模式全量公共回归通过。tiktok-seven-off-build.log 构建成功退出 0，七项链接类型/引用排除检查及注册保留通过。支付能力仍无真实收银台实现；部分开启组合、其他平台新组合及官方内部代码最终审计继续待完成。

### 登录开启、网络关闭的共享依赖检查（2026-11-23）

新增 ConfigureLoginOnly，构建探针在登录开启时提供 UnitySessionExchange 工厂，但不调用登录或 HTTP 请求。tiktok-login-only-build.log 成功退出 0，场景 login=1、network/payments=0。链接检查确认 TikTokCredentialProvider、VerifiedAuth、UnitySessionExchange 定义保留，其余六类能力实现/引用排除；平台注册仍在。RequiredTypes 工具对已裁剪登录的微信产物正确拒绝保留断言。仅证明此构建组合及通信依赖，未发生真实登录联调。

### 启动生命周期的编译能力前置校验（2026-11-23）

日志/生命周期复核发现直接 SdkStartup 构造未检查编译能力，会在平台初始化后才得到笼统组合失败；Unity 直接初始化路径也先清除旧启动。新增构造及宿主入口前置校验，未编译能力在接管运行时或清除旧启动之前拒绝。七种关闭能力专项覆盖运行时零初始化/零释放，公共 run-all-tests 全部通过；独立 BuildChecksProject 输出 SDK_EDITOR_CHECKS_PASS 并退出 0。最终日志/生命周期总复核仍未完成，原清单项保持未勾选。

### 聚合日志输出契约复核（2026-11-23）

新增 LogContractTests 并接入公共回归，实际调用初始化、登录、分享、广告、支付、三个存储接口、网络、同步/异步上报、关闭及 Startup 失败。检查 ISO 时间、接口、callId/traceId、逐条唯一 logId、阶段和 JSON 参数；所有 Start 有同 ID 的 Complete，敏感测试值与换行不泄漏。上层日志回调抛异常时默认 sink 接收完全相同日志行，未配置回调时默认输出可用；默认公共回归全通过。官方 SDK 自身日志不在聚合 sink 控制范围，最终全部生命周期/Unity 输出路径复核仍保留未勾选。

### Unity 默认日志输出验证（2026-11-23）

新增 UnityLogSinkChecks，独立 BuildChecksProject Play Mode 监听 Application.logMessageReceived。验证三种级别进入实际 Debug 输出、包含 SDK 字段且不泄露参数、上层回调接管后引擎无重复日志、回调抛异常后输出同一条格式化内容。首次测试错误地以取消后立即 Shutdown 的异步队列路径期待 Warning，修正为同步模块关闭路径后 unity-log-sink-fixed.log 输出 SDK_UNITY_LOG_SINK_CHECKS_PASS，进程结束。剩余生命周期复核重点为宿主在普通参数/工厂失败时是否保留旧启动；整体最终复核项暂不勾选。

### 第一阶段复核闭合：宿主初始化失败保留旧启动（2026-11-23）

Play Mode 首轮复现：重复使用已持有 provider 的初始化被拒绝，但旧等待启动已提前释放。调整 UnitySdkHost 两种直接初始化方法，新的 SDK 成功后再取消旧启动，并显式拒绝释放期间重入。HostStartupPreservationChecks 覆盖重复 provider、工厂异常、空工厂三种失败保留，原启动仍可完成；成功替换释放一次、拒绝宿主重入并忽略旧迟到回调。host-startup-preservation-after.log 输出 SDK_HOST_STARTUP_PRESERVATION_PASS；完整公共回归通过。

结合前序 LogContractTests、UnityLogSinkChecks、启动/操作/所有权/派发回归、域重载关闭的两轮 Play Mode 与真实 HTTP 生命周期记录，本阶段列明的 SDK 内部输出及生命周期复核已完成，勾选最后子项。该结论不包含官方 SDK 日志、真实平台接口/账号/支付或后续阶段真机验收。
