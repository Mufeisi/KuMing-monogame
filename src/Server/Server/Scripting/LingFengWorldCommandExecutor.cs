using System;
using System.Globalization;

namespace Server.Scripting
{
    public readonly record struct LingFengTeleportPlan(string MapName, int X, int Y, bool Random);

    public readonly record struct LingFengPetPlan(string MonsterName, byte Count, byte Level);

    public static class LingFengWorldCommandExecutor
    {
        public static bool TryPlanTeleport(
            string mapName,
            string xText,
            string yText,
            out LingFengTeleportPlan plan,
            out string diagnostic)
        {
            plan = default;
            diagnostic = string.Empty;
            if (string.IsNullOrWhiteSpace(mapName))
            {
                diagnostic = "传送地图不能为空。";
                return false;
            }

            if (!int.TryParse(xText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int x) ||
                !int.TryParse(yText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int y) ||
                x < 0 || y < 0)
            {
                diagnostic = $"传送坐标无效：{xText},{yText}。";
                return false;
            }

            if ((x == 0) != (y == 0))
            {
                diagnostic = "随机传送必须同时使用坐标 0,0。";
                return false;
            }

            plan = new LingFengTeleportPlan(mapName, x, y, x == 0);
            return true;
        }

        public static bool TryPlanPet(
            string monsterName,
            string countText,
            string levelText,
            out LingFengPetPlan plan,
            out string diagnostic)
        {
            plan = default;
            diagnostic = string.Empty;
            if (string.IsNullOrWhiteSpace(monsterName))
            {
                diagnostic = "宝宝怪物名称不能为空。";
                return false;
            }

            if (!byte.TryParse(countText, NumberStyles.Integer, CultureInfo.InvariantCulture, out byte count) ||
                count is < 1 or > 5)
            {
                diagnostic = $"宝宝数量无效：{countText}，允许范围为 1..5。";
                return false;
            }

            if (!byte.TryParse(levelText, NumberStyles.Integer, CultureInfo.InvariantCulture, out byte level) ||
                level > 7)
            {
                diagnostic = $"宝宝等级无效：{levelText}，允许范围为 0..7。";
                return false;
            }

            plan = new LingFengPetPlan(monsterName, count, level);
            return true;
        }
    }
}
