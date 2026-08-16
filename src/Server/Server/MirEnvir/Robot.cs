using Server.MirObjects;
using System.Text.RegularExpressions;
using Server.Scripting;

namespace Server.MirEnvir
{
    public class Robot
    {
        protected static Envir Envir
        {
            get { return Envir.Main; }
        }

        public int? Month;
        public int? Day;
        public int? Hour;
        public int? Minute;
        public DayOfWeek? DayOfWeek;
        private string Page;

        private static bool CheckHour;
        private static bool CheckMinute;
        private static DateTime NextCheck;
        private static readonly List<Robot> Robots = new List<Robot>();
        private static readonly LingFengRobotScheduler LingFengScheduler = new();

        private static void SetNextCheck()
        {
            var next = Envir.Now;
            next = next.AddSeconds(-next.Second);
            next = next.AddMinutes(1);

            if (!CheckMinute)
            {
                next = next.AddMinutes(-next.Minute);
                next = next.AddHours(1);

                if (!CheckHour)
                {
                    next = next.AddHours(-next.Hour);
                    next = next.AddDays(1);
                }
            }

            NextCheck = next;
        }

        public static void Clear()
        {
            Robots.Clear();
            CheckHour = false;
            CheckMinute = false;
            NextCheck = default;
            LingFengScheduler.Stop();
        }

        public static void PublishLingFengSchedules(
            LingFengRobotScheduleSnapshot snapshot,
            DateTime now) => LingFengScheduler.Publish(snapshot, now);

        private bool IsMatch(DateTime date)
        {
            if (date.Kind == DateTimeKind.Local)
            {
                date = date.ToUniversalTime();
            }
            else if (date.Kind == DateTimeKind.Unspecified)
            {
                date = DateTime.SpecifyKind(date, DateTimeKind.Local).ToUniversalTime();
            }

            if (Month != null && date.Month != Month) return false;
            if (Day != null && date.Day != Day) return false;
            if (Hour != null && date.Hour != Hour) return false;
            if (Minute != null && date.Minute != Minute) return false;
            if (DayOfWeek != null && date.DayOfWeek != DayOfWeek) return false;

            return true;
        }

        public static void Process(NPCScript script)
            => ProcessAt(script, Envir.Now);

        internal static void ProcessAt(NPCScript script, DateTime now)
        {
            if (script == null) return;
            LingFengScheduler.Process(now, script.Call, (page, error) =>
                MessageQueue.Instance.Enqueue($"[TxtScripts][LFENV10-ROBOT-006] Robot 页面执行失败：{page}，{error.GetType().Name}。"));
            if (NextCheck > now)
            {
                return;
            }

            var matches = Robots.Where(x => x.IsMatch(now));

            foreach (var match in matches)
            {
                script.Call(match.Page);
            }

            SetNextCheck();
        }

        public static void AddRobot(string page)
        {
            Regex regex = new Regex(@"\(([0-9#]{1,2}),([0-9#]{1,2}),([0-9#]{1,2}),([0-9#]{1,2}),([^\s]+)\)");
            Match match = regex.Match(page);

            if (!match.Success) return;

            var robot = new Robot { Page = page };

            if (int.TryParse(match.Groups[1].Value, out int val))
            {
                robot.Month = val;
            }
            if (int.TryParse(match.Groups[2].Value, out val))
            {
                robot.Day = val;
            }
            if (int.TryParse(match.Groups[3].Value, out val))
            {
                CheckHour = true;
                robot.Hour = val;
            }
            if (int.TryParse(match.Groups[4].Value, out val))
            {
                CheckMinute = true;
                robot.Minute = val;
            }
            if (Enum.TryParse<DayOfWeek>(match.Groups[5].Value, true, out DayOfWeek enumVal))
            {
                robot.DayOfWeek = enumVal;
            }

            Robots.Add(robot);
        }
    }
}
