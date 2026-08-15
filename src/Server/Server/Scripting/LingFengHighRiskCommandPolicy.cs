using System;
using System.Collections.Generic;

namespace Server.Scripting
{
    public static class LingFengHighRiskCommandPolicy
    {
        public static bool CanOpenBrowser(
            string urlText,
            bool capabilityEnabled,
            string allowedHttpsHosts,
            bool killSwitchEnabled,
            out Uri uri,
            out string diagnostic)
        {
            uri = null;
            diagnostic = string.Empty;
            if (!capabilityEnabled)
            {
                diagnostic = "TXT 高风险能力默认关闭。";
                return false;
            }
            if (!killSwitchEnabled)
            {
                diagnostic = "高风险操作 Kill Switch 已关闭。";
                return false;
            }
            if (string.IsNullOrWhiteSpace(urlText) || urlText.Length > 2048 ||
                !Uri.TryCreate(urlText, UriKind.Absolute, out Uri candidate))
            {
                diagnostic = "URL 为空、过长或不是绝对地址。";
                return false;
            }
            if (!candidate.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
                !string.IsNullOrEmpty(candidate.UserInfo) ||
                (!candidate.IsDefaultPort && candidate.Port != 443))
            {
                diagnostic = "只允许无用户信息的 HTTPS 443 地址。";
                return false;
            }

            var hosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string value in (allowedHttpsHosts ?? string.Empty).Split(
                         new[] { ',', ';', ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries))
                hosts.Add(value.Trim().TrimEnd('.'));
            string host = candidate.DnsSafeHost.TrimEnd('.');
            if (hosts.Count == 0 || !hosts.Contains(host))
            {
                diagnostic = $"HTTPS 主机不在精确白名单中：{host}。";
                return false;
            }

            uri = candidate;
            return true;
        }
    }
}
