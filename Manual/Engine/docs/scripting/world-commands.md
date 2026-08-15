# 地图、怪物与宝宝命令

- 兼容等级：B
- 适用入口：NPC 的 `#IF` 与 `#ACT`
- 最后复核：LFM2-2026-08-15-snapshot

## 地图检测与传送

选择 `LFM2-2026-08-15-snapshot` 后，`CHECKMAPNAME`、`ISONMAP` 分别作为 `CHECKMAP` 的别名，`TELEPORT` 作为 `MOVE` 的别名：

```text
#IF
CHECKMAPNAME 0
ISONMAP 比奇省
#ACT
TELEPORT 0 333 333
TELEPORT 0 0 0
```

正坐标执行精确传送，`0 0` 使用现有随机落点。负坐标、仅一个坐标为零、非整数或地图不存在时不修改玩家位置。兼容版本为空时不注册这些翎风别名。

## 怪物检测与清理

```text
#IF
CHECKMON < 100 0 1
#ACT
MONCLEAR 0 1 稻草人
```

`CHECKMON` 检测指定地图实例的怪物数量；玩家上下文沿用 Crystal 既有规则，从地图怪物计数中扣除当前玩家宝宝数。`MONCLEAR` 可选按怪物名过滤，并跳过主人为玩家的宠物或召唤物。

`CLEARMAPMON` 仍为 D，不会被严格快照接受。该翎风命令要求保留引擎“禁止清除名单”中的怪物，而当前项目没有对应配置模型；不得用 `MONCLEAR` 静默替代。

翎风 `Mongen.txt` 的配置行、条件标签和 `RegMon` 触发属于世界配置 Provider，不能用 NPC 动作 `MONGEN` 冒充，当前仍为 D。

## 宝宝

```text
#IF
PETCOUNT < 5
CHECKPET 虎卫
#ACT
GIVEPET 虎卫 2 3
CLEARPETS
```

`GIVEPET` 数量限制为 1 至 5，等级限制为 0 至 7；怪物定义不存在或参数越界时不生成部分宝宝。`CLEARPETS` 在服务端主循环把当前玩家全部宝宝标记为死亡，继续使用现有怪物对象同步。

## 排错

- 地图名称可使用项目当前地图定义能解析的代码或名称，无法解析时检测失败或动作无副作用。
- 精确传送仍由地图对象执行最终落点处理；脚本层不会绕过地图有效点规则。
- 所有玩家、地图和怪物状态修改都在现有 NPC 主线程执行路径提交。
