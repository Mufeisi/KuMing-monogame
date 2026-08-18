# SEC-05 受保护秘密与启动门禁

服务端正式启动默认启用安全门禁。秘密由 Windows DPAPI 以当前服务账号范围保护，密文位于 `Configs/ProtectedSecrets/*.dpapi`；复制密文到其他机器或改用其他服务账号不能直接解密，迁移时必须在目标身份下重新导入。

## 受保护项与一次性导入

| 受保护项 | 一次性进程环境变量 | 启用条件 |
|---|---|---|
| `tls-certificate-password` | `LYOCRYSTAL_IMPORT_TLS_CERT_PASSWORD` | `TlsEnabled=true` |
| `administrator-token` | `LYOCRYSTAL_IMPORT_ADMIN_TOKEN` | `StartHTTPService=true`；至少 32 字符 |
| `operator-token` | `LYOCRYSTAL_IMPORT_OPERATOR_TOKEN` | 可选；配置时至少 32 字符且不得与管理员相同 |
| `mysql-connection-string` | `LYOCRYSTAL_IMPORT_MYSQL_CONNECTION_STRING` | MySQL 持久化 |
| `micro-code` | `LYOCRYSTAL_IMPORT_MICRO_CODE` | `MicroServerActive=true` |
| `ai-api-key` | `LYOCRYSTAL_IMPORT_AI_API_KEY` | `AiScriptsEnabled=true` |

导入值只应由 CI/服务管理器注入到本次服务进程，不得作为机器级或用户级环境变量常驻。服务进程先用 DPAPI 写入密文，再立即清除自己的环境副本；CI 步骤或服务管理器还必须确保其父进程/步骤作用域在启动后结束。导入变量不写入命令行参数、INI、日志或仓库。

旧 `LYOCRYSTAL_TLS_CERT_PASSWORD`、`LYOCRYSTAL_ADMIN_TOKEN`、`LYOCRYSTAL_OPERATOR_TOKEN`、`OPENAI_API_KEY` 不再读取；检测到时会清除服务进程副本并阻止本次启动，要求改用一次性导入变量后重启。

## INI 清理

`Settings.Load` 在读取普通配置前删除历史 `GMPassword`、`MicroCode`、`AiScriptsApiKey` 与 `MySqlConnectionString` 键；删除写盘失败会阻止继续。`Settings.Save` 对这些键只写空值。服务端配置界面不再显示或接收微端 Code。

## 启动失败关闭

正式启动在创建服务工作线程前执行门禁，以下任一条件都会抛出明确错误并保持 `Running=false`：

- 启用 TLS 但缺少受保护证书密码；
- 管理监听是公网/通配地址，或非回环地址使用明文 HTTP；
- 管理可信来源不是明确回环/内网 IP；
- 管理员令牌缺失/过短，或两个角色令牌相同；
- 启用 MySQL、微端、AI 脚本但对应受保护秘密缺失。

测试代码可通过内部 `EnvirStartOptions.EnforceProductionSecurity=false` 隔离外部秘密依赖；正式宿主始终使用默认 `true`，配置文件不能关闭该门禁。

## 游戏内管理员授权

游戏内管理员身份不再通过聊天框口令获取。账号表 `accounts.admin_account`（权限等级：`0`=普通玩家，`>0`=管理员）是唯一授予来源：登录时账号权限等级 `>0` 即授予 `IsGM`。聊天命令 `LOGIN` 口令链路已移除，`game-master-password` 不再是受保护秘密，也不进入启动门禁。授予/调整权限在服务端「角色管理」（AccountInfoForm）勾选「管理员」完成，对应写入 `admin_account=1`。

## 备份与恢复

DPAPI 密文不等于跨机备份。应在受保护的企业密钥库中保留源秘密与轮换记录；灾难恢复时在目标服务账号下通过一次性导入重新生成密文。轮换采用“注入新值并重启 → 验证 → 在企业密钥库归档旧版本”的顺序，不在仓库保存密文或明文副本。
