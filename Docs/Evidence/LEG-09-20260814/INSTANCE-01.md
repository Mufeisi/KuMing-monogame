# LEG-09 / INSTANCE-01 验证证据

## 任务简报

- 目标：交付可保存、重载并在启动前失败关闭的服务实例档案。
- 做：实例、组件、端口偏移、依赖、健康探针、版本、日志和秘密引用模型；原子存储；稳定诊断。
- 不做：启动进程、作者工作台界面、生产运行、共享账号库。
- 方法约束：复用现有解决方案和测试入口；档案独立于启动器项目；不保存秘密值；不创建分析工具。
- 预估时间：1 个实现切片。
- 完成定义：合法档案可连续覆盖保存并重载；路径越界、端口冲突、依赖环和生产秘密值被阻断；启动器编辑器可引用新模块。
- 语言：中文，代码标识符、命令和原始错误除外。

## 工件与验证

- 工件：`Launcher.InstanceManagement` 实例档案模块、档案测试、解决方案与文档入口更新。
- `dotnet test Tests/Base05.Tests/Base05.Tests.csproj -c Release --filter "FullyQualifiedName~ServiceInstanceProfileTests" --no-restore`：3 项通过，0 项失败。
- `dotnet build src/Launcher/Launcher.Editor/Launcher.Editor.csproj -c Release --no-restore`：0 警告，0 错误。
- `git diff --check`：通过。

## 每日工件检查

- 可运行工件数量：实例档案模块 1、自动化测试 1。
- 过程资产数量：立项规格 1、验证证据 1；未超过工件数量。
- 语言：新增文档与提交信息使用中文。

## 回滚

本切片只新增旁路模块与档案目录约定。回滚对应提交即可，不修改运行数据库、服务配置或用户数据。
