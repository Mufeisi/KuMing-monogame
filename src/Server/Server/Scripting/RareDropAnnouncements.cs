using System;
using System.IO;
using System.Text;
using Server.MirDatabase;
using Server.MirEnvir;
using Server.MirObjects;

namespace Server.Scripting
{
    internal static class RareDropAnnouncements
    {
        private static readonly object Gate = new object();
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        private static bool _logWriteErrorLogged;

        public static void Notify(UserItem item, DropAttemptContext context)
        {
            if (item?.Info == null) return;
            if (!item.Info.GlobalDropNotify) return;
            if (context == null) return;

            TryBroadcast(item, context);
            TryLog(item, context);
        }

        private static void TryBroadcast(UserItem item, DropAttemptContext context)
        {
            try
            {
                var env = Envir.Main;
                if (env?.Players == null) return;

                var message = BuildBroadcastMessage(item, context);
                if (string.IsNullOrWhiteSpace(message)) return;

                foreach (var player in env.Players)
                {
                    player.ReceiveChat(message, ChatType.System2);
                }
            }
            catch
            {
                // 广播失败不影响主流程
            }
        }

        private static string BuildBroadcastMessage(UserItem item, DropAttemptContext context)
        {
            var source = context.Source ?? string.Empty;
            var playerName = context.Player?.Name ?? string.Empty;
            var monsterName = context.Monster?.Name ?? string.Empty;
            var itemName = item.FriendlyName ?? item.Info.FriendlyName ?? item.Info.Name ?? string.Empty;

            if (string.IsNullOrWhiteSpace(itemName))
                return string.Empty;

            if (string.Equals(source, "monster", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(monsterName))
            {
                return $"{monsterName} 掉落 {itemName}.";
            }

            if (string.Equals(source, "harvest", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(playerName) && !string.IsNullOrWhiteSpace(monsterName))
                    return $"{playerName} 采集 {monsterName} 获得 {itemName}.";
                if (!string.IsNullOrWhiteSpace(playerName))
                    return $"{playerName} 采集获得 {itemName}.";
            }

            if (string.Equals(source, "fishing", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(playerName))
                    return $"{playerName} 钓到 {itemName}.";
            }

            if (string.Equals(source, "npcdrop", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(playerName))
                    return $"{playerName} 获得 {itemName}.";
            }

            if (!string.IsNullOrWhiteSpace(playerName))
                return $"{playerName} 获得 {itemName}.";

            if (!string.IsNullOrWhiteSpace(monsterName))
                return $"{monsterName} 掉落 {itemName}.";

            return $"{itemName}.";
        }

        private static void TryLog(UserItem item, DropAttemptContext context)
        {
            try
            {
                var dir = Path.Combine(".", "Logs", "Drops");
                Directory.CreateDirectory(dir);

                var filePath = Path.Combine(dir, $"rare-drops-{DateTime.Now:yyyyMMdd}.log");

                var mapIndex = 0;
                var x = 0;
                var y = 0;

                if (context.Monster?.CurrentMap?.Info != null)
                {
                    mapIndex = context.Monster.CurrentMap.Info.Index;
                    x = context.Monster.CurrentLocation.X;
                    y = context.Monster.CurrentLocation.Y;
                }
                else if (context.Player?.CurrentMap?.Info != null)
                {
                    mapIndex = context.Player.CurrentMap.Info.Index;
                    x = context.Player.CurrentLocation.X;
                    y = context.Player.CurrentLocation.Y;
                }

                var source = context.Source ?? string.Empty;
                var dropTableKey = context.DropTableKey ?? string.Empty;
                var playerName = context.Player?.Name ?? string.Empty;
                var monsterName = context.Monster?.Name ?? string.Empty;
                var itemName = item.FriendlyName ?? item.Info.FriendlyName ?? item.Info.Name ?? string.Empty;

                var line =
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\t" +
                    $"source={source}\t" +
                    $"key={dropTableKey}\t" +
                    $"player={playerName}\t" +
                    $"monster={monsterName}\t" +
                    $"map={mapIndex}\t" +
                    $"x={x}\t" +
                    $"y={y}\t" +
                    $"item={itemName}\t" +
                    $"itemIndex={item.Info.Index}\t" +
                    $"uid={item.UniqueID}\t" +
                    $"count={item.Count}";

                lock (Gate)
                {
                    File.AppendAllText(filePath, line + Environment.NewLine, Utf8NoBom);
                }
            }
            catch (Exception ex)
            {
                if (_logWriteErrorLogged) return;
                _logWriteErrorLogged = true;
                MessageQueue.Instance.Enqueue($"[Drops] 稀有掉落日志写入失败：{ex.Message}");
            }
        }
    }
}
