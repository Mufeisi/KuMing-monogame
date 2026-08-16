# 翎风 Envir 外部依赖预检

仅复制 `Envir` 不会创建物品、怪物、地图、客户端资源或尚未接管的二进制领域配置。物理 TXT 候选会生成不可变依赖清单，并在发布日志中输出 `Accepted`、`RuntimeData`、`ExternalDependency`、`Rejected` 四类文件计数。

## 依赖等级

- `None`：兼容旧配置，不执行依赖门禁；不能据此声明 E1 或 E2。
- `E1`：核对脚本中的静态参数和领域配置引用的物品名称、物品编号、怪物名称和物理地图名。缺失项在候选发布前阻断，并保留上一完整快照。
- `E2`：在 E1 基础上，还要求显式确认动态脚本依赖、客户端契约和尚无安全 Schema 的领域配置。未提供确认清单时失败关闭，不会生成假资源。

配置示例：

```ini
[TxtScripts]
TxtScriptsDependencyLevel=E1
TxtScriptsClientContracts=
TxtScriptsDomainAdapters=
```

E2 的两个清单用分号分隔，键名必须与预检报告中的相对路径完全对应，例如：

```ini
TxtScriptsClientContracts=NpcIcons/NpcIcons.txt
TxtScriptsDomainAdapters=Market_Upg/武器升级.upg;Market_Prices/商店.prc
```

这两个字段表达“部署方已经导入或建立了等价映射”，不是让服务端读取或执行这些文件。资源导入、数据库迁移和客户端包发布必须各自使用已有发布流程。

## 失败诊断

缺失项使用以下稳定格式：

```text
LFENV15-DEPENDENCY-MISSING：level=E1;kind=ItemName;key=示例物品;source=monitems/示例怪物
```

`kind` 包括 `ItemName`、`ItemIndex`、`Monster`、`Map`、`ClientContract` 和 `DomainAdapter`。动态物品、怪物或地图表达式以 `ScriptDynamic/逻辑Key:行号` 记录为 E2 `DomainAdapter`，避免把无法静态证明的依赖冒充已满足。诊断只包含逻辑键和 Envir 相对来源，不记录宿主绝对路径。

单次启动最多展开 200 条缺失明细，超出时追加 `LFENV15-DEPENDENCY-SUMMARY` 总数；不可变报告仍保留全部缺失项，避免超大语料把日志和启动异常无限放大。

## 发布与回滚

依赖验证在主线程发布旧快照被替换之前执行；任一缺失项都会拒绝整批候选。回滚时必须把 `TxtScriptsDependencyLevel` 和两项确认清单恢复到上一配置，不能只关闭 TXT 后遗留 E2 声明。

E1 通过只说明当前目标数据库满足已抽取的服务器侧引用，不能冒充完整版本迁移。E2 还需要 LFENV-16 的完整玩法、持久化、客户端显示和重启闭环证据。
