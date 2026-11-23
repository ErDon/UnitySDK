# UnitySDK
面向多个 Unity 游戏复用的跨平台 SDK。以微信小游戏核心能力的接口语义为设计基准，按需适配微信、抖音、TikTok、Android/iOS 各发行渠道。

当前阶段：实现中的 UPM 开发预览，尚未完成真实平台端到端验收。

- [整体架构与设计图](docs/architecture.md)
- [错误码与诊断规范](docs/errors.md)
- [迭代与验收计划](docs/roadmap.md)
- [版本与验证矩阵](compatibility/README.md)

原则：模块可选、配置可预览、每个公开接口有文档与可运行示例；依赖版本可锁定；不依赖任何特定游戏的业务框架。第一阶段支持 Unity，保留跨引擎协议边界，不提前实现 Godot。

## 开发进度

已实现登录聚合入口、诊断日志、Editor Mock、配置检查与部分隔离审计，详见 [工作计划](WORK_PLAN.md) 和 [接入说明](docs/getting-started.md)。[微信](docs/wechat.md) 与 [TikTok](docs/tiktok.md) 已有登录凭证适配及契约测试；真实后端、官方运行时集成、真机链路与完整产物裁剪尚待验证，不可用于生产接入。抖音适配仍待实现；Android/iOS 本轮仅保留扩展接口。

配置面板入口：`Tools → ErDon SDK → 配置与预览`，详见[配置面板说明](docs/configuration.md)。当前真实平台装配尚未完成，Player 构建会被明确阻断；请在独立测试工程接入。

[存储接口与预览](docs/storage.md)：支持统一同步读写/删除、配置开关及 Editor 内存 Mock；微信和 TikTok 存储适配已实现，真机持久化仍待验收。
