# P1-SHOP 商城端到端证据索引

本目录收录商城专项测试输出、脱敏逍遥运行事实和数据库购买前后摘要；外部设备与数据库原件不入库。

## 每日工件检查

- 用户可见工件数量：4 项
  1. 商城代码差异：`FairyGuiHost.cs`、`GameShopState.cs` 与 `GameShopStateTests.cs`（提交 `0c01bbf`）。
  2. 专项测试输出：`test-output.txt`，`GameShopStateTests` 24/24 通过。
  3. 逍遥运行证据：`runtime-evidence.md`，记录 135 商品、15 固定格、9 页及连点单请求。
  4. 数据库前后证据：`database-before-after.md`，记录元宝、邮件、商品与快照 SHA-256 摘要。
- 过程资产数量：2 项（`CLAIM.md`、本 `README.md`）。
- 结论：用户可见工件 4 项，大于过程资产 2 项，未触发过程资产占主导的停止条件。
- 语言：本目录文档与状态内容均使用中文；英文仅保留代码标识符、命令、哈希、测试原文和不可翻译技术名词。
