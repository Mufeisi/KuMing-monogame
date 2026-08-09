# GATE-P1 使用探针闭环集成证据

语言：中文

## 范围

- ANDROID-01：沿用已合并的逍遥商城渲染、双币选择、单次购买与数据库前后证据。
- ANDROID-02..07：在已有逍遥入口/窗口证据上，分别以真实协议类型、请求门控、权威响应、状态和 FairyGUI 实际 UI 投影完成使用探针冒烟。
- 不把探针结果表述为完整双角色、专用资源、真实钓点或活动开放场景的设备操作。

## 专项结果

| 项目 | 结果 |
|---|---:|
| 师徒 | 11/11 |
| 关系 | 14/14 |
| 坐骑 | 8/8 |
| 封印/租赁 | 31/31 |
| 钓鱼 | 12/12 |
| 活动/赏金 | 16/16 |

## 集成回归

命令：`dotnet test Tests/Base05.Tests/Base05.Tests.csproj -c Release --no-restore --logger "console;verbosity=minimal"`

结果：Base05 223/223 通过，失败 0，跳过 0，持续时间 1 分 4 秒。构建输出仍含仓库既有 nullable、过时 API 和依赖漏洞告警，本轮未引入新的失败。

## 当前状态

GATE-P1 已关闭。实现提交 `2567e2c0a915ee3f3b2dfe1b715f30ba0becd1e3` 的远程 [BASE-03 CI 31313826844](https://github.com/Mufeisi/KuMing-monogame/actions/runs/31313826844) 全绿：

- Windows build (solution filter)：通过。
- General tests (discovered projects)：通过。
- Android Release arm64 AOT publish：通过，发布物上传成功。
