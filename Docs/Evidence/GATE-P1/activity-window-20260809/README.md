# P1-ACTIVITY 活动窗口证据

## 结论

- 活动模式打开 `Quest` 时使用专用 `__codex_mobile_activity_fallback`，避免复用普通任务窗口造成构造异常。
- 活动模式与普通任务模式切换时，先释放旧窗口绑定并移除旧组件，避免缓存窗口跨模式复用。
- 兜底窗口包含六行活动列表、上一页、页码标签、下一页和关闭控件；分页按当前行数计算，超出首屏的活动可继续访问。
- 当前分支的活动专项测试真实结果为 `15/15` 通过。

## 验证命令与结果

```text
dotnet test Tests/Base05.Tests/Base05.Tests.csproj --no-build --no-restore --nologo --filter "FullyQualifiedName~MobileActivityStateTests" --logger "console;verbosity=minimal"

已通过! - 失败:     0，通过:    15，已跳过:     0，总计:    15，持续时间: 91 ms - Base05.Tests.dll (net10.0)
```

构建阶段首次缺少资产文件时已执行同一项目的 `dotnet restore`，随后上述无还原专项测试通过；还原输出中的既有依赖漏洞警告不属于本任务改动。

## 证据边界

- 本目录只归档活动窗口的 fallback、缓存切换、分页控件和专项测试证据。
- `runtime-activity-result.log` 的窗口创建记录来自已验证历史复核，仅用于说明 fallback 构造和修复后未新增同类构造异常。
- 本证据不宣称完整的“请求→响应→状态→UI→真机”业务闭环，也不包含商城、坐骑、PRD 或其他任务证据。
- 未归档截图；文本证据已去除本机用户名、主机名和用户目录。

## 每日工件检查

- 用户可见工件：活动窗口代码差异、活动专项测试 `15/15`、fallback 窗口树和修复后活动运行记录。
- 过程资产：领取声明与本说明。
- 结论：代码与运行输出工件数量高于过程资产；本任务未触发防走偏停止条件。

