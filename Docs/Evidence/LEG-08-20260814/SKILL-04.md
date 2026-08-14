# LEG-08 / SKILL-04：火球术 PC / Android 表现对比

- 日期：2026-08-14
- 状态：已完成；`GATE-SKILL-VISUAL` 已关闭
- 对应规格：[`../../requirements/LEG-08-技能可视化与安全编辑.md`](../../requirements/LEG-08-技能可视化与安全编辑.md)
- 对比技能：`Spell.FireBall`（火球术）

## 可观察工件

1. 作者工具显示 PC 与 Android 的施法、飞行、命中和音效四类对比结果。
2. 火球术两端代码契约一致：`Magic[0..9]` 施法、`Magic[10..15]`/速度 30 投射物、`Magic[170..179]`/600 ms 命中、音效 `Spell×10+0/1/2`。
3. 对比器不会只给“相同/不同”结论；任何字段差异都会同时报告 PC 与 Android 的值和各自源码拥有者。
4. 图像库与音频实体不在源码仓库中，实体未核验缺口继续显示，不把代码一致冒充为资源文件存在。

## 自动与真实消费工程验证

```text
dotnet test Tests\Base05.Tests\Base05.Tests.csproj -c Release --filter "FullyQualifiedName~SkillTimelineInspectionTests|FullyQualifiedName~SkillSpatialInspectionTests|FullyQualifiedName~SkillInspectionTests" --no-restore
已通过：11；失败：0；跳过：0

dotnet build src\Clients\Client_VorticeDX11\Client_VorticeDX11.csproj -c Release --no-restore
错误：0；警告：36；耗时：9.85 秒

dotnet build src\Clients\Client_MonoGame.Android\Client_MonoGame.Android.csproj -c Debug --no-restore -p:RuntimeIdentifier=android-arm64
错误：0；警告：2910；耗时：3 分 36.53 秒

dotnet build src\Server\Server.MirForms\Server.csproj -c Release --no-restore
错误：0；警告：472
```

新增负向测试将 Android 命中资源改成 `Magic[999]`，确认差异被定位到 `Client_VorticeDX11` 与 `Client_MonoGame.Shared` 两个真实拥有者。警告均为现有工程项，本切片无编译错误。

## 不变量、缺口与回滚

- 对比器只读取表现契约，不写入客户端资源、技能配置或战斗状态。
- 服务端命中仍按 `500 + 距离×50 ms` 裁决；客户端投射物完成只触发表现。
- 外部 Magic 图像库和音频实体的加载验证并入最终 `SKILL-06` 测试服外环，当前 UI 明确显示未核验。
- 回滚只需移除双端对比投影、测试和本证据。

## 每日工件检查

- 运行工件：双端对比器、差异定位、窗体入口、11 个相关测试、PC 构建、Android 构建、服务端窗体构建，共 7 类。
- 过程资产：本证据与规格状态更新，共 2 类；过程资产少于运行工件。
- 语言：交流、规格、证据和提交信息使用中文；代码标识符与命令保留英文。
