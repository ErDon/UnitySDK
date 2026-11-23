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

构建前读取已安装包的清单，检查 Player 程序集中是否有其他平台程序集。构建后核查 BuildReport 输出文件和打包资源来源，写入 Library/ErDonSdk/last-artifact-audit.txt。发现登记的非目标平台内容则构建失败。

该检查只验证已登记内容，不能证明未登记官方插件、原生二进制、合并 JS 或 StreamingAssets 已被排除。后续适配包需提供覆盖这些资源的清单和实际产物验证；当前没有真实平台成功构建记录。不能仅凭审计空错误宣称完整裁剪。

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
