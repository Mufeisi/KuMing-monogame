# 只读系统占位符

- 功能状态：实验性（真实项目按需审核清单已建立）
- 首次支持版本：开发版 2026-08-15

系统占位符使用 `<$名称>` 读取人物、地图、装备或攻城状态，不保存到变量数据库，也不能通过 `MOV` 修改。例如：

```text
你好，<$USERNAME>
当前地图：<$MAPNAME>（<$X_COORD>,<$Y_COORD>）
生命：<$HP>/<$MAXHP>
```

## 当前真实项目审核清单

预检从指定真实项目识别出 47 种占位符，现有解析器均已有对应行为：

| 分类 | 占位符 |
|---|---|
| 人物与在线状态 | `USERNAME`、`LEVEL`、`CLASS`、`HP`、`MAXHP`、`MP`、`MAXMP`、`PKPOINT`、`CREDIT`、`GAMEGOLD`、`USERCOUNT`、`DATE` |
| 地图与 NPC | `MAP`、`MAPNAME`、`X_COORD`、`Y_COORD`、`NPCNAME`、`MONSTERCOUNT`、`PARCELAMOUNT`、`OUTPUT`、`ROLLRESULT` |
| 装备与坐骑 | `WEAPON`、`ARMOUR`、`HELMET`、`NECKLACE`、`BRACELET_L`、`BRACELET_R`、`RING_L`、`RING_R`、`AMULET`、`BELT`、`BOOTS`、`STONE`、`TORCH`、`MOUNT`、`MOUNTLOYALTY` |
| 行会与攻城 | `GUILDNAME`、`GUILDWARFEE`、`GUILDWARTIME`、`CONQUESTOWNER`、`CONQUESTGOLD`、`CONQUESTRATE`、`CONQUESTSCHEDULE`、`CONQUESTGATE`、`CONQUESTGUARD`、`CONQUESTSIEGE`、`CONQUESTWALL` |

`OUTPUT` 带参数，用于显示旧式变量内容；新变量优先使用 `$STR(...)` 或 `$FORMAT(...,位数)`。系统同时允许变量显示入口 `STR`、`FORMAT` 和只读当前目标入口 `C`。

## 安全边界

兼容预检明确拒绝 `PASSWORD`、`PASSWORD2`、`SECURITYQUESTION`、`SECURITYANSWER`、`PHONE` 和 `EMAIL`。这些数据不因旧商业引擎可能存在同名能力而开放，命中时返回 `VAR08-SENSITIVE-001` 并阻止兼容模式启用。

不在审核清单中的新占位符返回 `VAR08-UNKNOWN-001`。处理顺序是：确认真实脚本确实需要、核对现有实现、审查权限与隐私、补测试和本页说明，最后更新脚本摘要。禁止一次性开放数百个没有真实引用的符号。

## 相关页面

- [兼容模式与迁移](compatibility-migration.md)
- [操作与显示命令](commands.md)
- [错误与排查](errors.md)
