# CONTENT-05：资源引用闭环证据

- 执行日期：2026-08-13
- 事实源：[`../../requirements/LEG-06-内容生产工作台.md`](../../requirements/LEG-06-内容生产工作台.md)
- 固定基线：`main@4ee4a20`
- 语言：中文

## 可运行工件

- LibraryEditor 增加资源引用与库编辑工作区：缺失引用、反向引用、重复候选和未使用候选均可见，未使用候选不会自动删除；反向引用与缺失诊断可定位到主清单或分包清单的拥有记录。
- 主清单与分包清单由 Shared 的统一读取接缝合并，客户端和 LibraryEditor 不再分别维护解析规则。
- 资源库编辑使用独立草稿会话：图像与帧表变更可审查，`Ctrl+S` 显式保存，`Ctrl+R` 重载；缺失引用及资源库非法值会在保存前阻断；校验、取消、关闭或保存失败不改变内存事实对象与磁盘事实，保存使用同目录临时文件原子替换。
- `F6` / `Shift+F6` 在左栏、主表面、右栏之间正向/反向循环焦点。
- 1280×800 显示右侧分析栏；1100×700 自动折叠并保留“显示分析”入口；新功能文字为 12F。

## 自动验证

- `LibraryEditor.csproj` Debug 构建：0 警告、0 错误。
- `LibraryEditor.csproj` Release 构建：0 警告、0 错误；`Server.MirForms` Release 构建：0 错误（既有警告未在本切片处理）。
- `Client_MonoGame.Shared.csproj` 的 `net10.0` 目标：0 错误（既有可空性警告未在本切片处理）。
- `Server.ContentAuthoringIntegration.Windows.csproj`：26/26 通过，其中 CONTENT-05 资源测试 13/13。
- `Base05.Tests` 全量：433/433 通过。
- 四档 DPI（96/120/144/192）及 1280×800、1100×700 两档窗口均由后台 STA 自动化验证；无鼠标、键盘模拟或焦点抢占。

## 窗口证据

- [1280×800 完整工作区](CONTENT-05-resource-workspace-1280x800.png)
- [1100×700 折叠工作区](CONTENT-05-resource-workspace-1100x700.png)
- [资源引用分析面板](CONTENT-05-resource-analysis-panel.png)

## 恢复与回滚

- 数据恢复：保存失败时临时文件被清理，内存事实对象与原 `.Lib` 均保持不变且独立草稿可重试；重载从原文件恢复。
- 资源释放：主库、草稿、参考库、阴影库与参考图片均有明确所有权；保存、重载、切换和关闭会释放被替换的 Bitmap/GDI 资源，重载异常保留原草稿并返回可恢复错误。
- 代码回滚：回滚 CONTENT-05 独立提交；不修改 Schema、协议、资源清单格式或客户端运行时。
