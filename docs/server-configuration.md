# 服务端账号与密钥配置

真实账号密钥不是功能开发的前置条件。服务端提供三平台 JSON 模板、环境变量模板、离线配置检查和统一启动入口；自动测试使用临时凭据和模拟官方响应，不需要提供真实账号。

## 文件分工

| 文件 | 配置内容 |
| --- | --- |
| server/config/wechat.example.json | 微信登录、应用/游戏绑定、持久化会话库 |
| server/config/wechat-payment.example.json | 微信道具直购、加密 session_key、预下单签名、查单和发货通知；见[微信支付](wechat-payments.md) |
| server/config/douyin.example.json | 抖音登录、道具直购开关、Android/iOS 路由、区服、商品与数据库 |
| server/config/tiktok.example.json | TikTok 登录、支付开关、沙盒选择、商品、令牌加密密钥版本与数据库 |
| server/.env.example | App ID、App Secret、回调 Token、加密密钥和可选发货模块路径 |

将 JSON 复制为同目录的 `*.local.json`，将 `.env.example` 复制为 `.env`。本地配置、`.env` 和模板对应的数据目录均已加入 Git 忽略规则。JSON 中只写环境变量名称，例如 `secretEnv: "DOUYIN_APP_SECRET"`，加载器不接受直接写 `secret`；敏感值在服务端 `.env` 或部署环境变量中填写。Unity 面板/运行时配置中只放 App ID 等公开标识，不包含 App Secret 或回调 Token。

## 检查与启动

在仓库的 server 目录执行；以抖音为例：

下面的复制命令只用于首次创建文件；已经配置过的 .env / *.local.json 应直接编辑，跳过复制，避免覆盖现有值。游戏的 Unity JSON 与这里的服务端 JSON 是不同格式，不能互换。

```powershell
Copy-Item .env.example .env
Copy-Item config/douyin.example.json config/douyin.local.json
# 编辑 .env 和 config/douyin.local.json 后：
npm run config:check -- config/douyin.local.json
npm run start:configured -- config/douyin.local.json
```

这些 npm 命令读取当前 server 目录的 `.env`；已有进程环境变量优先。也可设置 `SDK_SERVER_CONFIG` 后省略配置路径。原 `npm start` 和 `npm run start:tiktok` 为兼容入口，行为不变。

检查输出仅含平台、环境、支付开关、端口、商品数量、所需/缺少的环境变量**名称**和 ready 状态，不包含实际 App ID、密钥、商品详情或数据库路径。即使尚未填写密钥，也能检查模板结构并看到缺项清单。缺项时退出码为 1；不会连接平台、创建数据库或监听端口。检查通过不等于密钥已获平台认可。

启动时创建配置的数据库父目录，监听 `127.0.0.1`。本机验证不需要公网；Unity/平台联调时由部署提供 HTTPS 反向代理。路径相对于 JSON 文件所在目录解析，三平台默认端口不同。`Ctrl+C` / SIGTERM 会停止接收请求、等待在途工作并关闭数据库。

## 模块配置规则

- `payments.enabled=false`：仅启动所选平台登录服务，不注册订单/通知路由，也不要求支付密钥。payments 对象只保留 enabled，避免关闭后残留配置被误认为生效。
- 抖音开启支付：填写 route（Android 或 IosCustomerService）、zoneId、callbackTokenEnv 和商品目录。道具价格单位为人民币分，productName 最长 10 个 UTF-16 字符。
- TikTok 开启支付：显式填写 isSandbox，商品的 Beans 数量/展示字段，以及 keysEnv/currentKeyId。金额和 Beans 数量由服务器配置，不能由客户端覆盖。
- 微信支付使用独立 `wechat-payment.example.json` 模板，需填对应环境的支付 AppKey、offer/分区、原始 ID、回调 Token 与凭据加密密钥；普通微信登录模板保持支付关闭。
- test/production 是游戏数据隔离标签。抖音 test 不会自动切换官方支付沙盒；TikTok 的 isSandbox 是独立配置。

商品修改不会改写已有订单价格。数据库路径不能互相重合，也不允许 `:memory:`。支付服务使用持久化会话/订单；换游戏时修改 gameId、App ID 和数据目录，不共用别的游戏数据库。

## 密钥轮换与发货

TikTok 的 `TIKTOK_CREDENTIAL_KEY_HEX` 是部署方生成并保存的 32 字节随机加密密钥，以 64 位十六进制填写，**不是 TikTok App Secret**。不得每次启动重新生成。轮换时在 keysEnv 中同时保留旧、新 keyId 的环境变量映射，把 currentKeyId 改为新 keyId；旧记录仍需要旧密钥解密，不能立即删除。

`SDK_DELIVERY_MODULE` 可指向受信的本地 `.mjs` 文件，该文件导出 `async function deliver(message)`。这是游戏权益入库扩展点，必须在同一业务事务内按 deliveryKey 去重和发道具，成功返回 true。未配置时支付通知可提交 Paid，但不会伪造发货或推进为 Fulfilled。此模块路径由部署方设置，不能接受玩家上传的代码。配置检查只检查 JSON/环境变量，不执行发货模块。

## 配置错误

| 错误码 | 含义 |
| --- | --- |
| CONFIG.FILE_REQUIRED | 未传 JSON 路径或 SDK_SERVER_CONFIG |
| CONFIG.UNREADABLE / CONFIG.TOO_LARGE | 文件不可读、JSON/UTF-8 无效或超过 128 KiB |
| CONFIG.INVALID | 字段缺失、拼写错误、类型错误或平台不适用；消息仅指出字段 |
| CONFIG.ENV_MISSING | 所需环境变量未填写 |
| CONFIG.ENV_INVALID | 已填环境变量不满足格式，例如加密密钥不是 64 位十六进制 |
| CONFIG.STORAGE_COLLISION | 多个数据库配置为同一路径 |
| CONFIG.PAYMENT_UNIMPLEMENTED | 平台支付后端尚未实现 |
| CONFIG.LISTEN_FAILED | 监听失败，检查端口占用 |
| CONFIG.DELIVERY_MODULE_INVALID | 发货模块不可导入或没有导出 deliver |

已验证三平台配置解析、无密钥预览、坏配置拒绝、CLI 脱敏、临时数据库与本机 HTTP 启停。平台权限、真实凭据和真实支付留待联调；不因缺少这些信息停止实现可离线完成的功能。
