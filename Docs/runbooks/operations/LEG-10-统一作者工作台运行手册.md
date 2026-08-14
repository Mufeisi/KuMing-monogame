# LEG-10：统一作者工作台运行手册

- 状态：已接受
- 负责人：项目所有者
- 最后复核日期：2026-08-14
- 适用范围：统一总览、聚合预检、版本快照差异和测试发布审查
- 事实源：[`LEG-10-统一作者工作台与路线关闭.md`](../../requirements/LEG-10-统一作者工作台与路线关闭.md)

## 1. 标准操作

1. 在 `Launcher.Editor` 打开目标项目，进入“概览 → 统一工作台”。
2. 先运行统一预检。每条结果必须显示原始 Owner；项目发布、发行体、玩家入口或实例任一失败时，在对应模块修复，不在工作台内改写规则。
3. 在变更前保存一次版本快照，完成受控修改并重新预检后再保存一次。
4. 比较最近两次快照，审查新增、删除、修改和未变事实；意外变化回到事实所有者处理。
5. 仅在预检和局部回归通过后生成测试资源发布。工作台复用既有签名发布链，并只记录验证通过的测试发布结果。

## 2. 工件与所有权

| 工件 | 位置 | 所有权与用途 |
|---|---|---|
| 版本快照 | `<项目根>/workbench-reviews/snapshots/*.json` | `Launcher.Workbench` 审查工件；不替代各模块版本事实 |
| 测试发布审查 | `<项目根>/workbench-reviews/test-releases/*.json` | 对既有签名测试发布结果的旁路记录 |
| 测试发布输出 | 用户选择的隔离输出目录 | 仍由现有发布器拥有；不得覆盖正式发布目录 |
| 实例日志与审计 | 实例档案声明的运行目录 | 仍由 `Launcher.InstanceManagement` 拥有 |

项目根和审查目录若是重解析点，工作台拒绝写入。档案、快照和审查记录不得包含秘密值，只允许秘密引用。

## 3. 故障处理

- 单个事实提供器失败：保留其他提供器结果，根据 Owner 和原始错误定位；不要把总览空白当作全部模块失败。
- 快照无法保存：确认项目根存在、不是重解析点且当前账号可写；不要改到仓库外的生产目录。
- 测试发布失败：保留发布器原始错误和隔离输出，修复后重新生成；失败结果不会被记录为“已验证”。
- 实例预检失败：回到“实例”页检查端口、根目录、依赖、秘密引用和正式环境保护；不要用强制终止绕过预检。

## 4. 回滚

统一工作台不改运行数据库和正式发布状态。回滚审查工件时，先关闭编辑器，再删除目标项目下对应的单个快照或测试发布审查 JSON；不要递归删除项目根或测试发布输出。代码回滚按 `WORKBENCH-03 → WORKBENCH-02 → WORKBENCH-01` 逆序执行。

## 5. 验证入口

```powershell
dotnet test Tests/Base05.Tests/Base05.Tests.csproj -c Release --filter "FullyQualifiedName~WorkbenchOverviewTests"
dotnet test eng/WindowsIntegration/Launcher.PlayerShellIntegration/Launcher.PlayerShellIntegration.Windows.csproj -c Release --filter "FullyQualifiedName~UnifiedWorkbench"
dotnet build LyoCrystal.Launcher.slnf -c Release
```

阶段末还应执行完整 Base05、Launcher Windows 集成测试以及 PC、Android、服务端构建；正式 Android 和发布制品仍以 CI 签名流程为准。
