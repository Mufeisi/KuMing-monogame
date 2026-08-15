# C# 脚本变量 API

- 功能状态：实验性（当前支持 P/D/M/N/S/I/Call/U/T）
- 首次支持版本：开发版 2026-08-15

## 注册声明

变量声明必须在 `IScriptModule.Register` 中完成，注册表会随脚本处理器一起原子发布：

```csharp
public void Register(ScriptRegistry registry)
{
    registry.RegisterVariable(
        ScriptVariableScope.P,
        "DropRate",
        ScriptVariableKind.Decimal,
        "0");
}
```

相同声明可以由多个组合模块重复注册；内容完全一致时按幂等处理，类型或默认契约冲突时整次脚本重载失败并保留旧版本。

## 读写和运算

NPC 页面回调中使用现有的 `ScriptContext.Api`：

```csharp
ScriptVariableMutationResult moved = context.Api.MutateVariable(
    player, call, "P.DropRate", "MOV", "12.5");

ScriptVariableMutationResult increased = context.Api.MutateVariable(
    player, call, "P.DropRate", "INC", "0.25");

if (!moved.Success || !increased.Success)
{
    context.Log($"变量失败：{increased.ErrorCode} {increased.Diagnostic}");
    return false;
}
```

`command` 支持 `MOV`、`INC`、`DEC`、`MUL`、`DIV` 和 `MOD`。操作数可以是字面量，也可以是另一个变量引用。

同一组 API 也可直接使用 `D0`、`M0`、`N0`、`S0`、`I0`、`U0`、`T0`、`N$名称`、`S$名称` 和已声明的 `Call.名称`。非 P 变量不要求存在 NPC 对话对象；M 必须有当前地图，U/T 必须有角色，Call 必须传入当前 `NpcPageCall`。

## 比较和显示

```csharp
ScriptVariableCheckResult check = context.Api.CheckVariable(
    player, call, "P.DropRate", ">=", "10.5");

ScriptVariableTextResult normal = context.Api.GetVariable(
    player, call, "P.DropRate");

ScriptVariableTextResult fixedDigits = context.Api.FormatVariable(
    player, call, "P.DropRate", 2);
```

只有 `check.Success && check.Matched` 才表示比较成立。`GetVariable` 去除无意义尾零；`FormatVariable(..., 2)` 固定显示两位小数。

## 显式取整

```csharp
ScriptVariableMutationResult converted = context.Api.ConvertVariable(
    player, call, "P0", "FLOOR", "P.DropRate");
```

转换目标必须是 Integer。支持 `ROUND`、`FLOOR`、`CEIL` 和 `TRUNC`，失败时目标旧值保持不变。

## 主动结束对话

通常由 PC 或移动客户端关闭 NPC 窗口时自动通知服务端。脚本需要提前清理时可调用：

```csharp
ScriptVariableResetResult reset = context.Api.ResetConversationVariables(player, call);
```

所有变量状态仍由统一模块持有；C# API 只是薄适配器，不建立第二份存储。

## 服务端进程冒烟

开发、发布或排查热重载问题时，可以在构建产物目录执行无界面的变量专项冒烟：

```powershell
dotnet Server.dll --headless-variable-smoke
```

该命令不会监听网络，也不会加载或保存正式游戏数据。它会在独立临时目录编译声明脚本，并通过真实 `Envir`、`NPCSegment` 和 `PlayerObject` 路径验证 TXT 整数/小数运算、比较、显示、P/D/M/N/S/I 生命周期、U/T 跨小退和服务停启保留、调用帧隔离、兼容热重载和冲突保旧。数据库往返由隔离的 SQLite 集成测试负责。

成功时退出码为 `0`，并输出以 `VARIABLE_SMOKE_OK` 开头的结构化结果；其他退出码和 `VARIABLE_SMOKE_*_FAILED` 信息表示对应阶段失败。
