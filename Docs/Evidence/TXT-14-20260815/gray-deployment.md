# TXT-14 真实服务器灰度部署记录

## 目标与备份

- 部署根：`D:\ChuanQi\Crystal_monogame\Server-mono`
- 部署前备份：`D:\ChuanQi\Crystal_monogame\Server-mono\Backups\TXT-14-20260815-20260815-233540`
- 备份规模：6,227 个文件，491,988,717 字节
- 备份范围：原 `Server.exe`、`Setup.ini`、`server.db*`、完整 `Guilds` 与完整 `Envir`
- 原 `Server.exe` SHA-256：`53A6BE3484966E9C43DFF4F5C5A20DD37FCD2A76386202D32D496988AFF4DAEB`
- 原 `Setup.ini` SHA-256：`DDDE7F86EEE3CAEDDE43BAB71DCBFC43F98E2A727AAAFF2C793F77BAF374EF42`
- 原数据库 SHA-256：`C026953E29EF6BF1B86BF6792AFCCA1FC7882EB06ED00BFFA8FC72E045BE406B`，备份副本逐字节摘要一致

目标服 `Setup.ini` 采用最小合并；合并后 SHA-256 为 `F03C0DFBF70E1B2AA17BEFE587315A957ED28019345544C538D98AC5749043CA`。候选内容摘要为 `4968B79F650FBF20B16CCFAF4220099060EBA10E04872B58AEA2ECBD093EAD23`，线上逐文件摘要与清单一致。

## 服务端闭环

- 候选完成 360 秒真实保存周期，标准错误为 0，脚本错误和未知命令均为 0；
- 服务端优雅退出后执行 30 秒冷启动，数据库重新打开并保存成功；
- 最终部署数据库 SHA-256：`65C28C17907068B327D32374B4965BDFA305F58BE8289AE29F64ECA489CFF5F2`；变化来自正常保存、本次测试账号/角色和灰度 NPC 定位数据；
- 最终服务端进程已停止，未遗留后台进程。

实际世界入口需要数据库 NPC 定位。本次在完整数据库备份后，以事务新增唯一记录：`npc_id=496`、`map_id=1`、`file_name=TXT灰度向导`、`name=TXT灰度向导_验证`、坐标 `(288,609)`。回滚时必须在事务中执行精确条件删除，禁止按名称模糊删除：

```sql
DELETE FROM npc_infos
WHERE npc_id = 496 AND file_name = 'TXT灰度向导';
```

## 热更新审计

Android 页面闭环期间仅对 NPC 门槛做过一次测试性临时热更新，随后恢复原候选：

```text
2026-08-16T00:42:04.662+08:00 发布版本=1，摘要=4968B79F650FBF20B16CCFAF4220099060EBA10E04872B58AEA2ECBD093EAD23，错误=0
2026-08-16T00:47:14.822+08:00 发布版本=2，摘要=988170E086613E8BDCB8AE510BC29FFBBFCDE321199CED09486ACFBF0E612159，错误=0
2026-08-16T00:51:36.647+08:00 发布版本=3，摘要=4968B79F650FBF20B16CCFAF4220099060EBA10E04872B58AEA2ECBD093EAD23，错误=0
```

版本 3 与仓库候选摘要一致，是当前最终状态。Android 实交互与运行时分派证据见 `../TXT-12-20260815/README.md`。

## 回滚

1. 停止服务端；
2. 应用 `Configs/LingFengTxtPilot/rollback.fragment.ini`，关闭物理 TXT、指标与用量追踪，并把 `CSharpScriptsFallbackToTxt` 恢复为 `false`；
3. 使用上述精确 SQL 删除 `npc_id=496` 候选定位记录；
4. 如需整体恢复，使用部署前备份恢复 `Server.exe`、`Setup.ini` 和数据库；不得覆盖备份之外的新业务运行数据；
5. 冷启动并核对候选摘要不再加载、数据库正常打开。
