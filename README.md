# UnitySDK
面向多个 Unity 游戏复用的跨平台 SDK。以微信小游戏核心能力的接口语义为设计基准，按需适配微信、抖音、TikTok、Android/iOS 各发行渠道。

当前阶段：实现中的 UPM 开发预览，尚未完成真实平台端到端验收。

首次接入按以下顺序阅读：

1. [当前能力与未完成事项](docs/status.md)：确认功能、验收与本轮范围。
2. [新项目接入](docs/getting-started.md)：安装、Mock 验证、真实平台启动。
3. [账号与密钥配置](docs/server-configuration.md)：填写服务端本地配置，不向客户端发放密钥。
4. [业务接口索引](docs/api-reference.md)：逐项查看调用、结果、取消和错误语义。
5. 需要支付时阅读 [支付编排](docs/payment-pipeline.md) 和 [抖音完整接入示例](docs/douyin-payments.md)。

- [上层接口索引与生命周期](docs/api-reference.md)
- [整体架构与设计图](docs/architecture.md)
- [错误码与诊断规范](docs/errors.md)
- [迭代与验收计划](docs/roadmap.md)
- [版本与验证矩阵](compatibility/README.md)
- [Android 原生 AAR 与 Unity 桥接接入](docs/android.md)
- [原生渠道扩展边界](docs/native-extension.md)

原则：模块可选、配置可预览、每个公开接口有文档与可运行示例；依赖版本可锁定；不依赖任何特定游戏的业务框架。第一阶段支持 Unity，保留跨引擎协议边界，不提前实现 Godot。

## 开发进度

已实现登录、存储、分享、激励广告、网络、数据上报聚合，以及支付/订单公共链路、诊断日志、Editor Mock、配置装配和部分隔离审计，详见 [工作计划](WORK_PLAN.md) 和 [接入说明](docs/getting-started.md)。[微信](docs/wechat.md)、[抖音](docs/douyin.md) 与 [TikTok](docs/tiktok.md) 已有平台适配、初始化桥接及程序集/注入行为测试；服务端已有会话和订单实现。真实凭证联调、真机功能、真实支付及最终产物裁剪尚待验收，不可用于生产接入。TikTok 干净官方 1.1.6 插件已完成独立工程集成、开发构建及官方小游戏导出；真机与最终产物隔离仍待验收。新增 [Android 原生 AAR 与 Unity 桥接](docs/android.md) 开发预览，内置存储/文本分享，其余渠道按接口扩展；iOS 仍仅提供扩展契约，不包含具体原生供应商。Android 任务单独见 [工作计划](android/WORK_PLAN.md)。

配置面板入口：`Tools → ErDon SDK → 配置与预览`，详见[配置面板说明](docs/configuration.md)。运行时按配置装配和平台启动已接通；完整构建依赖与产物验收尚未完成，正式 Player 构建仍被阻断，满足平台宏、适配器与混装检查的非生产 Development Build 可用于技术验证。请在独立测试工程接入，勿以 Mock/编译通过代替真实平台验收。

[存储接口与预览](docs/storage.md)：支持统一同步读写/删除、配置开关及 Editor 内存 Mock；三平台适配已实现，真机持久化仍待验收。

[数据上报与预览](docs/analytics.md)：三平台共用回调入口，支持本地超时/取消与 Editor 模拟；调用完成不代表后台入库。

[网络请求接入](docs/network.md)：包含 HTTP 状态与传输错误的区分、JSON POST、取消、超时、参数限额和平台验证边界。
