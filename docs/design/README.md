# 设计资料索引

本目录保存现行设计规范、尚未完成的实施计划与已归档的一次性记录。现行视觉与交互规则以[设计系统规范](design-system-spec.md)为准，实现进度见其[§11 实施现状](design-system-spec.md#11-实施现状)，行为契约见 [CONTEXT.md「设计系统」](../../CONTEXT.md#设计系统)；决策取舍以 [ADR 索引](adr/README.md) 为准；保留的原型见 [原型索引](../../prototypes/README.md)。

## 现行规范

| 文档 | 内容 |
| --- | --- |
| [设计系统规范](design-system-spec.md) | 视觉、token、组件与动效的现行规则；后续修订以各节及 ADR 为准 |
| [设计走查清单](design-walkthrough-checklist.md) | 逐表面验收清单；新增或调整表面时同步 |
| [Fluent 动效实验台交接](fluent-motion-prototype-handoff.md) | 动效行为与实验台说明；动效行为仍现行 |

## 待执行计划

| 文档 | 范围 | 状态 |
| --- | --- | --- |
| [桌面系统通知与激活集成计划](desktop-notifications-integration-plan.md) | 后台系统通知与激活 | 待实施 |
| [Linux 支持推进清单](linux-support-plan-2026-09-22.md) | Linux 平台适配与验收 | 推进中 |
| [设置页细化实施计划](settings-refinement-plan-2026-11-01.md) | 设置页分批细化 | 外观页第一批已实施 |
| [官方双样本参考后的后续计划](yostar-reference-follow-up-plan-2026-09-30.md) | 多游戏接入与复用路线 | 待执行 |

## 已归档记录

| 文档 | 内容 | 归档原因 |
| --- | --- | --- |
| [P1 实施计划](archive/p1-implementation-plan.md) | Token 体系重构与动态色基座 | 已完成 |
| [MaterialColorUtilities Spike](archive/color-utilities-spike.md) | M0 依赖可行性验证 | 已完成，结论已落地 |
| [Linux P0-A 实机验证记录](archive/linux-p0a-verification-2026-09-22.md) | 2026-09-22 实机五步验证 | 验证完成 |
| [Linux 进程识别判据设计](archive/linux-process-identification-design-2026-09-22.md) | P0-B 前移设计稿与实机样本 | 已由 ADR-036 落地 |

归档文档保留当时的目标、证据与验证边界，不再随实现更新；现行行为以代码、[领域契约](../../CONTEXT.md) 和仍有效的 ADR 为准。
