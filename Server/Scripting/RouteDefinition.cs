using System.Drawing;
using Server.MirDatabase;

namespace Server.Scripting
{
    /// <summary>
    /// C# 版本路线定义（Key = Routes/&lt;FileName&gt;），用于替代 Envir/Routes/*.txt。
    /// </summary>
    public sealed class RouteDefinition
    {
        public string Key { get; }

        private readonly List<RouteInfo> _route = new List<RouteInfo>();

        public IReadOnlyList<RouteInfo> Route => _route;

        public RouteDefinition(string key)
        {
            Key = LogicKey.NormalizeOrThrow(key);
        }

        public RouteDefinition Add(Point location, int delay = 0)
        {
            _route.Add(new RouteInfo
            {
                Location = location,
                Delay = delay
            });

            return this;
        }

        public RouteDefinition Add(int x, int y, int delay = 0)
        {
            return Add(new Point(x, y), delay);
        }
    }
}

