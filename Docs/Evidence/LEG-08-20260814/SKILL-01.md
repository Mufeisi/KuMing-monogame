# LEG-08 / SKILL-01：技能只读理解

- 日期：2026-08-14
- 状态：已完成
- 对应规格：[`../../requirements/LEG-08-技能可视化与安全编辑.md`](../../requirements/LEG-08-技能可视化与安全编辑.md)
- 用户入口：服务端管理器 → 技能设置列表 → 选择技能 → “只读理解”页

## 可观察工件

1. `Server.Authoring.SkillInspector` 从现有 `MagicInfo` 生成不可写快照，展示技能标识、技能书解析、0～3 级角色等级、熟练度、MP 消耗、冷却和运行结果区间。
2. 结果上界按服务端 `Random.Next` 的排他上界计算，不沿用旧窗体把随机宽度直接当作可达最大值的展示偏差。
3. 冷却减免得到负值、名称为空、倍率为负或技能书缺失时生成只读诊断；本切片不修改或保存原对象。
4. 现有 `MagicInfoForm` 新增“只读理解”页，并明确配置事实源、服务端行为拥有者和客户端非权威边界。

## 自动验证

```text
dotnet test Tests\Base05.Tests\Base05.Tests.csproj -c Release --filter FullyQualifiedName~SkillInspectionTests --no-restore
已通过：2；失败：0；跳过：0

dotnet build src\Server\Server.MirForms\Server.csproj -c Release --no-restore
错误：0；警告：472

git diff --check
退出码：0
```

构建警告来自仓库现有空引用和线程分析规则，本切片没有编译错误。未使用可见桌面自动化，因此本切片没有伪造窗体截图；真实 PC/Android 表现属于后续 `GATE-SKILL-VISUAL`，不在本切片范围内。

## 不变量与回滚

- 未修改 `MagicInfo` 序列化、协议、数据库 Schema、战斗执行或客户端表现。
- 只读快照复制基础值并返回只读集合；测试证明诊断与投影不会改变输入对象。
- 回滚本切片只需移除窗体只读页、`SkillInspector`、测试和对应文档，不需要恢复运行数据。

## 每日工件检查

- 运行工件：只读领域投影、真实窗体入口、2 个行为测试、Release 构建输出，共 4 类。
- 过程资产：活动规格与本证据，共 2 类；过程资产少于运行工件。
- 语言：交流、规格、证据和提交信息均使用中文；代码标识符与命令保留英文。
