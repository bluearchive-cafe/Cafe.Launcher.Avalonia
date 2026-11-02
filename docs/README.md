# 文档导航

本仓库维护 Cafe Launcher 的产品入口、工程规则、领域契约与设计依据。用户操作步骤在[中英文文档站](https://docs.bluearchive.cafe/cafe-launcher/)维护，源码位于配套的 [bluearchive-cafe/docs](https://github.com/bluearchive-cafe/docs) 仓库。

## 使用与反馈

| 需要了解 | 入口 |
| --- | --- |
| 产品、平台、下载与首次使用 | [中文 README](../README.md)、[English README](../README.en.md) |
| 安装、设置、游戏操作与排障 | [中文用户文档](https://docs.bluearchive.cafe/cafe-launcher/)、[English documentation](https://docs.bluearchive.cafe/en/cafe-launcher/) |
| 本地数据与网络处理 | [隐私政策](../PRIVACY.md) |
| 当前版本变化 | [本次发布说明](../CHANGELOG_RELEASE.md) |
| 源码与第三方许可 | [LICENSE](../LICENSE)、[第三方声明](../THIRD-PARTY-NOTICES.md) |

## 开发与维护

| 任务 | 权威文档 |
| --- | --- |
| 准备环境、构建、测试、升级依赖、发布 | [开发指南](DEVELOPMENT.md) |
| 修改实现、程序集、DI、持久化、本地化或测试 | [开发规范](../PROJECT_CONVENTIONS.md) |
| 判断安装、校验、停止、卸载、设置或更新行为 | [领域契约](../CONTEXT.md) |
| 编写界面文案与翻译 | [术语表](../UBIQUITOUS_LANGUAGE.md) |
| 代理开始任务 | [AGENTS.md](../AGENTS.md) |
| 查看当前发现与已接受风险 | [审计台账](../CODEBASE_AUDIT.md) |

## 设计与历史资料

[设计规范](design/design-system-spec.md)定义现行视觉规则，[动效交接](design/fluent-motion-prototype-handoff.md)定义动效行为，[走查清单](design/design-walkthrough-checklist.md)用于逐表面验收。修改前先按 [ADR 索引](design/adr/README.md)核对后续修订及替代关系。

[design/](design/) 中的实施计划和 [research/](research/) 中的研究记录保留当时的目标、证据与验证边界。它们用于追溯背景；实施现行功能时，以当前代码、领域契约、开发规范及仍有效的 ADR 为依据。审计发现统一记录在审计台账。

文档维护分工、跨仓库同步和验证要求见[开发指南「文档职责」](DEVELOPMENT.md#文档职责)。
