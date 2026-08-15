<section class="lyo-hero" markdown>

# LyoCrystal 引擎说明书

面向服主、脚本作者和运维人员的统一产品知识库。通过分类导航、中文全文搜索、完整示例和错误代码快速定位答案。

[开始阅读](getting-started/index.md){ .md-button .md-button--primary }
[变量系统](scripting/variables/overview.md){ .md-button }

</section>

!!! warning "当前说明书状态"
    变量系统已交付 `VAR-01`～`VAR-06`，并完成 `VAR-07` 的在线跨角色访问与兼容预检工具：运行时、私人、全局、每日和行会作用域，命名 Decimal、L$/D$、受控公式、显式单位概率、声明热重载及 Legacy/SQLite/MySQL 持久化可在开发版使用。真实运行服仍需按[兼容模式与迁移](scripting/variables/compatibility-migration.md)执行审核和冒烟。

<div class="lyo-card-grid" markdown>

<a class="lyo-card" href="getting-started/index.html">
<strong>快速开始</strong>
<span>了解分类、搜索、页面状态和离线阅读方式。</span>
</a>

<a class="lyo-card" href="scripting/index.html">
<strong>脚本开发</strong>
<span>按概念、命令、示例和错误排查学习脚本能力。</span>
</a>

<a class="lyo-card" href="scripting/variables/decimal.html">
<strong>小数变量</strong>
<span>查看 Decimal 声明、运算、显示和几率单位。</span>
</a>

<a class="lyo-card" href="reference/feature-status.html">
<strong>功能状态</strong>
<span>确认哪些能力已发布，哪些仍处于规划阶段。</span>
</a>

</div>

## 如何查找内容

- 使用页面顶部搜索框输入中文功能名、命令、变量前缀或错误代码。
- 从左侧导航按系统分类浏览。
- 在命令页面查看语法和参数，在示例页面查看完整组合用法。
- 遇到失败时先记录稳定错误代码，再进入对应“错误与排查”页面。

## 内容原则

- “已实现”必须有代码、测试和发布版本支持。
- “实验性”表示代码和自动验证已完成，但尚未随正式版本发布。
- “规划中”只表达目标用法，不能用于当前正式服。
- 示例必须完整、可复制，并明确生命周期、单位和失败行为。
- 本说明书不收录密码、密保答案、私钥或生产数据。

## 当前专题

变量系统是第一套按商业产品说明书标准建设的专题，包含作用域、声明、热重载、小数、命令、组合示例和错误参考。实现状态仍以功能状态页为准。
