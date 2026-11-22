# Yostar 双样本协议向量

这些是合成的离线 fixture，不是实际安装文件、HAR 或在线 response。
`fixture-host`、`fixture-version`、`fixture-basis`、文件与启动参数都是测试输入，不能登记到生产档案。
它们只固定本地配置、manifest 与 Authorization 的协议格式，不证明目前的服务端行为或第二款游戏已受支持。

迁移参考是本地只读仓库 `E:\Repos\_GameLauncher_Samples\StellaSora.Launcher`，
commit `ee2d1dc2b4ad7d3d844b2f713bf25d22181c6455` 的
`tests/Cafe.Launcher.Tests/YostarProtocolContractTests.cs` 与同目录 `Fixtures/YostarProtocol/`。
本次保留独立 Node 生成方式，并补充有正确 vc 的非法数字输入与完整对象上的 schema 拒绝测试。

原始算法来源是 StellaSora 官方 CN 1.3.0 与 Blue Archive 官方 JP 1.7.2 发行包：

- 两包 `node_modules/@launcher/utils/src/crypto.ts` 的 `getObjectHash` 与 `getAuthHeader`。
- Stella `out/main/index-52e4c114.js:18926–18930`：配置写入顺序为 `tag,name,version,vc`。
- BA `out/main/index-f057dd97.js` 的 `insertGameConfigFile`：配置写入顺序为 `tag,name,params,version,vc`。
- 两包共有的本地 manifest info 与文件 vc 格式。

参考仓库的 `docs/design/yostar-reference-follow-up-plan-2026-09-30.md` 保存了取证范围；
该记录列出的官方包 SHA-256 为：

| 材料 | SHA-256 |
| --- | --- |
| BA JP 1.7.2 `app.asar` | `FD243A514FDCD5EA9CF91C2A4557C560F9023B59D8DD877312410B0C665CA4F7` |
| Stella CN 1.3.0 `out/main/index.js` | `D96B0824409D7CCC78AD59B7FACF99BEAB07E95740605E4EAE6F470ECD2A9A38` |
| BA JP 1.7.2 `out/main/index.js` | `22CB4202FF8AF22EDC6620ECE9E512E59DC60395EB543CA7470C3CB0C3576816` |

`generate.cjs` 使用 Node 内置 `crypto` 独立产生预期值，不调用被测 .NET 代码，也不访问网络。
在仓库根目录运行 `node tests/Cafe.Launcher.Tests/Fixtures/YostarProtocol/generate.cjs` 可再生三个 JSON。
测试读取提交的静态 JSON，不在运行时再生预期值；既有 BA 真实安装固定向量继续保留。

三个向量分别覆盖 Stella 缺少 `params`、BA 空 `params` 与 BA 非空 `params`，并包含独立重排后的配置 vc。
BA 空参数向量还包含非法 manifest 数字字符串，其 vc 仍按官方算法计算，以确保失败来自数字格式校验。
测试中的 `ProtocolProfile` 仅从 BA 档案替换签名与本地协议字段，是明确的合成协议测试；
继承的运行时路径、可执行名与 Cookie 路径不构成生产 Stella 档案。
盐是参考发行包中的公开协议常量，fixture 不含用户数据、Cookie 或在线短期凭据。
