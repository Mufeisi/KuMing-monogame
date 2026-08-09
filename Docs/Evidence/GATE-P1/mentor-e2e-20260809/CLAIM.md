# P1-MENTOR 师徒端到端任务领取声明

## 任务简报

- 会话：ANDROID-02 独立任务会话（来源任务 `019fe013-c20f-7ed3-9f27-db24679efa06`）
- 目标：交付 Mentor/Mentee 师徒“请求→响应→状态→UI→逍遥实机等效设备”业务闭环。
- 做：复用并核验现有师徒协议、服务端处理、移动端 `MobileMentorState`、FairyGUI 师徒窗口与专项测试；只修复阻塞闭环的真实缺口；归档真实专项测试输出、请求/响应/状态/UI 和逍遥窗口证据。
- 不做：不修改 PRD 或架构/汇总文档；不碰关系、坐骑、封印/租赁、钓鱼、活动、商城；不重造 FairyGUI、协议、微端或服务端已有通用机制；不合并、不推送；不输出或提交账号、密码、用户名、主机名、用户目录等敏感字段。
- 方法约束：基于干净 `54b3dba`；先复用现有实现与测试，验收已经闭环则不制造代码 diff；协议只以 `Shared/` 为事实源；玩家状态遵守主线程边界；移动绘制继续走现有 `SpriteBatchStack`/FairyGUI 接缝；服务端/客户端从 `D:\ChuanQi\Crystal_monogame` 启动，逍遥设备使用 `127.0.0.1:21503`，反向端口使用 7000/7777；证据脱敏。
- 预估时间：≤ 3 小时。
- 完成定义：必要的师徒代码/测试 diff（如确有缺口）；真实专项测试通过输出；逍遥设备上师徒请求、响应、关系状态刷新及 UI 可见证据；证据目录包含脱敏 README/日志/截图或 UI 树；CLAIM 状态改为“待审核”；工作树干净。
- 语言：本任务交流、文档、状态报告与提交信息使用中文；英文仅限代码标识符、命令、报错原文及不可翻译的技术名词。

## 状态与边界

- 任务：P1-MENTOR（ANDROID-02）Mentor/Mentee 师徒请求、响应、状态与 UI 闭环
- 状态：进行中
- 分支：`codex/p1-mentor-e2e-20260809`
- 工作树：`C:\Users\luo\.codex\worktrees\c750\LyoCrystal-main`
- 基线：`54b3dba`（当前 `main`/`origin/main`）
- 代码与测试所有权：
  - `Client_MonoGame.Shared/MirScenes/MobileMentorState.cs`
  - `Client_MonoGame.Shared/UI/FairyGui/FairyGuiHost.MobileMentor.cs`
  - `Tests/Base05.Tests/MobileMentorStateTests.cs`
  - 上述文件直接引用的师徒协议/服务端处理，仅在闭环阻塞且不扩大范围时修改
- 证据所有权：`Docs/Evidence/GATE-P1/mentor-e2e-20260809/`
- 外部测试目录：`D:\ChuanQi\Crystal_monogame`；只启动现有服务、创建临时测试账号/角色并保留既有运行数据，不清理或覆盖既有账号数据。
- 设备定义：按 2026-08-09 PRD/ADR 决定，逍遥模拟器为 Android 实机等效设备；完整步骤通过后才可标注“实机通过”。
