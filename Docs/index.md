# LyoCrystal 文档索引

- 状态：已接受
- 负责人：项目所有者
- 最后复核日期：2026-08-15
- 目的：提供文档入口与事实源导航，不复制动态开发状态

## 当前状态入口

| 状态范围 | 唯一事实源 | 当前生命周期 |
|---|---|---|
| 工程治理 | [`governance/工程治理实施路线.md`](governance/工程治理实施路线.md) | ENG-13 已完成，工程治理进入维护态 |
| 当前产品任务 | [`requirements/LEG-10-统一作者工作台与路线关闭.md`](requirements/LEG-10-统一作者工作台与路线关闭.md) | LEG-10 已完成并转入维护；当前无活动产品任务 |
| PC 稳定与 Android 发布 P0～P5 | [`requirements/PRD-PC稳定与Android发布.md`](requirements/PRD-PC稳定与Android发布.md) | 已完成并转入维护，只保留历史验收事实 |
| 启动器编辑器与独立微端 L0～L5 | [`design/launcher/启动器编辑器与独立微端实施规格.md`](design/launcher/启动器编辑器与独立微端实施规格.md)及对应 Evidence | 已完成；后续新增能力必须重新立项 |
| 长期架构决定 | [`adr/`](adr/) | 已接受 ADR 为决定事实源；实施状态以代码、测试和证据为准 |

LEG-09、LEG-10 与传奇类参考源码吸收路线均已完成并转入维护；当前无活动产品任务。后续候选必须重新立项，不从历史 Evidence 或路线附录自动恢复。

## 首先阅读

| 文档 | 职责 | 权威范围 |
|---|---|---|
| [`../README.md`](../README.md) | 项目简介、构建与资源入口 | 快速开始 |
| [`../AGENTS.md`](../AGENTS.md) | Agent 执行铁律 | 全仓库执行约束 |
| [`governance/工程开发规范.md`](governance/工程开发规范.md) | 标准开发生命周期与质量门禁 | 后续工程工作方式 |
| [`governance/引擎说明书维护规范.md`](governance/引擎说明书维护规范.md) | 产品说明书的信息架构、页面标准、中文搜索和功能完成门禁 | 引擎说明书维护方式 |
| [`governance/工程治理实施路线.md`](governance/工程治理实施路线.md) | 从当前现状达到工程规范的分阶段任务 | 工程治理动态状态唯一事实源 |
| [`guides/开发者指南.md`](guides/开发者指南.md) | 环境、构建、测试、调试和产物入口 | 开发者操作事实源 |
| [`guides/模块地图.md`](guides/模块地图.md) | 子系统职责、依赖方向、接缝和测试入口 | 模块导航与变更定位 |
| [`governance/代码托管治理.md`](governance/代码托管治理.md) | PR、所有权、必需检查和主分支保护 | GitHub 治理目标与应用记录 |
| [`requirements/PRD-PC稳定与Android发布.md`](requirements/PRD-PC稳定与Android发布.md) | 已完成发布阶段的产品目标、GATE、验收和历史快照 | P0～P5 历史事实源，不是活动队列 |
| [`architecture/继续开发架构设计报告.md`](architecture/继续开发架构设计报告.md) | 2026-08-08 架构审计基线、目标架构和风险 | 历史架构基线；有效决定以 `adr/` 为准 |
| [`requirements/传奇类参考源码吸收开发路线.md`](requirements/传奇类参考源码吸收开发路线.md) | 传奇类参考源码的候选吸收范围、总顺序、玩法差距终审、门禁与排除项 | 已完成并转入维护；未激活候选仍须重新立项 |
| [`../CONTEXT.md`](../CONTEXT.md) | 启动器与微端统一术语 | 领域语言 |
| [`governance/执行纪律与防走偏铁律.md`](governance/执行纪律与防走偏铁律.md) | 防走偏、停止条件和验证成本 | 执行细则 |

## 架构决定

- [`adr/`](adr/)：启动器、玩家入口、微端、发布源和部署形态的逐项 ADR。
- 架构总览与跨项目红线以 [`architecture/继续开发架构设计报告.md`](architecture/继续开发架构设计报告.md) 为入口。
- ADR 只表达决定；实际实施状态必须由代码、测试和对应证据支持。

## 专项文档

