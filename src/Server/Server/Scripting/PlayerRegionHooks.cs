using System;
using System.Collections.Generic;
using System.Drawing;
using Server.MirEnvir;
using Server.MirObjects;

namespace Server.Scripting
{
    public enum PlayerRegionEventType : byte
    {
        Enter = 1,
        Leave = 2,
    }

    public enum PlayerRegionTransitionReason : byte
    {
        Unknown = 0,
        StartGame = 1,
        Walk = 2,
        Run = 3,
        Teleport = 4,
        MapMovement = 5,
        TownRevive = 6,
        Pushed = 7,
    }

    public enum PlayerRegionShapeType : byte
    {
        Rectangle = 1,
        Polygon = 2,
    }

    public interface IPlayerRegionShape
    {
        PlayerRegionShapeType ShapeType { get; }

        Rectangle Bounds { get; }

        bool Contains(Point p);
    }

    internal sealed class PlayerRegionRectShape : IPlayerRegionShape
    {
        public PlayerRegionRectShape(int left, int top, int right, int bottom)
        {
            if (left > right)
                throw new ArgumentException("left 不能大于 right。", nameof(left));
            if (top > bottom)
                throw new ArgumentException("top 不能大于 bottom。", nameof(top));

            Left = left;
            Top = top;
            Right = right;
            Bottom = bottom;
        }

        public int Left { get; }
        public int Top { get; }
        public int Right { get; }
        public int Bottom { get; }

        public PlayerRegionShapeType ShapeType => PlayerRegionShapeType.Rectangle;

        public Rectangle Bounds => Rectangle.FromLTRB(Left, Top, Right + 1, Bottom + 1);

        public bool Contains(Point p) =>
            p.X >= Left && p.X <= Right && p.Y >= Top && p.Y <= Bottom;
    }

    internal sealed class PlayerRegionPolygonShape : IPlayerRegionShape
    {
        private readonly Point[] _points;
        private readonly Rectangle _bounds;

        public PlayerRegionPolygonShape(IReadOnlyList<Point> points)
        {
            if (points == null) throw new ArgumentNullException(nameof(points));
            if (points.Count < 3)
                throw new ArgumentException("多边形至少需要 3 个点。", nameof(points));

            _points = new Point[points.Count];
            for (var i = 0; i < points.Count; i++)
                _points[i] = points[i];

            var minX = _points[0].X;
            var maxX = _points[0].X;
            var minY = _points[0].Y;
            var maxY = _points[0].Y;

            for (var i = 1; i < _points.Length; i++)
            {
                var pt = _points[i];
                if (pt.X < minX) minX = pt.X;
                if (pt.X > maxX) maxX = pt.X;
                if (pt.Y < minY) minY = pt.Y;
                if (pt.Y > maxY) maxY = pt.Y;
            }

            _bounds = Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
        }

        public PlayerRegionShapeType ShapeType => PlayerRegionShapeType.Polygon;

        public Rectangle Bounds => _bounds;

        public bool Contains(Point p)
        {
            if (!_bounds.Contains(p))
                return false;

            // 边界点视为“在区域内”
            for (var i = 0; i < _points.Length; i++)
            {
                var a = _points[i];
                var b = _points[(i + 1) % _points.Length];
                if (IsPointOnSegment(p, a, b))
                    return true;
            }

            // Ray casting
            var inside = false;
            for (var i = 0; i < _points.Length; i++)
            {
                var j = (i + _points.Length - 1) % _points.Length;
                var pi = _points[i];
                var pj = _points[j];

                var intersect = ((pi.Y > p.Y) != (pj.Y > p.Y)) &&
                                (p.X < (double)(pj.X - pi.X) * (p.Y - pi.Y) / (pj.Y - pi.Y) + pi.X);
                if (intersect)
                    inside = !inside;
            }

            return inside;
        }

        private static bool IsPointOnSegment(Point p, Point a, Point b)
        {
            // 叉积为 0 且投影在线段范围内
            var cross = (long)(p.Y - a.Y) * (b.X - a.X) - (long)(p.X - a.X) * (b.Y - a.Y);
            if (cross != 0) return false;

            var minX = Math.Min(a.X, b.X);
            var maxX = Math.Max(a.X, b.X);
            var minY = Math.Min(a.Y, b.Y);
            var maxY = Math.Max(a.Y, b.Y);

            return p.X >= minX && p.X <= maxX && p.Y >= minY && p.Y <= maxY;
        }
    }

