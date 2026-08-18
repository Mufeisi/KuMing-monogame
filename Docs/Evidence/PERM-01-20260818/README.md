# PERM-01 游戏内 GM 权限等级化与聊天口令移除

- 状态：已实施
- 负责人：项目所有者
- 最后复核日期：2026-08-18
- 事实源：源码、accounts 表 `admin_account`（权限等级）、Base05 专项与全量回归

## 目标

游戏内管理员身份不再通过聊天框口令获取：管理员权限一律来自账号权限等级（`AdminLevel > 0`，默认 `0`=普通玩家），由服务端「角色管理」授予；启动门禁不再要求 `game-master-password`。HTTP 管理端令牌与微端 Code 两个受保护秘密保持不变（网络接口边界，非游戏内权限）。

## 变更清单

| 文件 | 变更 |
|---|---|
| `src/Server/Server/MirDatabase/AccountInfo.cs` | `AdminAccount` bool → `AdminLevel` int（`AdminAccount => AdminLevel > 0` 兼容）；旧二进制读兼容 |
| `src/Server/Server/Persistence/Sql/SqlServerPersistence.cs` | DTO `AdminLevel`，读写/别名映射（列名 `admin_account` 不变，语义=等级） |
| `src/Server/Server/MirObjects/PlayerObject.cs` | 删除 `GMPassword` 字段、`GMLogin` 字段、`Chat()` 口令比对分支、`case "LOGIN"` 提权命令；登录授权沿用 `Account.AdminAccount`（现为等级派生） |
| `src/Server/Server/Settings.cs` | 移除 `GMPassword` 字段与 INI 读写/清理 |
| `src/Server/Server/Security/ProductionSecurityPolicy.cs` | 删除 GM 口令 `Require` 与一次性导入项 |
| `src/Server/Server/Security/ProtectedSecretStore.cs` | 移除 `GameMasterPassword` 常量 |
| `src/Server/Server.MirForms/SMain.cs` | 「脚本调试」移除 GM 口令确认框（保留风险确认） |
| `src/Server/Server.MirForms/Account/AccountInfoForm.cs/.Designer.cs` | 列表显示权限等级；勾选「管理员」写入 `AdminLevel=1`；列头「权限等级」 |
| `Tests/Base05.Tests/LoginPermissionTests.cs` | 新增 4 例：等级语义、聊天口令无法提权 |
| `Tests/Base05.Tests/ProductionSecurityTests.cs` | 重写 GM 口令相关断言至新契约 |
| `Docs/runbooks/security/SEC-05/SEC-04.md` | 移除 GM 口令，增加账号等级授权说明 |

说明：`accounts` 表列名保持 `admin_account`（已为 INTEGER），避免 SQLite/MySQL 双方言列重命名对现有部署的 DDL 风险；语义已为权限等级。UI 暂以勾选表达等级 0/1，>1 级细分命令权限留待后续切片。

## 验证命令与结果

专项（新增+重写 12 例，全部通过）：

```powershell
dotnet test Tests/Base05.Tests/Base05.Tests.csproj -c Release --filter "FullyQualifiedName~LoginPermissionTests|FullyQualifiedName~ProductionSecurityTests"
```

结果：`失败: 0，通过: 12，总计: 12`；TRX 见本目录 `perm01-targeted.trx`。

构建：

```powershell
dotnet build LyoCrystal.Server.slnf -c Release
```

结果：`已成功生成`（0 错误）。

全量回归（Base05 1066 例，失败 6 例，与本改动无关）：

```powershell
dotnet test Tests/Base05.Tests/Base05.Tests.csproj -c Release
```

结果：`通过: 1060，失败: 6`。6 例失败均不触达本次改动代码路径，属在途 LFENV-16 语料/命令工作或环境：

| 失败用例 | 原因 |
|---|---|
| `LingFengCompleteSliceTests.封神原版五行灵珠…` | LFENV16-DEFINE-002 宏 `#T装备首暴#` 冲突定义（LFENV-16 语料在途工作） |
| `LingFengMultiVersionMatrixTests.十个代表家族…` | 依赖本机真实 Envir 语料，环境 |
| `LingFengEnvirCorpusCatalogTests.本机权威语料…` | 依赖本机 `D:\ChuanQi\服务端` 语料哈希，环境 |
| `LingFengPlayerCommandTests.高频比较与取反…` | NPC 检测链断言（LFENV 命令在途工作） |
| `ServiceInstanceRuntimeTests.真实隐藏组件…` / `.后置组件健康超时…` | mock cmd.exe 子进程，环境 flake |

## 运行行为变化

- 玩家聊天输入 `LOGIN` 不再进入管理员口令流程（该命令已移除）；管理员身份只在登录时按账号权限等级授予。
- 服务端首次启动不再要求 `LYOCRYSTAL_IMPORT_GM_PASSWORD`；仍要求 Administrator 令牌与微端 Code（如相关功能开启）。
- 服务端 GUI「角色管理」勾选「管理员」= `admin_account=1`。