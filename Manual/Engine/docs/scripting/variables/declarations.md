# 声明、初始化与热重载

- 功能状态：实验性（TXT 与 C# 声明共用原子注册表）
- 首次支持版本：开发版 2026-08-15

## 声明变量

```text
VAR Decimal U DropRate DEFAULT 1.0
VAR Decimal G EventRate DEFAULT 2.5
VAR Integer HUMAN KillCount DEFAULT 0
```

这些 `VAR` 行可以放在物理 TXT 的 `Variables` 白名单目录、服务器启动脚本、QM 或其他已注册的 `TextFileDefinition` 中。它们在完整脚本快照发布前统一收集，不会等到人物登录后才声明。字符串默认值包含空格时可加双引号：

```text
VAR String A Notice DEFAULT "全服活动 已开启"
```

也可使用 C# 脚本注册等价声明：

```csharp
using Server.Scripting;
using Server.Scripting.Variables;

public sealed class VariableDeclarations : IScriptModule
{
    public void Register(ScriptRegistry registry)
    {
        registry.RegisterVariable(
            ScriptVariableScope.P,
            "DropRate",
            ScriptVariableKind.Decimal,
            "1.0");
    }
}
```

TXT 和 C# 声明支持 `P/D/M/N/I/U/T/G/A/J/Z/HUMAN/GUILD/GLOBAL/Call`。`S$名称` 和 `N$名称` 免声明；L$/D$ 为临时复合值，也不使用 `VAR`。同一声明可重复出现，但类型或默认契约不同会让整次候选重载失败。

当前声明注册名称、类型、作用域、默认值和来源位置，不会立即为所有人物、行会或服务器写入数据库。因此私人、行会和全局变量都可以在服务器启动或脚本加载阶段统一声明；声明不需要对应所有者在线。J/Z 的每日重置策略由作用域固定，不允许热重载改成永久变量。

## 默认值与首次写入

读取尚未写入的命名变量时，引擎返回声明中的默认值，但不会立即为所有人物生成数据库记录。第一次 `MOV/INC/DEC/MUL/DIV` 成功后才保存实际值。需要明确落盘默认值时使用：

```text
INITVAR U.SpecialRate
```

`INITVAR` 只在当前角色尚无实际值时写入默认值；重复登录执行不会覆盖存量。

若在登录 QM 中执行 `MOV U.SpecialRate 1.5`，每次登录都会覆盖旧值；只需要默认值时应直接读取声明默认值，确需落盘时使用 `INITVAR`。

## 热重载

新增声明不要求重启服务器：

1. 引擎解析新脚本和声明；
2. 完整校验候选版本；
3. 加载需要的全局持久值；
4. 在主线程安全点原子切换；
5. 新调用立即使用新声明。

C# 脚本文件由现有编译监视器处理；物理 TXT 由独立 watcher 构建候选快照。两种来源的 `VAR` 声明在发布前合并校验，并在主线程安全点切换。也可执行现有重载操作；不需要重启服务端。

解析或校验失败时，旧脚本和旧声明继续运行。

## 热重载限制

| 修改 | 行为 |
|---|---|
| 新增声明 | 允许 |
| 修改说明或显示元数据 | 允许 |
| 修改默认值 | 只影响尚无值的所有者 |
| 修改类型或作用域 | 拒绝，必须迁移 |
| 修改持久性或重置策略 | 拒绝，必须迁移 |
| 删除声明 | 不自动删除历史数据 |

发布后的注册表被冻结。脚本若在注册阶段之外调用 `RegisterVariable`，操作会被拒绝，防止声明绕过原子切换。

冲突时可在日志中查找 `DeclarationConflict`，并根据文件和行号修正声明。
