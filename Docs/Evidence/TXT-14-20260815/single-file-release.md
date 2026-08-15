# TXT-14 单文件发布物验证

- 验证日期：2026-08-16
- 运行时标识：`win-x64`
- 发布模式：Release、自包含、单文件、无调试符号
- 输出：`artifacts/TXT-14/server-win-x64/Server.exe`
- 文件大小：233,953,284 字节
- SHA-256：`C4057F853106C2C1B80CAACACC5D761EBA0DBB3C2A5B64B1CC4DFB718FB89B76`

## 原生依赖闭包修复

首次部署的单文件在真实数据库启动时暴露 `SQLitePCLRaw` 原生库无法加载。发布工程现仅在 `PublishSingleFile=true` 时启用 `IncludeNativeLibrariesForSelfExtract`，把原生依赖纳入单文件自解压闭包。重新发布后，候选在目标服完成真实 SQLite 数据库打开、完整保存周期、优雅退出和冷启动复验。

## 发布物烟测

命令：`Server.exe --headless-variable-smoke`

- 退出码：0
- 结果前缀：`VARIABLE_SMOKE_OK`
- 通过项：整数、小数、重置、运行期作用域、私有/服务器持久化、服务器重启清理、每日重置、自定义持久作用域、复合表达式、公式、概率、初始化、旧 A 变量适配、跨对象访问、兼容预检和冲突拒绝。

## 真实语料只读预检

命令：`Server.exe --variable-preflight D:\ChuanQi\Crystal_monogame\Server-mono\Envir`

- 退出码：0
- 扫描文件：2,348
- 语料摘要：`7E1E13532F37151BBC15E4B7B43383A8C1D95E3B0DC25944A2F19864D74ABE3D`
- 结果：无阻断错误；保留换行格式与旧 A 变量语义复核警告，未对真实服务器文件执行写入。

该验证证明最终单文件发布物能够在没有旁置托管 DLL 的情况下，为 Roslyn 动态脚本编译建立完整的内嵌程序集引用闭包，并能在目标服加载所需原生数据库依赖。
