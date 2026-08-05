using System;
using System.Drawing;
using Server.MirEnvir;
using Server.MirObjects;

namespace Server.Scripting
{
    public enum PlayerMapTransitionReason : byte
    {
        Unknown = 0,
        StartGame = 1,
        Teleport = 2,
        MapMovement = 3,
        TownRevive = 4,
    }

    public sealed class PlayerMapLeaveRequest
    {
        public PlayerMapLeaveRequest(
            PlayerObject player,
            Map fromMap,
            Point fromLocation,
            Map toMap,
            Point toLocation,
            PlayerMapTransitionReason reason)
        {
            Player = player ?? throw new ArgumentNullException(nameof(player));
            FromMap = fromMap ?? throw new ArgumentNullException(nameof(fromMap));
            FromLocation = fromLocation;
            ToMap = toMap ?? throw new ArgumentNullException(nameof(toMap));
            ToLocation = toLocation;
            Reason = reason;
        }

        public PlayerObject Player { get; }

        public Map FromMap { get; }

        public Point FromLocation { get; }

        public Map ToMap { get; set; }

        public Point ToLocation { get; set; }

        public PlayerMapTransitionReason Reason { get; }

        /// <summary>
        /// - Continue：继续引擎默认换图
        /// - Cancel：阻止本次换图（可配合 <see cref="FailMessage"/> 提示原因）
        /// </summary>
        public ScriptHookDecision Decision { get; set; } = ScriptHookDecision.Continue;

        /// <summary>
        /// 当 <see cref="Decision"/> = Cancel 时，可用于提示玩家原因（例如“该地图暂未开放”）。
        /// </summary>
        public string FailMessage { get; set; } = string.Empty;
    }

    public sealed class PlayerMapEnterResult
    {
        public PlayerMapEnterResult(
            PlayerObject player,
            Map fromMap,
            Point fromLocation,
            Map toMap,
            Point toLocation,
            PlayerMapTransitionReason reason,
            bool redirected,
            ScriptHookDecision leaveDecision)
        {
            Player = player ?? throw new ArgumentNullException(nameof(player));
            FromMap = fromMap;
            FromLocation = fromLocation;
            ToMap = toMap ?? throw new ArgumentNullException(nameof(toMap));
            ToLocation = toLocation;
            Reason = reason;
            Redirected = redirected;
            LeaveDecision = leaveDecision;
        }

        public PlayerObject Player { get; }

        public Map FromMap { get; }

        public Point FromLocation { get; }

        public Map ToMap { get; }

        public Point ToLocation { get; }

        public PlayerMapTransitionReason Reason { get; }

        /// <summary>
        /// 是否发生脚本重定向（Leave Before 将目的地改为其它地图/坐标）。
        /// </summary>
        public bool Redirected { get; }

        public ScriptHookDecision LeaveDecision { get; }
    }
}

