# UnitySDK
面向多个 Unity 游戏复用的跨平台 SDK。以微信小游戏核心能力的接口语义为设计基准，按需适配微信、抖音、TikTok、Android/iOS 各发行渠道。

当前阶段：架构设计，尚未提供可安装的 SDK 或已验证的平台实现。

- [整体架构与设计图](docs/architecture.md)
- [错误码与诊断规范](docs/errors.md)
- [迭代与验收计划](docs/roadmap.md)

原则：模块可选、配置可预览、每个公开接口有文档与可运行示例；依赖版本可锁定；不依赖任何特定游戏的业务框架。第一阶段支持 Unity，保留跨引擎协议边界，不提前实现 Godot。
