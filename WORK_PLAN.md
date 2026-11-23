# 工作计划

以 Unity 2022.3 为首轮验证基线；不依赖 RS2 和第三方异步框架。当前执行第一阶段，不将 Mock 或代码编译当作真实平台接入完成。

- [x] 设计基线与仓库建立。
- [ ] 第一阶段：SDK 聚合入口、按能力多态、初始化/释放、统一结果、SDK 格式化日志、上层回调与 Unity 默认输出、Mock 登录示例及测试。
- [ ] 第二阶段：独立配置编辑器、模块勾选、配置预览、依赖解析、平台专属程序集及资源装配、生产 Mock 阻断、构建产物核验。
- [ ] 第三阶段：微信真实登录和服务端校验；确认官方依赖版本、记录真机兼容矩阵。
- [ ] 第四阶段：广告、分享、存储、网络与上报，逐能力文档及失败示例。
- [ ] 第五阶段：订单、验签、通知、查单及幂等发货接口，测试支付闭环。
- [ ] 第六阶段：抖音、TikTok 适配与升级回归；Android/iOS 本轮仅保留扩展接口。
- [ ] 第七阶段：空白项目及第二个游戏接入验收，发布稳定标签。

每阶段提交代码、说明和验证结果。未知/未实现能力明确报错，不伪造成功。每条日志正文由 SDK 生成，包含时间、接口、脱敏参数、调用 ID、日志 ID、平台、阶段和结果；上层只接收级别与完整字符串。

## 2026-11-23 执行记录

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
