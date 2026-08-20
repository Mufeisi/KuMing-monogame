using System;
using System.Collections.Generic;

namespace Client.Bootstrap
{
    internal sealed class PcBootstrapPackageIndexView
    {
        public string GeneratedAtUtc { get; set; }
        public string ResourceVersion { get; set; }
        public List<PcBootstrapPackageIndexPackageView> Packages { get; set; } = new List<PcBootstrapPackageIndexPackageView>();
    }

    internal sealed class PcBootstrapPackageIndexPackageView
    {
        public string Name { get; set; }
        public string Sha256 { get; set; }
        public long Size { get; set; }
    }

    internal sealed class PcBootstrapPreLoginUpdatePlanView
    {
        public bool Skipped { get; set; }
        public bool Failed { get; set; }
        public string RepositoryRoot { get; set; }
        public string ResourceVersion { get; set; }
        public List<string> PackagesToUpdate { get; set; } = new List<string>();
        public string Message { get; set; }

        public static PcBootstrapPreLoginUpdatePlanView Skip(string message)
        {
            return new PcBootstrapPreLoginUpdatePlanView
            {
                Skipped = true,
                Failed = false,
                Message = string.IsNullOrWhiteSpace(message) ? "已跳过资源更新。" : message,
            };
        }

        public static PcBootstrapPreLoginUpdatePlanView Fail(string message)
        {
            return new PcBootstrapPreLoginUpdatePlanView
            {
                Skipped = false,
                Failed = true,
                Message = string.IsNullOrWhiteSpace(message) ? "资源更新失败。" : message,
            };
        }
    }

    internal sealed class PcBootstrapApplyResultView
    {
        public bool Completed { get; set; }
        public bool Skipped { get; set; }
        public bool Failed { get; set; }
        public string Message { get; set; }
        public string ResourceVersion { get; set; }
        public int UpdatedPackageCount { get; set; }
        public List<string> UpdatedPackages { get; set; } = new List<string>();

        public static PcBootstrapApplyResultView Skip(string message)
        {
            return new PcBootstrapApplyResultView
            {
                Completed = false,
                Skipped = true,
                Failed = false,
                Message = string.IsNullOrWhiteSpace(message) ? "已跳过资源更新。" : message,
            };
        }

        public static PcBootstrapApplyResultView Fail(string message)
        {
            return new PcBootstrapApplyResultView
            {
                Completed = false,
                Skipped = false,
                Failed = true,
                Message = string.IsNullOrWhiteSpace(message) ? "资源更新失败。" : message,
            };
        }
    }

    internal sealed class PcBootstrapProgress
    {
        public DateTime AtLocal { get; set; } = DateTime.Now;
        public string Stage { get; set; }
        public string PackageName { get; set; }
        public long ReceivedBytes { get; set; }
        public long TotalBytes { get; set; }
        public string Message { get; set; }
    }
}

