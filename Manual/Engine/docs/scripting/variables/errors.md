# 错误与排查

- 功能状态：实验性（VAR-01 错误码已实现）
- 首次支持版本：开发版 2026-08-15

| 错误代码 | 含义 | 处理方式 |
|---|---|---|
| `UnknownReference` | 变量不存在或作用域未知 | 检查声明、拼写和限定名 |
| `DeclarationConflict` | 同名声明的类型或作用域冲突 | 根据日志文件和行号统一声明 |
| `TypeMismatch` | 值或操作与声明类型不符 | 显式解析或取整，不要依赖隐式转换 |
| `Overflow` | 结果超过类型范围 | 降低输入或重新设计单位 |
| `ScaleExceeded` | 小数位超过允许范围 | 调整输入或显式格式/舍入 |
| `InvalidExpression` | 公式非法或除零 | 检查表达式和除数 |
| `ContextUnavailable` | 当前人物、NPC 或目标不存在 | 在正确事件和作用域中调用 |
| `TargetOffline` | 第一版不允许离线跨人物写入 | 改为目标在线操作或使用受支持事务入口 |
| `QuotaExceeded` | 变量数量或内容超过配额 | 删除无用临时数据或拆分业务 |
| `WrongThread` | 非服务器主线程修改状态 | 通过现有主线程调度执行 |

## 兼容预检失败

`--variable-preflight` 返回 3 或 `LingFengCompatible` 拒绝启动时，先查看[兼容模式与迁移](compatibility-migration.md)中的诊断表。不要通过填写旧摘要绕过检查：脚本内容变化后必须重新扫描、审核，并把新摘要写入 `ScriptVariableCompatibilityAcknowledgement`。

## 热重载失败

热重载失败不会应用半个版本。检查日志中的：

1. 声明文件和行号；
2. 冲突变量的旧类型与新类型；
3. 默认值是否能按声明类型解析；
4. 持久存储是否能安全加载；
5. 脚本是否引用了未声明变量。

TXT 适配器的失败会写入服务端消息队列，前缀为 `[Variables][TXT]`。C# API 直接返回 `Success`、`ErrorCode` 和 `Diagnostic`；调用者不要只读取结果值而忽略 `Success`。

修复后再次保存脚本即可重试，不要求重启服务器。
