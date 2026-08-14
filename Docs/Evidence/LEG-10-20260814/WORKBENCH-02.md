# LEG-10 / WORKBENCH-02 验证证据

## 任务简报

- 目标：交付可持久审查的版本差异、已验签测试发布结果和候选关闭决定。
- 做：原子快照、稳定 ID 差异、测试发布结果记录、工作台入口、合服和玩法候选终审。
- 不做：替代签名发布源、连接生产数据库、实现合服器、自动删除旧路径。
- 方法约束：测试发布复用 `TestResourceReleasePublisher`；审查记录只接受已验签结果；快照不成为动态事实源。
- 预估时间：1 个实现切片。
- 完成定义：最近两次版本快照可比较；测试资源发布通过原签名自检并留有结果；所有未激活候选有保留或关闭决定。
- 语言：中文，代码标识符、命令和原始错误除外。

## 工件与验证

- 工件：`WorkbenchReviewStore`、工作台快照/差异/测试发布入口、候选与平行路径关闭记录。
- `dotnet test Tests/Base05.Tests/Base05.Tests.csproj -c Release --filter "FullyQualifiedName~WorkbenchOverviewTests" --no-restore`：3 项通过，0 项失败。
- `dotnet build src/Launcher/Launcher.Editor/Launcher.Editor.csproj -c Release --no-restore`：0 警告，0 错误。
- `dotnet test eng/WindowsIntegration/Launcher.PlayerShellIntegration/Launcher.PlayerShellIntegration.Windows.csproj -c Release --filter "FullyQualifiedName~UnifiedWorkbenchShowsOwnedVersionsCapabilitiesAndAggregatedPreflights" --no-restore`：1 项通过，0 项失败。
- Windows 集成外环先保存 Schema 17 快照，再把同一实例档案更新为 Schema 18 并保存第二份快照；差异稳定识别为 `Changed`。随后调用现有测试资源发布器生成 3 个已签名包，并原子记录版本、序列、KeyId 和输出位置。

## 安全与回滚

- 审查目录拒绝重解析点，JSON 有大小上限并原子写入；只记录已通过原发布器签名自检的测试结果。
- 快照与测试结果保存在项目 `workbench-reviews/`，不写秘密，不修改正式发布指针或数据库。
- 回滚本切片提交不会删除已生成审查工件；旧版本代码会忽略该旁路目录。

## 每日工件检查

- 可运行工件数量：审查存储 1、工作台审查入口 1、真实签名发布集成外环 1。
- 过程资产数量：关闭记录 1、验证证据 1；未超过工件数量。
- 语言：新增文档与提交信息使用中文。
