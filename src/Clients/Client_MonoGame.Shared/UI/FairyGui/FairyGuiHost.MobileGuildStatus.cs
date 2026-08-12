using System;
using S = ServerPackets;

namespace MonoShare
{
    internal static partial class FairyGuiHost
    {
        private static string _mobileGuildStatusGuildName = string.Empty;
        private static string _mobileGuildStatusRankName = string.Empty;
        private static byte _mobileGuildStatusLevel;
        private static long _mobileGuildStatusExperience;
        private static long _mobileGuildStatusMaxExperience;
        private static uint _mobileGuildStatusGold;
        private static byte _mobileGuildStatusSparePoints;
        private static int _mobileGuildStatusMemberCount;
        private static int _mobileGuildStatusMaxMembers;
        private static bool _mobileGuildStatusVoting;
        private static byte _mobileGuildStatusItemCount;
        private static byte _mobileGuildStatusBuffCount;
        private static GuildRankOptions _mobileGuildStatusMyOptions;

        public static void UpdateMobileGuildStatus(S.GuildStatus status)
        {
            try
            {
                _mobileGuildStatusGuildName = status?.GuildName ?? string.Empty;
                _mobileGuildStatusRankName = status?.GuildRankName ?? string.Empty;
                _mobileGuildStatusLevel = status?.Level ?? 0;
                _mobileGuildStatusExperience = status?.Experience ?? 0;
                _mobileGuildStatusMaxExperience = status?.MaxExperience ?? 0;
                _mobileGuildStatusGold = status?.Gold ?? 0;
                _mobileGuildStatusSparePoints = status?.SparePoints ?? 0;
                _mobileGuildStatusMemberCount = status?.MemberCount ?? 0;
                _mobileGuildStatusMaxMembers = status?.MaxMembers ?? 0;
                _mobileGuildStatusVoting = status?.Voting ?? false;
                _mobileGuildStatusItemCount = status?.ItemCount ?? 0;
                _mobileGuildStatusBuffCount = status?.BuffCount ?? 0;
                _mobileGuildStatusMyOptions = status?.MyOptions ?? 0;
            }
            catch
            {
                _mobileGuildStatusGuildName = string.Empty;
                _mobileGuildStatusRankName = string.Empty;
                _mobileGuildStatusLevel = 0;
                _mobileGuildStatusExperience = 0;
                _mobileGuildStatusMaxExperience = 0;
                _mobileGuildStatusGold = 0;
                _mobileGuildStatusSparePoints = 0;
                _mobileGuildStatusMemberCount = 0;
                _mobileGuildStatusMaxMembers = 0;
                _mobileGuildStatusVoting = false;
                _mobileGuildStatusItemCount = 0;
                _mobileGuildStatusBuffCount = 0;
                _mobileGuildStatusMyOptions = 0;
            }

            MarkMobileGuildDirty();
        }

        private static string FormatMobileGuildOptions(GuildRankOptions options)
        {
            if (options == 0)
                return "无";

            string text = string.Empty;

            void Add(string label)
            {
                if (string.IsNullOrWhiteSpace(label))
                    return;

                text = string.IsNullOrWhiteSpace(text) ? label : (text + "/" + label);
            }

            if ((options & GuildRankOptions.CanRecruit) != 0) Add("招募");
            if ((options & GuildRankOptions.CanKick) != 0) Add("踢人");
            if ((options & GuildRankOptions.CanChangeNotice) != 0) Add("公告");
            if ((options & GuildRankOptions.CanChangeRank) != 0) Add("改职");
            if ((options & GuildRankOptions.CanStoreItem) != 0) Add("存物");
            if ((options & GuildRankOptions.CanRetrieveItem) != 0) Add("取物");
            if ((options & GuildRankOptions.CanActivateBuff) != 0) Add("Buff");
            if ((options & GuildRankOptions.CanAlterAlliance) != 0) Add("联盟");

            return string.IsNullOrWhiteSpace(text) ? "无" : text;
        }
    }
}
