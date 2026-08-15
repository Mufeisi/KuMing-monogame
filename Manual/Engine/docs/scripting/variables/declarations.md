# 声明、初始化与热重载

- 功能状态：实验性（当前仅 C# 注册声明）
- 首次支持版本：开发版 2026-08-15

## 声明变量

```text
VAR Decimal U DropRate DEFAULT 1.0
VAR Decimal G EventRate DEFAULT 2.5
VAR Integer HUMAN KillCount DEFAULT 0
```

当前开发版使用 C# 脚本注册等价声明：

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

`VAR Decimal P DropRate DEFAULT 1.0` 的纯 TXT 声明语法仍在规划中；TXT NPC 可以操作由 C# 启动脚本声明的 `P/D/M/N/I.名称`。Call 声明仅供 C# 调用帧使用。

声明只注册名称、类型、作用域、默认值和重置策略，不会立即为所有人物写入数据库。因此私人变量也可以在服务器启动或脚本加载阶段统一声明。

## 初始化当前所有者

```text
INITVAR U.SpecialRate 1.5
```

`INITVAR` 只在当前人物尚无该值时写入。下面的写法会在每次登录时覆盖旧值，不适合作为初始化：

```text
MOV U.SpecialRate 1.5
```

## 热重载

新增声明不要求重启服务器：

1. 引擎解析新脚本和声明；
2. 完整校验候选版本；
3. 加载需要的全局持久值；
4. 在主线程安全点原子切换；
5. 新调用立即使用新声明。

脚本文件保存后由现有 C# 热重载监视器重新编译。也可在脚本调试界面执行现有重载操作；不需要重启服务端。

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
