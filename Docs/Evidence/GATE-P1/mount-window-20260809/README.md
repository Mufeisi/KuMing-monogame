# P1-MOUNT 坐骑窗口误命中修复证据

## 范围

- 基线：`2697dc0e516f58c9172733fb2f7e302ad0eac302`
- 来源：已验证分支 `1848d7e` 中仅坐骑关键词提交 `29d924c`
- 代码改动：`FairyGuiHost.MobileMount.cs` 将窗口解析关键词从宽泛的 `mount/horse/ride/saddle` 收窄为 `mountwindow/mountdialog/horsewindow/saddlewindow`，保留中文“坐骑/骑乘”。这样不会把包含 `ride` 的非坐骑组件（例如 `RankingGrideItem`）作为坐骑窗口候选。
- 未修改：`FairyGuiHost.cs`、PRD、其他任务代码和其他证据目录。

## 坐骑专项自动化

命令：

```text
dotnet test Tests/Base05.Tests/Base05.Tests.csproj --filter FullyQualifiedName~MobileMount --no-restore --logger "console;verbosity=minimal"
```

真实结果：

```text
失败: 0，通过: 13，已跳过: 0，总计: 13
```

专项覆盖 `MobileMountStateTests` 与 `MobileMountHudRegressionTests`，测试运行成功。还原阶段报告的依赖安全警告不影响本次测试结果。

## 运行证据

- `FairyGui-MobileWindow-Mount-Tree.txt`：从 `1848d7e` 提取的坐骑窗口树，已确认解析结果为 `fallback`，不含本机路径。
- `mount-success.log`：从 `1848d7e` 成功日志提取的坐骑行，已去除活动、商城及设备路径信息。
- `mount-keyword-check.txt`：当前源码关键词静态核对，确认不含宽泛英文候选词。

设备真机/模拟器重新运行不在本工作树的代码专项范围内；运行树证据沿用已验证分支并仅归档坐骑行。