| 类别 | 文档前缀或入口 | 职责 |
|---|---|---|
| 数据库 | [`runbooks/database/`](runbooks/database/) | SQLite/MySQL、保存、备份、恢复和 RPO/RTO |
| 安全 | [`runbooks/security/`](runbooks/security/)、[`Compliance/`](Compliance/) | TLS、秘密、管理端与微端签名 |
| 发布 | [`runbooks/release/`](runbooks/release/) | 签名、事务发布、灰度、回滚和设备验收 |
| 运维 | [`runbooks/operations/`](runbooks/operations/) | 监控、崩溃诊断、Kill Switch 和依赖审计 |
| 性能 | [`quality/performance/`](quality/performance/) | 指标、基线和有证据的优化 |
| 协议 | [`quality/protocol/`](quality/protocol/)、[`generated/protocol/`](generated/protocol/) | 协议与资源兼容；manifest 为生成审计物 |
| 启动器/微端 | [`design/launcher/`](design/launcher/)、[`requirements/`](requirements/) | 产品设计、实施规格和候选路线 |
| 桌面作者工具 GUI | [`design/launcher/桌面作者工具GUI设计规范.md`](design/launcher/桌面作者工具GUI设计规范.md) | 启动器编辑器及后续桌面作者工具的信息架构、设计令牌、交互、无障碍与验收唯一规范 |
| 自定义游戏 GUI | [`design/custom-gui/游戏GUI运行描述规范.md`](design/custom-gui/游戏GUI运行描述规范.md)、[`design/custom-gui/游戏GUI动态协议规范.md`](design/custom-gui/游戏GUI动态协议规范.md) | 静态运行描述、动态协议、上限和失败关闭语义 |
| 脚本变量系统 | [`design/scripting/翎风变量系统兼容实施规格.md`](design/scripting/翎风变量系统兼容实施规格.md) | 翎风变量行为盘点、LyoCrystal 兼容设计、分阶段进度、迁移与验证门禁 |
| TXT 脚本兼容迁移 | [`design/scripting/翎风TXT脚本兼容迁移实施规格.md`](design/scripting/翎风TXT脚本兼容迁移实施规格.md)、[`design/scripting/翎风TXT脚本兼容差异说明.md`](design/scripting/翎风TXT脚本兼容差异说明.md)、[`guides/原生TXT脚本快速开始.md`](guides/原生TXT脚本快速开始.md)、[`generated/scripting/README.md`](generated/scripting/README.md) | 原生 TXT 入口、翎风命令与触发兼容、开发顺序、审计清单、测试、灰度迁移和说明书交付门禁 |
| 引擎用户说明书 | [`../Manual/Engine/README.md`](../Manual/Engine/README.md)、[`governance/引擎说明书维护规范.md`](governance/引擎说明书维护规范.md) | Material for MkDocs 源工程、中文搜索、图文页面、离线构建和功能同步门禁 |
| 构建/Android | [`runbooks/build/`](runbooks/build/) | 平台构建与打包专项说明 |

## 证据与历史

- [`Evidence/`](Evidence/)：按 GATE 保存的执行输出、声明和快照。它们记录当时证据，不作为当前状态源。
- [`assets/images/`](assets/images/)：文档图片资源。
- [`archive/`](archive/)：已确认不再作为当前入口的历史设计、PRD 合并稿和升级记录；仅用于追溯，不覆盖当前事实源。

## 生命周期定义

| 状态 | 含义 | 是否可作为当前任务入口 |
|---|---|---|
| 草案 | 尚未接受，允许讨论和修改 | 否 |
| 已接受 | 内容或决定已经确认，但不代表实现完成 | 仅在明确指定为活动任务时可以 |
| 已实施 | 已有代码、配置、测试或运行证据支持 | 否；除非另有维护任务 |
| 已完成并转入维护 | 阶段验收结束，文档保留为历史和维护依据 | 否 |
| 已废弃 | 不再适用，仅为历史追溯保留 | 否 |
| 已取代 | 已有明确后继事实源 | 否；必须跳转到后继文档 |

Evidence 中的“进行中”“待审核”“未完成”等状态描述只代表证据生成时的截面，不随当前状态回写，也不得覆盖对应 PRD、Issue 或治理路线的最终结论。

## 文档维护规则

1. 工程治理状态只修改 [`governance/工程治理实施路线.md`](governance/工程治理实施路线.md)；产品任务状态只修改被明确指定的活动 PRD、Issue 或实施规格。当前没有活动产品 PRD 时，不得把历史文档恢复为活动队列。
2. 架构性重大决定新增一份 ADR，并明确状态和取代关系。
3. 具体功能设计不复制 PRD，只引用需求并描述实现、风险、验证和回滚。
4. Evidence 不反向改写历史结果；新证据建立新快照。
5. 新增长期文档必须更新本索引。
6. 发现冲突时先标记并确定权威来源，不新建第三份汇总文档。
