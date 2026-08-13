# LEG-02 项目语义预检完成证据

- 日期：2026-08-13
- 分支：`codex/leg-02-semantic-preflight`
- 范围：只读领域预检、稳定诊断、故障/正常样例、真实项目扫描
- 语言：中文，代码标识符、命令和原始测试字段除外

## 工件

1. `src/Server/Server/Diagnostics/ProjectSemanticPreflight.cs`：复用现有地图、怪物、物品、NPC 与脚本注册表的只读领域服务。
2. `Tests/Base05.Tests/ProjectSemanticPreflightTests.cs`：故意破坏样例、正常样例、坐标边界、资源摘要与脚本引用回归。
3. `Tests/Base05.Tests/Leg02ExternalProjectPreflightTests.cs`：按环境变量启用的真实项目只读扫描。
4. `src/Server/Server/Scripting/ScriptCompiler.cs`：排除仅供 `extern alias` 协议兼容测试使用的程序集，保持动态脚本只引用正式 `Shared` 事实源。

## 验证结果

```text
Server.Library Release build: 通过，0 警告，0 错误
LEG-02 快内环与真实扫描入口: 通过 6，失败 0
Base05.Tests 全量: 通过 389，失败 0，跳过 0
```

测试结果文件：

- `Tests/Base05.Tests/TestResults/leg02-fast.trx`
- `Tests/Base05.Tests/TestResults/leg02-real-project.trx`
- `Tests/Base05.Tests/TestResults/leg02-full.trx`

## 真实项目只读扫描

扫描根：`D:/ChuanQi/Crystal_monogame/Server-mono`。测试只读加载现有 SQLite 世界关系、地图文件和 C# 脚本；未修改数据库、地图、脚本或资源。

```text
maps=626
monsters=1237
items=3938
npcs=495
drops=1398
shops=275
recipes=118
errors=58
diagnostics=128
publishable=no
```

发现项均带稳定代码和实体位置：

- `LEG02-SHOP-002`：58 条，集中于可达的 `游戏管理/管理任务-GM` 商店缺失商品引用；这是实际内容债务，不在本任务修改外部项目。
- `LEG02-NPC-003`：5 条警告，NPC 未匹配 TXT/C# 脚本。
- `LEG02-SHOP-001`：35 条警告，商店未匹配当前 NPC 记录。
- `LEG02-SPAWN-004`：29 条警告，刷新时间为 0。
- `LEG02-RESOURCE-000`：1 条建议，本次服务端真实扫描未提供玩家入口启动资源清单；资源存在、大小和 SHA-256 规则已由正常/破坏样例验证。

“正常项目无误报阻断”由完整正常样例验证；真实项目扫描如实返回当前内容阻断，未通过降低缺失商品规则来伪造可发布状态。

## 回滚

回滚 LEG-02 提交即可移除预检能力。服务和测试不写入真实项目，无数据回滚步骤。
