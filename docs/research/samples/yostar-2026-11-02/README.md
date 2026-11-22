# 2026-11-02 官方 Yostar 协议只读样本

这些 JSON 来自当日真实官方 API/CDN 的有限只读请求，由
[`Test-YostarProtocol.ps1`](../../../../scripts/Test-YostarProtocol.ps1) 取得。
来源、请求时刻、状态、原始响应字节 SHA-256 和读取上限见 `evidence.json`；
用于签名的官方包版本与本地 ASAR/成员源哈希见 `official-source-hashes.json`。
详细结论与未验证边界见[研究记录](../../yostar-protocol-verification-2026-11-02.md)。

- `*-game-config.json`、`*-cdn-config.json`、`*-manifest-location.json` 是小型公共响应的格式化 JSON。
- `*-manifest-selected.json` 只保存原始清单前三项、全部可执行/脚本条目及数量摘要，不能用于完整安装。
- `original_body_sha256` 总是对网络收到的原字节计算，不是格式化 JSON 文件的哈希；清单全文没有落盘。
- 游戏文件只读取入口 EXE 的前 4096 bytes，二进制不保存；只归档摘要、16 bytes 十六进制前缀和选定公共响应头。
- 认证头、时刻签名、salt、cookie、用户数据均不归档；没有启动官方程序、写游戏状态或下载完整游戏文件。

此目录与 `tests/Cafe.Launcher.Tests/Fixtures/YostarProtocol` 的用途不同：
后者由参考 JavaScript 算法独立生成，是固定的**合成离线向量**，用于验证签名和本地状态的字段存在性、顺序与 vc；
它们不是抓包、真实安装状态或官方安装互操作证明。本目录是**带日期的在线观测**，也不应成为默认离线 CI 的网络依赖。
两类证据都不能替代完整安装、续传、整文件 CRC64、启动、进程家族或反作弊验收。

`evidence.json` 保留第一次 BA 备源被精确主机白名单挡住的两条记录。
该域名随后由本次官方 CDN 响应确认并加入精确名单，使用 `-ResumeBlockedFileProbes`
只补发此前未发送的 HEAD 和 Range；后面两条记录才是真实备源响应。
