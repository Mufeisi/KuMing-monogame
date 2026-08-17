# LFENV-16 单版本完整切片验证证据

## 验证范围

- 任务：`LFENV-16 单版本完整切片`。
- 固定来源：`01酷明传奇` 只验证原版命格系统；`封神` 只验证原版法宝系统。
- 隔离约束：两套版本的脚本、数据库、地图、奖励与断言没有交叉拼接。
- 本证据只关闭 LFENV-16；不代表 LFENV-17、LFENV-18、LFENV-19 或 `GATE-ALL` 已完成。

## 可运行工件

- 原样 Envir 严格预检与完整玩法闭环：`Tests/Base05.Tests/LingFengCompleteSliceTests.cs`。
- 隔离候选配置：`Configs/LingFengEnvirSlice/`。
- 人物进度与客户端表现状态：`src/Server/Server/MirDatabase/LingFengCharacterProgress.cs`、`src/Shared/Shared/Data/LingFengClientPresentationState.cs`。
- 整服文本、CSV、宏、镜像地图、副本、运行期列表和脚本引用接缝：`src/Server/Server/Scripting/LingFeng*.cs`。
- PC 与 Android 共用协议和状态模型：`src/Shared/Shared/Packet.cs`、`src/Shared/Shared/ServerPackets.cs` 及两端客户端实现。

## 专项结果

`LingFengCompleteSliceTests` 的六个验收场景全部通过，且没有资源缺失跳过：

1. 封神法宝精简包通过严格脚本预检；
2. 法宝完整玩法候选通过严格预检且 Setup 与回滚对称；
3. 酷明命格完整目录未知命令为零，跨玩法引用保留为外部依赖；
4. INI 唯一全局跳转进入真实执行，缺失外部页进入 E2 依赖；
5. 酷明原样 Envir 通过严格 E1 脚本预检；
6. 法宝玩法完成登录、NPC、任务、击杀、提交、奖励、保存和重启闭环。

第 6 项同时断言：

- 任务标记、掉落、提交和奖励均进入真实领域链；
- 金币净变化为 200，可由测试账本核对；
- 保存与重建人物对象后进度仍存在；
- PC 与 Android 的 NPC 协议字节一致。

## 门禁命令与结果

### Base05 当前阶段全量

```powershell
dotnet test Tests/Base05.Tests/Base05.Tests.csproj --configuration Release --no-build --filter "FullyQualifiedName!~LingFengMultiVersionMatrixTests" --logger "trx;LogFileName=lfenv16-full-gate-final.trx" --results-directory .tmp/test-results
```

结果：`1043/1043` 通过，`0` 失败，`0` 跳过，用时 `6 分 3 秒`。`LingFengMultiVersionMatrixTests` 是 LFENV-17 的下一阶段红测，未纳入 LFENV-16 完成声明。

### Windows 解决方案构建

```powershell
dotnet build LyoCrystal.Windows.slnf --configuration Release --no-restore
```

结果：`0` 错误；`517` 条为既有编译警告。

### 协议清单漂移

```powershell
dotnet run --project Tools/ProtocolManifestGenerator/ProtocolManifestGenerator.csproj --configuration Release -- --verify Docs/generated/protocol/protocol-wire-manifest.generated.json
```

结果：通过，生成清单与代码协议一致。

### 差异格式

```powershell
git diff --check
```

结果：无空白错误；仅报告工作区下一次写入时的 LF/CRLF 转换提示。

### 引擎说明书严格构建

```powershell
Push-Location Manual/Engine
.\.venv\Scripts\python.exe -m mkdocs build --strict --clean --config-file mkdocs.yml
Pop-Location
```

结果：通过，站点在 `10.41 秒` 内完成构建；Material for MkDocs 的上游版本提示不属于 MkDocs 严格模式错误。

### 网关正常观察性能基线

性能测试只使用不触发违规证据构造的正常 Observe 流量，保留 `5 微秒` 门槛不变；同一性能用例在当前主机负载下连续四次通过。违规证据与限流路径继续由独立测试覆盖。

## E1 / E2 结论

- E1：酷明原样 Envir 的严格候选构建通过，未知命令、未知触发、未知常量、未知标签和未归属文件均由专项测试断言为零；跨玩法资源没有被伪装成本地能力。
- E2：封神法宝至少一条完整玩法闭环通过，并覆盖持久化、重启、经济核账及 PC/Android 协议等价。
- 未声明：未取得的原 WIL/Pak 表现资源、十版本矩阵、53 根全语料、真实服灰度与正式发布仍属于后续任务。

## 回滚

- 禁用 `Configs/LingFengEnvirSlice/` 对应候选配置并恢复上一成功快照；候选发布失败时现有 Provider 保留上一完整快照。
- 数据模型升级保持向后读取；新状态没有有效值时按旧版本默认值运行。
- 本任务形成独立提交，主线合并前由项目所有者确认。
