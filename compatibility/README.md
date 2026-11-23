# 版本与验证矩阵

记录日期：2026-11-23。版本锁定不等于兼容性已验收；没有对应证据的格子一律为待验证。当前不能发布稳定版本。

| 组件 | 版本基线 | 当前证据 | 尚未完成 |
| --- | --- | --- | --- |
| Unity 核心/Editor/HTTP | SDK 0.1.0，Unity 2022.3.14f1c1 | 独立工程编译、Editor 检查、登录与存储 Play Mode 测试 | 域重载关闭、多游戏及目标平台构建 |
| 微信 | com.qq.weixin.minigame 0.1.23 | 本地 0.1.23 包在隔离 Unity 工程重新编译、适配加载及工厂/分享测试，无原 CS1684 警告 | 官方来源完整性、初始化、导出、后端与真机 |
| TikTok | 1.1.6-Release | Runtime/Editor DLL 登录、存储、分享入口编译，注入行为测试 | 干净官方插件集成、真机与后端联调；本机插件含旧项目宏 |
| 抖音 | TTSDK 6.9.7 | BGDT 官方安装、真实 DLL 登录/存储/分享入口编译与注入行为测试、隔离 Unity 工程编译加载 | 初始化、目标导出、后端联调及真机 |
| BGDT 安装工具 | 3.0.271 | 官网安装包下载、独立 Unity 导入、cp 模式安装 TTSDK 6.9.7 | 导出构建链路 |
| 公共服务端 | 0.1.0，Node 24.20.0 | 37 项测试：模拟微信/抖音/TikTok 上游、真实 loopback HTTP、SQLite 重开与并发连接、内存/SQLite 订单和幂等发货恢复 | 官方接口调用、Unity HTTPS、部署、备份与性能 |
| Android/iOS 原生渠道 | 无 | 扩展边界设计 | 本轮不接具体供应商 |
| Godot | 无 | 协议边界设计 | 本轮不实现引擎桥接 |

## 可重跑证据

- `tests/run-all-tests.ps1`：纯 C# 核心、配置行为契约、存储和隔离策略。
- `tests/run-wechat-tests.ps1`：需本地官方 0.1.23 包；不是实际微信登录。
- `tests/run-wechat-storage-tests.ps1`：需工程编译的 Wx.dll；不是干净包重编译。
- `tests/run-tiktok-tests.ps1`：需本地 1.1.6-Release 插件；分别编译 Runtime/Editor 入口。
- `tests/run-douyin-tests.ps1`：需 BGDT 安装的官方 TTSDK 6.9.7；真实 DLL 编译，回调为注入，不是真机登录。
- `tests/SdkEditorChecks.cs.txt`、`tests/SdkPlayChecks.cs.txt`：复制到独立 Unity 测试工程后执行。
- `server/` 下 `node --test`：包含临时本地端口和数据库，不访问真实平台。

## 升级记录

升级前运行 `tools/export-sdk-inventory.ps1 -Platform WeChat -PackageRoot '<官方包目录>' -OutputPath '<快照文件.json>'`，保存所测版本的程序集、C#、asmdef、导入设置及 jslib 的 SHA-256。输出不包含安装目录、密钥或任意包描述字段。它只证明被测试本地文件的内容，不能证明官方来源；本机已经改过宏的包也会如实记录。

升级后比较相对文件路径与哈希，检查接口和导入设置变化；更新对应适配器后重跑上述验证。真机记录必须补设备、操作系统、宿主版本、基础库、初始化结果、能力结果和导出产物审计。存在未知格子时不标“支持”。官方强制服务端变化不能仅靠回退本地 DLL 解决。

## 自动生成升级差异报告

```powershell
./tools/compare-sdk-inventory.ps1 -BeforePath before.json -AfterPath after.json -OutputPath upgrade-report.json
```

报告含前后版本、版本是否变化、文件新增/删除/修改、前后哈希及审查分类。即使版本号没变也比较内容；同版本被本地修改不能漏过。DLL 变化需要接口编译和运行验证，asmdef/meta 变化要复查平台裁剪及导入设置，jslib 变化要复查真机桥接。比较工具拒绝不同平台/包、重复路径、非法相对路径和无效哈希，不允许覆盖输入快照。

报告仅覆盖快照选取的文件，不是 API 语义分析，也不判断升级是否兼容；结果固定 `compatibility=not-assessed`。版本号没变化或 changedFileCount=0 都不是免测条件。工具不会安装/降级 SDK 或修改工程。测试 `tests/run-inventory-tests.ps1` 已并入公共测试入口。
