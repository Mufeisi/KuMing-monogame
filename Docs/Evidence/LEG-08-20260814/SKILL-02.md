# LEG-08 / SKILL-02：目标条件与作用范围网格

- 日期：2026-08-14
- 状态：已完成；`GATE-SKILL-READONLY` 已关闭
- 对应规格：[`../../requirements/LEG-08-技能可视化与安全编辑.md`](../../requirements/LEG-08-技能可视化与安全编辑.md)
- 用户入口：服务端管理器 → 技能设置列表 → 选择技能 → “只读理解”页 → 空间档案

## 可观察工件

1. 空间档案明确展示建模状态、目标条件、中心类型、方向、主要作用点、等级附加点、代码证据和说明。
2. `FireBall/GreatFireBall/FrostCrunch` 显示“敌对对象且飞行路径可达”；`ThunderBolt` 显示单一敌对对象。
3. `FireBang/IceStorm` 按服务端 `Map.CompleteMagic` 展示以选定地图格为中心的 3×3 范围。
4. `HellFire` 按服务端 `HumanObject.HellFire` 与 `Map.CompleteMagic` 展示朝向四格；3 级追加左右两个方向各四格，附加点单独标识。
5. 其他尚未逐项核对的技能明确显示“未建模”，不使用 `MagicInfo.Range` 推断作用形状。

## 自动验证

```text
dotnet test Tests\Base05.Tests\Base05.Tests.csproj -c Release --filter "FullyQualifiedName~SkillSpatialInspectionTests|FullyQualifiedName~SkillInspectionTests" --no-restore
已通过：6；失败：0；跳过：0

dotnet build src\Server\Server.MirForms\Server.csproj -c Release --no-restore
错误：0；警告：472
```

新增测试覆盖飞行路径目标条件、3×3 范围、3 级附加方向点和未知技能失败关闭。构建警告为仓库现有空引用与线程分析项，本切片没有编译错误。

## 不变量与回滚

- 空间档案是只读投影，不参与选目标、命中、伤害或状态裁决。
- 已建模形状均引用现有服务端代码行为；未核对行为不猜测。
- 未修改协议、数据库、战斗执行、PC 渲染或 Android 渲染。
- 回滚只需移除 `SkillSpatialInspector`、窗体展示、测试和本证据，不需要恢复运行数据。

## 每日工件检查

- 运行工件：空间档案、ASCII 网格、窗体入口、4 个空间行为测试、Release 构建，共 5 类。
- 过程资产：本证据与规格状态更新，共 2 类；过程资产少于运行工件。
- 语言：交流、规格、证据和提交信息使用中文；代码标识符与原始命令保留英文。