    public sealed class PlayerRegionDefinition
    {
        public PlayerRegionDefinition(string mapFileName, string regionKey, IPlayerRegionShape shape)
        {
            if (string.IsNullOrWhiteSpace(mapFileName))
                throw new ArgumentException("mapFileName 不能为空。", nameof(mapFileName));
            if (string.IsNullOrWhiteSpace(regionKey))
                throw new ArgumentException("regionKey 不能为空。", nameof(regionKey));

            Shape = shape ?? throw new ArgumentNullException(nameof(shape));

            MapFileName = LogicKey.NormalizeOrThrow(mapFileName);
            RegionKey = LogicKey.NormalizeOrThrow(regionKey);
            Id = $"{MapFileName}/{RegionKey}";
        }

        public string Id { get; }

        public string MapFileName { get; }

        public string RegionKey { get; }

        public IPlayerRegionShape Shape { get; }

        public PlayerRegionShapeType ShapeType => Shape.ShapeType;

        public Rectangle Bounds => Shape.Bounds;

        public bool Contains(Point p) => Shape.Contains(p);
    }

    public sealed class PlayerRegionRegistry
    {
        private readonly Dictionary<string, PlayerRegionDefinition> _definitions =
            new Dictionary<string, PlayerRegionDefinition>(StringComparer.Ordinal);

        private readonly Dictionary<string, List<PlayerRegionDefinition>> _byMap =
            new Dictionary<string, List<PlayerRegionDefinition>>(StringComparer.Ordinal);

        public int Count => _definitions.Count;

        public IReadOnlyDictionary<string, PlayerRegionDefinition> Definitions => _definitions;

        public void Register(PlayerRegionDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));

            if (_definitions.ContainsKey(definition.Id))
                throw new InvalidOperationException($"重复的区域定义 Key：{definition.Id}");

            _definitions.Add(definition.Id, definition);

            if (!_byMap.TryGetValue(definition.MapFileName, out var list))
            {
                list = new List<PlayerRegionDefinition>();
                _byMap.Add(definition.MapFileName, list);
            }

            list.Add(definition);
        }

        public void RegisterRect(string mapFileName, string regionKey, int left, int top, int right, int bottom)
        {
            Register(new PlayerRegionDefinition(mapFileName, regionKey, new PlayerRegionRectShape(left, top, right, bottom)));
        }

        public void RegisterPolygon(string mapFileName, string regionKey, IReadOnlyList<Point> points)
        {
            Register(new PlayerRegionDefinition(mapFileName, regionKey, new PlayerRegionPolygonShape(points)));
        }

        public bool TryGet(string id, out PlayerRegionDefinition definition)
        {
            definition = null;

            if (!LogicKey.TryNormalize(id, out var normalizedId))
                return false;

            return _definitions.TryGetValue(normalizedId, out definition);
        }

        public IReadOnlyList<PlayerRegionDefinition> GetByMap(string mapFileName)
        {
            if (string.IsNullOrWhiteSpace(mapFileName))
                return Array.Empty<PlayerRegionDefinition>();

            if (!LogicKey.TryNormalize(mapFileName, out var normalizedMap))
                return Array.Empty<PlayerRegionDefinition>();

            return _byMap.TryGetValue(normalizedMap, out var list) ? list : (IReadOnlyList<PlayerRegionDefinition>)Array.Empty<PlayerRegionDefinition>();
        }
    }

    public sealed class PlayerRegionEvent
    {
        public PlayerRegionEvent(
            PlayerObject player,
            Map fromMap,
            Point fromLocation,
            Map toMap,
            Point toLocation,
            PlayerRegionDefinition region,
            PlayerRegionEventType eventType,
            PlayerRegionTransitionReason reason)
        {
            Player = player ?? throw new ArgumentNullException(nameof(player));
            Region = region ?? throw new ArgumentNullException(nameof(region));

            FromMap = fromMap;
            FromLocation = fromLocation;
            ToMap = toMap;
            ToLocation = toLocation;
            EventType = eventType;
            Reason = reason;
        }

        public PlayerObject Player { get; }

        public Map FromMap { get; }

        public Point FromLocation { get; }

        public Map ToMap { get; }

        public Point ToLocation { get; }

        public PlayerRegionDefinition Region { get; }

        public PlayerRegionEventType EventType { get; }

        public PlayerRegionTransitionReason Reason { get; }
    }
}

