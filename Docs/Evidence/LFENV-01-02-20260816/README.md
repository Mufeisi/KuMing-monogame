# LFENV-01/02 验证证据

- 状态：已实施
- 负责人：项目所有者 / Codex
- 验证日期：2026-08-16
- 范围：LFENV-01 语料去重与版本画像；LFENV-02 服务器常量完整目录

## 工件摘要

- `lingfeng-envir-roots.csv`：53 个 Envir 根、68,140 个文件、580,922,342 字节、24 个版本家族，并逐根记录文本编码分布。
- `lingfeng-server-symbols.csv`：905 行；附件 281 个原始表达式全部保留；限定 53 个 `Envir*` 根后，513 个归一化符号族共出现 627,292 次。
- 敏感项 `PASSWORD`、`MACHINEID`、`GAMEDIRECTORY`、`M2DIRECTORY` 均为 X，不直接暴露。
- 当前旧实现仅记录为 D，等待 LFENV-03 至 LFENV-05 的统一解析模块和行为契约测试。

## 自动化验证

执行命令：

```powershell
dotnet test Tests\Base05.Tests\Base05.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~LingFengEnvirCorpusCatalogTests" --logger "trx;LogFileName=lfenv-01-02-targeted.trx"
```

定向结果：5/5 通过，0 失败，0 跳过；其中本机语料测试逐根重算文件、字节、哈希和编码，重新扫描 513 个在用符号族，并核对附件 281 个原始表达式。TRX：`lfenv-01-02-targeted.trx`。

随后执行 Base05 全量回归：703/703 通过，0 失败，0 跳过，用时 2 分 19 秒。TRX：`lfenv-01-02-full.trx`。提交版 TRX 已脱敏本机用户名、设备名和用户目录，不改变测试计数与结果。

## SHA-256

- `lingfeng-envir-roots.csv`：`E6DE1B8A608AEB050A2D72AF6FB14786D724510968E909682B81AC75F1AA9CEC`
- `lingfeng-server-symbols.csv`：`03C4BD0F5270E0C7E92BC5830E17D6819561F3C4DF3C7D472A0FD15207A402B0`
- `lfenv-01-02-targeted.trx`：`29238808517C129D0099F6052F6E927FCB6DD2D3DA5E89D6AB9A0FD813931AE9`
- `lfenv-01-02-full.trx`：`8CFC8BF73ACA7AE06B88F9A53872710FCF350A66410B7AFEF8520FD4FB83FDAE`

哈希对应本阶段最终定向测试时的工作树。后续阶段若修改目录或测试，必须重跑并刷新证据。
