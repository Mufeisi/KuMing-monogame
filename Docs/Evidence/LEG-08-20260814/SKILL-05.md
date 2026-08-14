# LEG-08 / SKILL-05：技能白名单安全编辑

- 日期：2026-08-14
- 状态：已完成；下一切片为 `SKILL-06`
- 对应规格：[`../../requirements/LEG-08-技能可视化与安全编辑.md`](../../requirements/LEG-08-技能可视化与安全编辑.md)

## 可观察工件

1. 技能编辑窗体只允许名称和图标进入草稿；等级、消耗、伤害、冷却、范围和倍率字段保持只读，并由事件写入守卫阻断事实对象修改。
2. 作者可查看稳定诊断码与字段级差异，只有“显式保存”会调用现有 `Envir.SaveDB()`；关闭、取消和重载均不隐式保存。
3. 每个 `MagicInfo` 拥有独立编辑会话，切换技能保留草稿；外部并发修改会产生 `LEG08-SKILL-CONFLICT-001` 并阻止覆盖。
4. 持久化失败时先恢复名称与图标原值，再保留草稿供修正或重试；失败不会留下半写入事实对象。

## 自动验证

```text
dotnet test Tests\Base05.Tests\Base05.Tests.csproj -c Release --filter "FullyQualifiedName~SkillEditingSessionTests|FullyQualifiedName~SkillTimelineInspectionTests|FullyQualifiedName~SkillSpatialInspectionTests|FullyQualifiedName~SkillInspectionTests" --no-restore
已通过：16；失败：0；跳过：0

dotnet build src\Server\Server.MirForms\Server.csproj -c Release --no-restore
错误：0；本切片新增代码告警：0
```

覆盖的失败路径包括空名称、超长名称、控制字符、并发事实冲突和持久化异常。窗体工程仍有既存空值分析告警，本切片没有引入错误。

## 不变量与回滚

- 编辑会话直接包裹现有 `MagicInfo`，没有建立第二份可写技能事实源。
- 白名单不含伤害、目标、范围、冷却或状态结果，客户端权威边界不变。
- 回滚本切片只需移除编辑会话、窗体安全编辑接缝、测试和本证据；未触达协议与数据库结构。
- `SKILL-06` 继续验证真实二进制保存/重载、服务端重验、协议兼容、战斗回归和三端冒烟。

## 每日工件检查

- 运行工件：编辑会话、窗体安全编辑接缝、5 个新增失败/成功路径测试、服务端窗体构建，共 4 类。
- 过程资产：本证据与规格状态更新，共 2 类；过程资产少于运行工件。
- 语言：交流、规格、证据和提交信息使用中文；代码标识符、诊断码与命令保留英文。
