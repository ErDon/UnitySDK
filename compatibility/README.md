# 版本与验证矩阵

记录日期：2026-11-23。版本锁定不等于兼容性已验收；没有对应证据的格子一律为待验证。当前不能发布稳定版本。

| 组件 | 版本基线 | 当前证据 | 尚未完成 |
| --- | --- | --- | --- |
| Unity 核心/Editor/HTTP | SDK 0.1.0，Unity 2022.3.14f1c1 | 独立工程编译、Editor 检查、登录与存储 Play Mode 测试 | 域重载关闭、多游戏及目标平台构建 |
| 微信 | com.qq.weixin.minigame 0.1.23 | 登录真实 DTO 契约；存储引用本机已有 Wx.dll 编译入口 | 干净官方工程编译、初始化、后端与真机 |
| TikTok | 1.1.6-Release | Runtime/Editor DLL 登录与存储入口编译，注入行为测试 | 干净官方插件集成、真机与后端联调；本机插件含旧项目宏 |
| 抖音 | TTSDK 版本未确定 | 官方文档形状的登录替身测试 | 官方 SDK 安装/版本锁定、编译、后端联调及真机 |
| BGDT 安装工具 | 3.0.271 | 官网安装包下载、独立 Unity 导入成功、界面打开 | 面板持续 Loading，尚未取得 TTSDK 列表或登录入口 |
| 公共服务端 | 0.1.0，Node 24.20.0 | 22 项测试：模拟微信/抖音/TikTok 上游、真实 loopback HTTP、SQLite 重开与并发连接 | 官方接口调用、Unity HTTPS、部署、备份与性能 |
| Android/iOS 原生渠道 | 无 | 扩展边界设计 | 本轮不接具体供应商 |
| Godot | 无 | 协议边界设计 | 本轮不实现引擎桥接 |

## 可重跑证据

- `tests/run-all-tests.ps1`：纯 C# 核心、配置行为契约、存储和隔离策略。
- `tests/run-wechat-tests.ps1`：需本地官方 0.1.23 包；不是实际微信登录。
- `tests/run-wechat-storage-tests.ps1`：需工程编译的 Wx.dll；不是干净包重编译。
- `tests/run-tiktok-tests.ps1`：需本地 1.1.6-Release 插件；分别编译 Runtime/Editor 入口。
- `tests/SdkEditorChecks.cs.txt`、`tests/SdkPlayChecks.cs.txt`：复制到独立 Unity 测试工程后执行。
- `server/` 下 `node --test`：包含临时本地端口和数据库，不访问真实平台。

## 升级记录

升级前运行 `tools/export-sdk-inventory.ps1 -Platform WeChat -PackageRoot '<官方包目录>' -OutputPath '<快照文件.json>'`，保存所测版本的程序集、C#、asmdef、导入设置及 jslib 的 SHA-256。输出不包含安装目录、密钥或任意包描述字段。它只证明被测试本地文件的内容，不能证明官方来源；本机已经改过宏的包也会如实记录。

升级后比较相对文件路径与哈希，检查接口和导入设置变化；更新对应适配器后重跑上述验证。真机记录必须补设备、操作系统、宿主版本、基础库、初始化结果、能力结果和导出产物审计。存在未知格子时不标“支持”。官方强制服务端变化不能仅靠回退本地 DLL 解决。
