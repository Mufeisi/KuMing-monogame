namespace Server.Authoring;

public enum SkillTargetCondition
{
    Unknown,
    HostileObject,
    HostileObjectWithFlightPath,
    MapLocation,
    SelfDirection
}

public enum SkillCenterKind
{
    Unknown,
    Target,
    SelectedLocation,
    Caster
}

public enum SkillGridPointRole
{
    Center,
    Primary,
    Additional
}

public sealed record SkillGridPoint(int X, int Y, SkillGridPointRole Role);

public sealed record SkillSpatialProfile(
    Spell Spell,
    byte SkillLevel,
    bool IsModeled,
    SkillTargetCondition TargetCondition,
    SkillCenterKind CenterKind,
    string Orientation,
    IReadOnlyList<SkillGridPoint> Points,
    string BehaviorEvidence,
    string Explanation)
{
    public string RenderGrid()
    {
        if (!IsModeled || Points.Count == 0)
            return "（未建模：不根据 MagicInfo.Range 猜测作用形状）";

        int minX = Math.Min(-1, Points.Min(point => point.X));
        int maxX = Math.Max(1, Points.Max(point => point.X));
        int minY = Math.Min(-1, Points.Min(point => point.Y));
        int maxY = Math.Max(1, Points.Max(point => point.Y));
        var lines = new List<string>();

        for (int y = minY; y <= maxY; y++)
        {
            var line = new char[maxX - minX + 1];
            Array.Fill(line, '·');
            foreach (SkillGridPoint point in Points.Where(point => point.Y == y))
                line[point.X - minX] = point.Role switch
                {
                    SkillGridPointRole.Center => '中',
                    SkillGridPointRole.Primary => '主',
                    SkillGridPointRole.Additional => '附',
                    _ => '?'
                };
            lines.Add(new string(line));
        }

        return string.Join(Environment.NewLine, lines);
    }
}

public static class SkillSpatialInspector
{
    public static SkillSpatialProfile Build(Spell spell, byte skillLevel)
    {
        return spell switch
        {
            Spell.FireBall or Spell.GreatFireBall or Spell.FrostCrunch =>
                SingleHostileTarget(spell, skillLevel, requiresFlightPath: true),
            Spell.ThunderBolt =>
                SingleHostileTarget(spell, skillLevel),
            Spell.FireBang or Spell.IceStorm => LocationSquare(spell, skillLevel),
            Spell.HellFire => DirectionalHellFire(skillLevel),
            _ => Unknown(spell, skillLevel)
        };
    }

    private static SkillSpatialProfile SingleHostileTarget(Spell spell, byte skillLevel, bool requiresFlightPath = false)
    {
        return new SkillSpatialProfile(
            spell,
            skillLevel,
            true,
            requiresFlightPath ? SkillTargetCondition.HostileObjectWithFlightPath : SkillTargetCondition.HostileObject,
            SkillCenterKind.Target,
            "目标所在格",
            Array.AsReadOnly(new[] { new SkillGridPoint(0, 0, SkillGridPointRole.Center) }),
            requiresFlightPath
                ? "HumanObject.Fireball：服务端复核 IsAttackTarget 与 CanFly 后建立延迟命中。"
                : "HumanObject.ThunderBolt：服务端复核 IsAttackTarget 后建立延迟命中。",
            requiresFlightPath
                ? "单一敌对对象且需要可飞行路径；网格只表示命中格。"
                : "单一敌对对象；网格只表示命中格，不表示客户端选择权威。");
    }

    private static SkillSpatialProfile LocationSquare(Spell spell, byte skillLevel)
    {
        var points = new List<SkillGridPoint>();
        for (int y = -1; y <= 1; y++)
        for (int x = -1; x <= 1; x++)
            points.Add(new SkillGridPoint(x, y, x == 0 && y == 0 ? SkillGridPointRole.Center : SkillGridPointRole.Primary));

        return new SkillSpatialProfile(
            spell,
            skillLevel,
            true,
            SkillTargetCondition.MapLocation,
            SkillCenterKind.SelectedLocation,
            "地图坐标为中心",
            points.AsReadOnly(),
            "Map.CompleteMagic：FireBang/IceStorm 遍历中心坐标前后各一格的 3×3 区域。",
            "中心格及周围八格；每个对象仍由服务端 IsAttackTarget 过滤。");
    }

    private static SkillSpatialProfile DirectionalHellFire(byte skillLevel)
    {
        var points = new List<SkillGridPoint> { new(0, 0, SkillGridPointRole.Center) };
        for (int distance = 1; distance <= 4; distance++)
            points.Add(new SkillGridPoint(0, -distance, SkillGridPointRole.Primary));

        if (skillLevel == 3)
        {
            for (int distance = 1; distance <= 4; distance++)
            {
                points.Add(new SkillGridPoint(-distance, -distance, SkillGridPointRole.Additional));
                points.Add(new SkillGridPoint(distance, -distance, SkillGridPointRole.Additional));
            }
        }

        return new SkillSpatialProfile(
            Spell.HellFire,
            skillLevel,
            true,
            SkillTargetCondition.SelfDirection,
            SkillCenterKind.Caster,
            "施法者朝上；实际按角色 Direction 旋转",
            points.AsReadOnly(),
            "HumanObject.HellFire 与 Map.CompleteMagic：主方向递进 4 格；3 级追加左右相邻方向各 4 格。",
            skillLevel == 3 ? "主方向四格，另有两条附加方向线。" : "主方向四格；只有 3 级追加两条侧向线。");
    }

    private static SkillSpatialProfile Unknown(Spell spell, byte skillLevel)
    {
        return new SkillSpatialProfile(
            spell,
            skillLevel,
            false,
            SkillTargetCondition.Unknown,
            SkillCenterKind.Unknown,
            "未知",
            Array.Empty<SkillGridPoint>(),
            "尚未建立经过服务端代码核对的空间档案。",
            "未知行为保持未建模；MagicInfo.Range 只展示配置距离，不推断作用形状。");
    }
}
