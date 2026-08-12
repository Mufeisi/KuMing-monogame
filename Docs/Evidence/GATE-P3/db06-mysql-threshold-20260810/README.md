# DB-06 MySQL 切换门槛证据

## 工件

- `db06-target.trx`：DB-06 专项测试；覆盖四类持续门槛、未授权正式 provider 拒绝、真实跨卷备份授权、缺异地/同卷/副本篡改失败关闭。
- `db06-base05-full.trx`：Base05 全量回归。
- `build-server-library.txt`：`Server.Library` Release 构建输出。
- `build-server-mirforms.txt`：`Server.MirForms` Release 构建输出。
- `cross-volume-proof.txt`：本轮 Windows 文件系统卷探针；专项据此真实执行 `C:` 本地副本到 `D:` 异地副本。

## 运行命令

```powershell
dotnet test Tests\Base05.Tests\Base05.Tests.csproj --no-restore --nologo --filter "FullyQualifiedName~MySqlSwitchPolicyTests" --logger "trx;LogFileName=db06-target.trx"
dotnet test Tests\Base05.Tests\Base05.Tests.csproj --no-restore --nologo --logger "trx;LogFileName=db06-base05-full.trx"
dotnet build Server\Server.Library.csproj -c Release --no-restore --nologo
dotnet build Server.MirForms\Server.csproj -c Release --nologo
```

最终计数和构建结果以本目录归档工件为准。已知 NuGet 安全告警与既有编译警告不在 DB-06 范围；未新增警告策略或依赖。

## 结果

- DB-06 专项：13/13 通过。
- Base05 全量：295/295 通过。
- `Server.Library` Release：0 错误，10 个既有警告。
- `Server.MirForms` Release：0 错误，451 个既有警告。
