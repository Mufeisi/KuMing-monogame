using System;
using System.Collections.Generic;
using System.Drawing;

namespace Server.Scripting
{
    /// <summary>
    /// C# 脚本侧声明“坐标触发点”（对应 legacy DefaultNPC 的 [@_MAPCOORD(...)] 扫描行为）。
    /// 用途：
    /// - PlayerObject.CheckMovement 仅在地图 ActiveCoords 中命中时才会触发 DefaultNPCType.MapCoord
    /// - 在逐步迁移/跳过加载 txt 的过程中，需要由 C# 脚本提供这些坐标清单
    /// </summary>
    public sealed class ActiveMapCoordRegistry
    {
        private readonly Dictionary<string, HashSet<Point>> _byMap =
            new Dictionary<string, HashSet<Point>>(StringComparer.OrdinalIgnoreCase);

        public int MapCount => _byMap.Count;

        public int CoordCount
        {
            get
            {
                var total = 0;
                foreach (var set in _byMap.Values)
                    total += set?.Count ?? 0;
                return total;
            }
        }

        public IReadOnlyDictionary<string, HashSet<Point>> ByMap => _byMap;

        public void Register(string mapFileName, int x, int y)
        {
            if (string.IsNullOrWhiteSpace(mapFileName))
                throw new ArgumentException("mapFileName 不能为空。", nameof(mapFileName));

            var point = new Point(x, y);

            if (!_byMap.TryGetValue(mapFileName, out var set))
            {
                set = new HashSet<Point>();
                _byMap.Add(mapFileName, set);
            }

            set.Add(point);
        }
    }
}
