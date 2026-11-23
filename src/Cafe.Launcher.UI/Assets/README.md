# 游戏视觉资源

2026-11-02 整体界面方案的运行时资产登记。游戏身份到资源的映射由 UI 的 `GameVisualAssets` 持有。

| 资源 | 来源与处理 |
| --- | --- |
| `app-icon.png` | 从同目录 `app-icon.ico` 提取的 256×256 PNG 帧，供 `FAAppWindow.Icon` 使用。更新 ICO 时同步替换该帧 |
| `launcher-background.png` | 沿用仓库既有的蔚蓝档案内置背景 |
| `launcher-background-stella-sora.png` | 用户提供的本地官方 StellaSora 1.3.0 样例，`cn_app-32/resources/app/out/renderer/assets/bg-0d1f18e5.png`，原字节复制 |
| `game-blue-archive.png` | 用户提供的官方 BA 1.7.2 样例 `app-32_v1.7.2/resources/app/resources/build.ico` 中的原始 PNG 图标帧，未重绘 |
| `game-stella-sora.png` | 官方 StellaSora 样例 `cn_app-32/resources/app/resources/build.ico` 中的原始 PNG 图标帧，未重绘 |
| `launcher-background-fallback.png` | 用户确认的通用图，内置 imagegen 生成；提示词与哈希见[生成记录](../../../docs/assets/ui-improvement-2026-11-02/README.md) |

官方游戏图像和图标归各自权利人；关于页保留蔚蓝档案内置壁纸的原作链接。远程壁纸仍通过固定游戏的官方配置和统一网络传输读取，本地资源作为离线回退。
