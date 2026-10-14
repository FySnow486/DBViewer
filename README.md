# DBViewer

使用 C# / ASP.NET Core 8 开发的跨平台数据库结构查看工具，提供简洁明亮的 Web 界面。默认从示例数据库开始，无需配置数据库即可体验。

## 启动

安装 .NET 8 SDK，在项目目录运行：

```sh
dotnet restore
dotnet run
```

浏览器访问 http://localhost:5080 。支持 Windows、macOS 和 Linux。前端为原生 HTML/CSS/JavaScript，不需要 Node.js 或前端构建。字体优先使用 DM Sans、JetBrains Mono，离线时自动使用系统字体。

## 功能

- PostgreSQL、MySQL（及兼容协议的 MariaDB）、SQL Server、SQLite 连接配置。
- 表格展示字段名称、数据类型、长度/精度、可空性、主键、默认值和注释。
- 自动读取外键，按关联分组排列可拖动、缩放的 ER 图；无关联表采用网格排布。卡片显示字段名、类型与长度/精度、换行字段注释和两行表注释预览（悬停可看完整表注释）。
- 从表格切换至 ER 图时自动居中并高亮当前表；ER 模式下选择左侧表也会定位，高亮关联线。ER 模式使用窗口剩余空间，上方工具栏持续可用。按住鼠标中键拖动画布，左键拖动标题移动单表；Ctrl + 滚轮以鼠标位置为中心缩放，按钮缩放保持视野中心，可点击「定位当前表」返回；双击卡片查看表格字段。
- 表名/表注释搜索、字段搜索、结构刷新、JSON 快照导出及 Excel 结构导出。Excel 每张表一个 sheet，包含表注释、所有字段属性及外键目标；sheet 名遵守 Excel 的 31 字符和唯一性限制。
- 连接成功后将配置加密保存到本机，刷新页面和重启服务后自动恢复上次连接。连接管理器中可选择、编辑或删除已保存连接。
- 只查询系统元数据，不读取业务表内容。SQLite 强制只读打开已有数据库文件。

## 连接示例

在「新建连接」中选择类型，填写连接名称与连接字符串。文件路径和网络地址均相对于 **ASP.NET Core 服务器**。

| 数据库 | 示例 |
| --- | --- |
| PostgreSQL | `Host=localhost;Port=5432;Database=mydb;Username=postgres;Password=YOUR_PASSWORD;Timeout=10` |
| MySQL | `Server=localhost;Port=3306;Database=mydb;User ID=root;Password=YOUR_PASSWORD;Connection Timeout=10` |
| SQL Server | `Server=localhost,1433;Database=mydb;User ID=sa;Password=YOUR_PASSWORD;Encrypt=True;Connect Timeout=10` |
| SQLite | `Data Source=/absolute/path/database.db` |

