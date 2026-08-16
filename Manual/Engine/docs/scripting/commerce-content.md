# 翎风商店、配方与规则名单

- 兼容等级：B/C（见下方边界）
- 适用布局：`TxtScriptsLayout=LingFeng`
- 最后复核：LFENV-13 / 2026-08-16

## 商城

根目录 `Shopitemlist.txt` 使用十列 Tab 分隔记录。当前可购买货币为 `0`（点券）和 `1`（元宝）；启用标记为 `1` 的记录在物品索引和名称均能匹配数据库后进入现有游戏商城。

购买继续执行现有商城停服开关、数量上限、个人/全服库存、余额、邮箱交付和审计。商品物品全部创建成功后才扣款和记库存；失败不会生成部分邮件。

以下记录只作为兼容事实保留，不会出现在可购买列表：

- 启用标记为 `0`；
- `ItemIndex=0` 的服务型商品；
- 货币类型 `4`。当前项目没有经过验证的等价货币模型，启动诊断会明确报告该差异。

## 配方

根目录 `Makeitem.txt` 每个节表示一个产出物品，节下每行最后一个字段为材料数量：

```text
[灰色药粉(少量)]
食人树叶 4
毒蜘蛛牙齿 2
```

产出物品、材料物品和数量会在候选发布前验证。NPC 页继续使用现有 `[RECIPE]` 商品区展示并调用 `NPCScript.Craft`；材料不足、重复槽位或越界槽位不会消耗背包物品。

## 规则名单

根级 `*List.txt`、`Allow*`、`Deny*`、`Disable*`、`Enable*`、`Filter*` 和 `Myshopitems.txt` 会进入独立名单 Provider。空行与整行 `;`、`//` 注释忽略，重复正文按首次出现去重。脚本可通过现有名单检查命令或 `ScriptApi.NameListContains` 查询，例如 `Denyaccountlist.txt` 对应 `NameLists/DenyAccountList`。

名单 Provider 只提供确定的只读事实，不自行添加封号、回收或物品过滤副作用；具体引擎入口必须已有消费者或在后续兼容阶段显式接入。

## 双运行时与热更新

- C# 关闭时使用物理 TXT 配方和名单。
- `TxtScriptsSourcePriority=TxtFirst` 时物理 TXT 覆盖同名 C# 定义。
- `CSharpFirst` 时先用 C#；仅当 `CSharpScriptsFallbackToTxt=true` 才补入 TXT 独有定义。
- 商城、配方或名单单独变化也会更新 TXT 快照摘要与 `ChangedKeys`；依赖或语法失败时继续使用上一完整候选。

`Market_Saved`、`Market_Storage`、`Market_SellOff`、`Market_Prices/*.prc` 等运行数据不会被文本热更新覆盖。`Market_Upg/*.upg` 也没有按文本猜测解析。
