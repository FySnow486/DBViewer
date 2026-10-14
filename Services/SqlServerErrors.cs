using Microsoft.Data.SqlClient;
namespace DBViewer.Services;

public static class SqlServerErrors
{
    // Return actionable categories, never the original error text or credentials.
    public static string Describe(SqlException exception)
    {
        if (exception.Number == -2146893019 || HasCertificateError(exception))
            return "SQL Server 证书验证失败。若连接的是你信任的本机自签名证书实例，可在连接字符串末尾添加 TrustServerCertificate=True; 并保留 Encrypt=True。远程服务器请配置受信任证书。";
        return exception.Number switch {
            18456 => "SQL Server 登录失败。请检查 User ID 和 Password，并确认服务器已启用 SQL Server 身份验证。",
            4060 or 916 => "无法打开指定数据库。请检查 Database 是否为已存在的数据库名称，并确认当前账号有访问权限。",
            -2 => "SQL Server 连接或查询超时。请检查服务器地址、端口和容器运行状态。",
            53 or 26 or 40 or 10061 or 11001 => "无法访问 SQL Server。请检查 Server 地址、端口和数据库服务状态。",
            229 or 297 => "SQL Server 权限不足。请确认当前账号具有目标数据库的元数据读取权限。",
            _ => "SQL Server 连接或结构读取失败。请检查地址、数据库名称、登录信息和元数据读取权限。"
        };
    }

    static bool HasCertificateError(Exception exception)
    {
        for (Exception? current = exception; current != null; current = current.InnerException)
            if (current.Message.Contains("certificate", StringComparison.OrdinalIgnoreCase) || current.Message.Contains("证书", StringComparison.Ordinal)) return true;
        return false;
    }
}
