# Android 启动示例

先安装核心和 Android 包，切换 Android 目标、添加 ERDON_PLATFORM_ANDROID，最低 API 26。
配置面板选择 Android、非生产、仅存储/分享，应用构建能力选择；使用 Development Build。

将 AndroidStartupExample 挂到场景，Share 方法连接 UI 按钮。该组件拥有 SDK 生命周期，
不要与游戏其他启动器重复使用。Editor 不模拟原生系统功能，请用核心 Mock 预览。

本示例不需要账号、不配置支付、不引入任何供应商。完整接入及原生扩展见仓库 docs/android.md。
