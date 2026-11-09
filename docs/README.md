# 文档导航

本仓库维护 Cafe Launcher 的产品入口、工程规则、领域契约与设计依据。用户操作步骤在[中英文文档站](https://docs.bluearchive.cafe/cafe-launcher/)维护，源码位于配套的 [bluearchive-cafe/docs](https://github.com/bluearchive-cafe/docs) 仓库。

每条规则只在它的归属文档维护；其他入口负责指路，不复制正文。文档职责与跨仓库同步规则见[开发指南「文档职责」](DEVELOPMENT.md#文档职责)。

## 按角色开始

| 你是 | 从这里读 |
| --- | --- |
| 使用者 | [中文 README](../README.md) / [English README](../README.en.md) → [中文用户文档](https://docs.bluearchive.cafe/cafe-launcher/) / [English documentation](https://docs.bluearchive.cafe/en/cafe-launcher/) |
| 新贡献者 | [开发指南「环境与启动」](DEVELOPMENT.md#环境与启动) → [开发规范 §1](../PROJECT_CONVENTIONS.md#1-基本原则) → [领域契约](../CONTEXT.md) |
| 编码代理 | [AGENTS.md](../AGENTS.md) 的任务路由 → 本页「按任务查找」 |
| 界面与设计 | [设计规范](design/design-system-spec.md) → [设计资料索引](design/README.md) |
| 维护者与审计 | [开发指南](DEVELOPMENT.md) → [审计台账](../CODEBASE_AUDIT.md) |

## 按任务查找

| 需要了解 | 权威文档 |
| --- | --- |
| 产品、平台、下载与首次使用 | [中文 README](../README.md)、[English README](../README.en.md) |
| 安装、设置、游戏操作与排障 | [中文用户文档](https://docs.bluearchive.cafe/cafe-launcher/)、[English documentation](https://docs.bluearchive.cafe/en/cafe-launcher/) |
| 准备环境、构建、测试、升级依赖、发布 | [开发指南](DEVELOPMENT.md) |
| 修改实现、程序集、DI、持久化、本地化或测试 | [开发规范](../PROJECT_CONVENTIONS.md) |
| 判断安装、校验、停止、卸载、设置或更新行为 | [领域契约](../CONTEXT.md) |
| 编写界面文案与翻译 | [术语表](../UBIQUITOUS_LANGUAGE.md) |
| 界面视觉、组件、表面与无障碍 | [设计规范](design/design-system-spec.md) 与 [设计资料索引](design/README.md) |
| 判断某项决策为何如此 | [ADR 索引](design/adr/README.md) |
| 查看当前发现与已接受风险 | [审计台账](../CODEBASE_AUDIT.md) |
| 本地数据与网络处理 | [隐私政策](../PRIVACY.md) |
| 当前版本变化 | [本次发布说明](../CHANGELOG_RELEASE.md) |
| 源码与第三方许可 | [LICENSE](../LICENSE)、[第三方声明](../THIRD-PARTY-NOTICES.md) |

## 设计与历史资料

[设计资料索引](design/README.md)按现行规范、待执行计划与归档记录列出 `design/` 下的全部文档；[研究记录索引](research/README.md)列出调查与选型记录；[原型索引](../prototypes/README.md)列出保留的视觉与交互实验。修改前先按 [ADR 索引](design/adr/README.md)核对后续修订及替代关系。

归档计划、验证记录与研究报告保留当时的目标、证据与验证边界。它们用于追溯背景；实施现行功能时，以当前代码、领域契约、开发规范及仍有效的 ADR 为依据。审计发现统一记录在审计台账。

## 相关仓库

| 仓库 | 职责 |
| --- | --- |
| [Cafe.Launcher.Avalonia](https://github.com/bluearchive-cafe/Cafe.Launcher.Avalonia) | 桌面端源码（本仓库） |
| [Cafe.Launcher.Backend](https://github.com/bluearchive-cafe/Cafe.Launcher.Backend) | 发行元数据代理、更新说明与公告资源转换 |
| [Cafe.Launcher.Downloads](https://github.com/bluearchive-cafe/Cafe.Launcher.Downloads) | 发行包下载镜像、同步与下载统计 |
| [Cafe.Launcher.Avalonia_Release](https://github.com/bluearchive-cafe/Cafe.Launcher.Avalonia_Release) | 遗留分发仓库，保留兼容旧链接的发行资产 |
| [bluearchive-cafe/docs](https://github.com/bluearchive-cafe/docs) | 用户文档站源码（本地配套工作区 `cafe-docs`） |

服务端与下载镜像的行为契约以各自仓库的 README 为准；修改端点、资产命名或镜像校验时，需同步核对客户端、发行工作流与对应服务仓。