生产数据库推荐元数据读取账号。SQL Server 需要受信任的服务器证书。连接参数参考 [Npgsql](https://www.npgsql.org/doc/connection-string-parameters.html)、[MySqlConnector](https://mysqlconnector.net/connection-options/)、[SqlClient](https://learn.microsoft.com/en-us/dotnet/api/microsoft.data.sqlclient.sqlconnection.connectionstring)、[SQLite](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/connection-strings)。

### 元数据范围

- PostgreSQL：读取非系统 schema 中的普通表和分区表，使用 `pg_catalog` 注释及约束。
- MySQL：读取当前数据库的基本表、注释及外键。
- SQL Server：读取当前数据库的用户表；表和字段注释使用 `MS_Description` 扩展属性。
- SQLite：读取 main 数据库的用户表；SQLite 没有标准表/字段注释，注释为空。关系来自声明的外键，不会按字段名称推断。
- ER 图显示外键方向；复合外键逐字段展示，不推断一对一基数。当前不包含视图、索引详情或数据编辑。

## 验证

```sh
dotnet build
node --check wwwroot/app.js
# 先启动应用，再在另一个终端运行（Python 3）：
python3 tests/smoke.py
```

集成检查覆盖演示接口、SQLite 类型/长度/默认值、复合主键、显式/隐式复合外键、带引号的表名、只读打开及错误处理。SQL Server 协议读取已在本机 Azure SQL Edge 15.0.2000.1574 上通过 22 项真实数据库/API 集成检查，详见 [SQL Server 测试记录](tests/SqlServerIntegration/README.md)。完整 SQL Server 各版本及其他数据库仍需配合目标实例验证。

## 发布

```sh
dotnet publish -c Release -o ./publish
dotnet ./publish/DBViewer.dll --urls http://localhost:5080
```

默认启动配置监听所有网卡。此版本面向可信局域网内的单用户使用，未实现登录授权。共享部署前应增加身份认证、数据库访问范围限制和 HTTPS。连接字符串通过同源请求发送至后端；错误响应不回显密码或数据库内部异常。

## 目录

- `Program.cs`：Web 服务、同源检查、API 与超时处理。
- `Models/Schema.cs`：统一结构模型。
- `Services/SchemaReader.cs`：四种数据库的元数据适配器。
- `Services/DemoSchema.cs`：电商示例结构。
- `wwwroot/`：响应式界面与 ER 图。
- `tests/smoke.py`：真实 SQLite 端到端接口检查。

## 本机连接持久化

连接使用 [ASP.NET Core Data Protection](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/introduction) 加密后保存在 `Data/connections.json`，密钥保存在 `Data/keys/`。Unix 系统将 Data 目录权限设为 700，配置文件设为 600。密钥文件由本机目录权限保护；拥有该目录和密钥的本机账号仍可解密连接。默认配置适用于本机单用户使用。

`Data/` 已从 Git 和发布输出中排除。迁移保存的连接时需一同迁移配置和密钥，并限制访问。可通过 `DBVIEWER_DATA_DIR` 环境变量指定持久化目录。请勿仅删除密钥，否则已有连接无法解密。

独立持久化测试（虚构凭据，临时目录及端口 5083）：

```sh
python3 tests/connections.py
```

验证加密落盘、列表不包含密码、服务重启恢复、编辑不重复、激活、删除、输入验证及跨来源请求拦截。

多连接工作空间：左侧显示所有本机保存的连接，点击即可切换数据库，齿轮可编辑已有连接。新建连接会独立保存，不会覆盖其他配置；连接字符串在本机 Data 目录加密存储。每个连接在当前页面分别保留已加载结构、选中表、筛选条件、ER 布局与缩放，刷新结构可获取最新数据库定义。重新打开恢复上次使用的连接。

关键字筛选：在视图上方或左侧输入关键字，匹配 Schema、表名、表注释、字段名、字段类型、长度和字段注释（忽略大小写）。多个关键字用空格分隔，所有关键字都匹配的表才会显示。表格和 ER 图同步筛选，ER 图只显示匹配表之间的外键；清除筛选恢复全部表。

ER 默认横向展开：关联分层从左到右，每层最多三张表；独立分组限制纵向高度后继续向右排列。点击「横向重排」可重新自动布局当前筛选结果。

局域网访问：默认 `dotnet run` 监听 `http://0.0.0.0:5080`。同一局域网设备使用 `http://本机局域网IP:5080/` 访问；本机仍可访问 `http://127.0.0.1:5080/`。当前版本无登录认证，同一网络可访问此端口的设备可以查看结构及管理已保存连接，请在可信局域网使用。

「所有表」视图以表格展示当前数据库的 Schema、表名称、字段数和完整表注释。沿用结构关键字筛选，点击表名称打开字段详情，切换数据库自动更新清单。

ER 支持业务关联：根据字段注释中明确的 `→ 表名.字段`、`关联/引用/对应 表名` 推断连线。未指定目标字段时，使用唯一主键，或无主键表的 id 字段；多字段主键需明确指定目标字段。业务关联显示为虚线，悬停可看原始注释；数据库外键仍为实线，工具栏可关闭业务关联。不存在或无法唯一确定的表/字段不会猜测，底部显示未解析数量及悬停详情。

Windows 离线发布：执行 `bash scripts/publish-windows.sh`，或 Windows PowerShell 执行 `scripts/publish-windows.ps1`。发布目录在 `artifacts/windows-x64`，包含自带 .NET 运行环境的 `DBViewer.exe` 与 `wwwroot` 等资源；请复制或解压整个目录，不能只复制 EXE。双击 EXE 后自动打开浏览器，并弹出“服务在线”提示；关闭提示框不停止服务，关闭控制台停止服务。服务默认端口为 5080，地址由同目录 `appsettings.json` 配置；`--no-startup-dialog` 可禁用自动打开浏览器和启动提示。首次运行需要目录可写（保存本机连接）。页面右上角“服务在线/离线”每 15 秒检查一次，点击可打开状态提示框。

离线快照：连接数据库后点击“导出快照”，得到 `.dbviewer.json` 文件，包括全部表结构、完整注释、声明外键及推断业务关联，不含数据库账号、密码或业务记录。复制此文件和程序发布包到另一台设备，启动程序，点击右上角“导入快照”。导入后无需原数据库即可查看表清单、字段、筛选和 ER 图；快照保存在当前浏览器的 IndexedDB 中，重新打开同一地址自动恢复。换浏览器或换访问地址需重新导入；快照不是数据库备份，也不会创建或修改数据库表。兼容旧版导出的 schema JSON，单文件上限 20 MB。前端不依赖外部字体或网络资源。

业务关联额外支持同 Schema、同模块的 `*_id` 字段命名：依次匹配最近的模块表名，目标为单字段主键或无主键表的 id，要求字段类型一致；明确注释引用和实际外键优先。例如示例模块中 `DEMO_QC_ITEM.main_id → DEMO_QC_MAIN.id`、`DEMO_QC_MAIN.template_id → DEMO_QC_TEMPLATE.id`、`DEMO_QC_TEMPLATE_ITEM.template_id → DEMO_QC_TEMPLATE.id`。命名推断仍使用虚线，并提供推断依据。
