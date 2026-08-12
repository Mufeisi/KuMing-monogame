namespace Server.Persistence.Sql
{
    public sealed class SqlSessionOptions
    {
        /// <summary>
        /// 打开连接的最大尝试次数（包含首次尝试）。
        /// </summary>
        public int ConnectMaxRetries { get; set; } = 1;

        /// <summary>
        /// 单条命令执行的最大尝试次数（包含首次尝试）。
        /// </summary>
        public int CommandMaxRetries { get; set; } = 1;

        /// <summary>
        /// 重试基础延迟（毫秒）。实际延迟为 min(5000, BaseRetryDelayMs * attempt)。
        /// </summary>
        public int BaseRetryDelayMs { get; set; } = 200;
    }
}

