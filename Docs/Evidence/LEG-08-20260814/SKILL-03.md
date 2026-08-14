# LEG-08 / SKILL-03：技能表现时间线与资源引用

- 日期：2026-08-14
- 状态：已完成
- 对应规格：[`../../requirements/LEG-08-技能可视化与安全编辑.md`](../../requirements/LEG-08-技能可视化与安全编辑.md)
- 用户入口：服务端管理器 → 技能设置列表 → 火球术 → “只读理解”页 → 表现时间线

## 可观察工件

1. 火球术时间线分开显示施法、飞行、服务端命中、客户端命中效果、持续效果与音效。
2. 服务端命中显示真实公式 `500 + 距离×50 ms`；距离 5 格样例为 750 ms，不用客户端投射物完成时刻替代权威裁决。
3. PC 与 Android/MonoGame 均显示 `Libraries.Magic` 的施法、投射物和命中帧，以及音效编号 `20000 + Spell×10 + 0/1/2`。
4. 两端代码引用一致被单独标记为已核对；外部 Magic 图像库和音频实体未在源码快照中独立验证，缺口保持可见。
5. 未核对技能明确显示“未建模”，不根据技能编号、`Range` 或相邻技能推断时间线。

## 自动验证

```text
dotnet test Tests\Base05.Tests\Base05.Tests.csproj -c Release --filter "FullyQualifiedName~SkillTimelineInspectionTests|FullyQualifiedName~SkillSpatialInspectionTests|FullyQualifiedName~SkillInspectionTests" --no-restore
已通过：10；失败：0；跳过：0

dotnet build src\Server\Server.MirForms\Server.csproj -c Release --no-restore
错误：0；警告：472
```

测试覆盖五类时间阶段、距离延迟公式、双端代码引用、资源实体缺口、未知技能失败关闭和非法样例距离。构建警告为仓库现有空引用与线程分析项。

## 不变量与回滚

- 时间线只解释现有代码，不参与战斗调度或客户端命中判断。
- 服务端权威事件和客户端表现事件在模型与 UI 中显式区分。
- 未修改协议、数据库、战斗执行、资源文件或渲染器。
- 回滚只需移除时间线投影、窗体展示、测试和本证据。

## 每日工件检查

- 运行工件：时间线模型、窗体入口、资源差异显示、4 个时间线测试、Release 构建，共 5 类。
- 过程资产：本证据与规格状态更新，共 2 类；过程资产少于运行工件。
- 语言：交流、规格、证据和提交信息使用中文；代码标识符与原始命令保留英文。
