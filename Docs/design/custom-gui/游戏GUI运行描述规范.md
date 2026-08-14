# 游戏 GUI 运行描述规范（Schema v1）

- 状态：已接受
- 对应任务：`LEG-07 / GUI-01`
- Schema 版本：1
- 最后复核日期：2026-08-14
- 事实实现：`src/Shared/Shared/CustomGui/`
- 示例：[`../../samples/custom-gui/new-player-event.v1.json`](../../samples/custom-gui/new-player-event.v1.json)
- 语言：中文，JSON 字段、枚举值和代码标识符除外

## 1. 目标与边界

同一份运行描述必须能被 PC 与 Android Adapter 解释为同一棵静态 GUI 对象图。Schema 只表达布局、显示内容、逻辑资源标识、受限输入和不带业务结果的动作标识，不包含渲染器对象、客户端奖励、价格、物品事实、任意脚本、反射、文件路径或 URL 调用。

本版本定义线格式和兼容边界；资源数量、文本长度、嵌套深度、循环引用、字体、图集、总包大小及安全区的完整语义校验由 `GUI-05` 增加。作者工具、PC/FairyGUI Adapter 分别属于 `GUI-02..04`，不得在本切片提前实现。

## 2. 顶层文档

| 字段 | 类型 | 必填 | 语义 |
|---|---|---:|---|
| `schemaVersion` | 整数 | 是 | 当前只接受 `1`；其他版本以 `GUI01-SCHEMA-002` 失败关闭 |
| `documentId` | 字符串 | 是 | GUI 文档稳定标识，不是文件路径 |
| `revision` | 长整数 | 是 | 文档内容修订号；非负和递增约束由 `GUI-05` 校验，签名发布序列由既有发布链另行负责 |
| `viewport` | 对象 | 是 | 跨端参考画布和安全区策略 |
| `elements` | 数组 | 是 | 以稳定顺序保存的扁平对象图 |

序列化使用 UTF-8、camelCase 字段和枚举字符串。字段名区分大小写，整数枚举、未知字段、未知枚举与未知 `type` 均拒绝。生产 Codec 使用源生成 JSON 元数据，避免 PC 与 Android 各自维护一套解释器。

## 3. 视口和安全区

`viewport.referenceWidth/referenceHeight` 是作者坐标系；v1 的 `scaleMode` 只能是 `fit`，保持宽高比缩放。`safeArea` 只能是 `required`：Android 先以系统安全区收缩可用矩形，PC 使用完整客户区，然后把参考画布等比居中放入可用矩形。Adapter 不得通过裁切或非等比拉伸掩盖越界。

## 4. 对象图与公共字段

每个元素都有：

- `type`：判别类型；v1 只接受下表值。
- `id`：文档内稳定且唯一的对象标识。
- `parentId`：父容器标识；根 `window` 不设置。
- `layout`：位置、尺寸、锚点和边距。
- `visible`：静态可见状态，默认 `true`。
- `zIndex`：同一父容器内的显式层级；同值按 `elements` 顺序稳定排序。

对象图采用 `parentId` 而不是嵌套 JSON，使设计核心、差异审查和运行 Adapter 使用同一稳定标识。父对象只允许 `window`、`panel` 或 `list`；唯一根、父存在性、环和深度在 `GUI-05` 失败关闭。

## 5. 首版控件

| `type` | 对应能力 | 专有字段 |
|---|---|---|
| `window` | Window 根窗口 | `title`、`modal` |
| `panel` | Panel 容器 | `flow`、`clipChildren`、`backgroundColor` |
| `image` | Image | `assetId`、`stretch`、`alternateText` |
| `text` | Text/RichText | `content`、`format=plain/rich`、`fontId`、`fontSize`、`color` |
| `button` | Button | `text`、`actionId`、`assetId`、`enabled` |
| `textInput` | TextInput | `placeholder`、`maxLength`、`multiline`、`password`、`bindingKey` |
| `list` | List | `orientation`、`spacing`、`selectionBindingKey`、静态 `items` |
| `progressBar` | ProgressBar | `minimum`、`maximum`、`value`、`text`、`bindingKey` |
| `itemSlot` | ItemSlot | `assetId`、`displayName`、`quantity`、`bindingKey` |

`assetId` 是既有 Bootstrap 资源包中的逻辑标识，不是任意本地路径或网络地址。`actionId` 只声明动作意图；动态阶段必须由服务端登记白名单并权威处理。`bindingKey` 只是受限状态投影键，客户端不能借此声明业务表达式。

## 6. 布局语义

`layout` 包含 `x/y/width/height`、`horizontalAnchor`、`verticalAnchor` 和 `margin`：

- 非 `stretch` 时，`width/height` 是参考坐标尺寸；`x/y` 是相对左、中心或右及上、中心或下锚点的偏移。
- 水平 `stretch` 时，`x` 是左侧附加内缩、`width` 是右侧附加内缩；最终宽度再扣除左右 `margin`。
- 垂直 `stretch` 时，`y` 是顶部附加内缩、`height` 是底部附加内缩；最终高度再扣除上下 `margin`。
- 负最终尺寸、父级越界和安全区越界不得被 Adapter 自动纠正，必须由校验诊断阻断发布或加载。

`panel.flow.direction` 为 `none/horizontal/vertical`。`none` 保留子元素绝对布局；横向或纵向流按 `elements` 稳定顺序排列直接子元素，使用 `padding`、子元素 `margin` 和 `spacing`，主轴位置不再读取子元素 `x/y`，交叉轴仍使用锚点。`list.orientation` 只规定列表项方向，不改变列表控件自身布局。

## 7. 兼容与失败关闭

1. v1 Reader 只接受 `schemaVersion=1`，不猜测未来字段语义。
2. 任何未知控件、字段或枚举值均以 `GUI01-SCHEMA-001` 拒绝，不允许忽略后继续运行。
3. 缺失顶层必填字段、元素 `id/layout` 或构造值也以 `GUI01-SCHEMA-001` 拒绝。
4. Writer 对同一内存文档产生稳定 UTF-8 字节；列表排序属于作者/构建输入，不在序列化时偷偷重排。
5. Schema 升级必须增加新版本和迁移说明；不得改变 v1 已有字段的含义或复用已有判别值。

## 8. 回滚

`GUI-01` 没有数据库或协议迁移。回滚代码和本文档即可删除 v1 能力；尚未进入静态发布门禁的 GUI 包不得作为已接受客户端资源。后续若已有签名 v1 包进入兼容窗口，必须保留 v1 Reader 或先完成有证据的资源版本回退。
