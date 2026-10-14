# SQL Server 集成测试

验证日期：2026-10-12（Asia/Shanghai）。

验证环境：本机 SQL Server 兼容实例（Azure SQL Edge 15.0.2000.1574）。运行测试时可通过环境变量指定自己的实例。

## 结果

修复前：19 项检查中 16 项通过、3 项失败。

失败并修复的项目：

- 自定义 `nvarchar` 别名类型保留类型名，但未显示底层字符长度。
- 自定义 `decimal` 别名类型未显示精度和小数位数。
- `datetime2`、`time`、`datetimeoffset` 未显示小数秒精度。

`SchemaReader` 现在使用 `sys.types` 中的底层系统类型计算长度/精度，同时保留别名类型名称；时间类型使用 `sys.columns.scale`。参考 [sys.columns](https://learn.microsoft.com/en-us/sql/relational-databases/system-catalog-views/sys-columns-transact-sql) 和 [sys.types](https://learn.microsoft.com/en-us/sql/relational-databases/system-catalog-views/sys-types-transact-sql)。

修复后：**22 / 22 项通过**；`dotnet build` 为 0 警告、0 错误；SQLite smoke 回归通过。

覆盖范围：数据库名称、同名跨 schema 表、Unicode/非 Unicode 字段长度、max 类型、decimal 精度、时间精度、别名类型、复合主键、可空性、默认约束、中文表/字段注释、特殊标识符、nchar、计算列、复合外键逐字段配对、跨 schema 外键、自关联、HTTP API、错误脱敏。

## 运行

测试会创建随机命名的 `DBViewer_Test_<GUID>` 数据库，仅在其中创建对象；在 finally 中清理该临时数据库，不修改现有业务库。需要具有创建数据库权限的测试账号。不要在生产实例上运行此测试。

```sh
dotnet build tests/SqlServerIntegration/SqlServerIntegration.csproj
# 先启动应用；下面展示从测试容器读取密码的示例：
docker exec sql sh -c 'printf "%s" "${MSSQL_SA_PASSWORD:-$SA_PASSWORD}"' | DBVIEWER_URL=http://127.0.0.1:5080 dotnet tests/SqlServerIntegration/bin/Debug/net8.0/SqlServerIntegration.dll
```

密码只经 stdin 传入，不写入项目文件、不输出。测试连接针对本机容器使用 `Encrypt=True;TrustServerCertificate=True`；该设置仅存在于测试程序，不改变应用连接策略。默认账号为 sa。省略 `DBVIEWER_URL` 时仅测试读取服务，不测试 HTTP API。

环境变量 `DBVIEWER_SQLSERVER`、`DBVIEWER_SQLUSER` 可指定测试实例和账号。若进程被强制终止，finally 可能无法执行，需要人工确认并清理该次创建的随机测试数据库。

此结果验证 Azure SQL Edge 的 SQL Server 协议和相关系统目录，不代表已验证完整 SQL Server 各版本、Windows 集成认证或生产 TLS 配置。

## 只读连接诊断

添加 `--probe` 可诊断本机连接，不创建测试库。使用同样的 stdin 凭据管道运行：

```sh
docker exec sql sh -c 'printf "%s" "${MSSQL_SA_PASSWORD:-$SA_PASSWORD}"' | dotnet tests/SqlServerIntegration/bin/Debug/net8.0/SqlServerIntegration.dll --probe
```

2026-10-12 实测：默认 TLS 验证因本机自签名证书失败（SqlException -2146893019）；仅在本机测试连接中启用 TrustServerCertificate 后，直连及 HTTP API 成功。错误密码、数据库不存在时，API 分别返回登录和数据库访问提示，不回显凭据。应用默认仍验证证书，未自动更改用户连接字符串。
