using System;
using System.Collections.Generic;
using FairyGUI;
using MonoShare.MirObjects;
using MonoShare.MirScenes;

namespace MonoShare
{
    internal static partial class FairyGuiHost
    {
        private static readonly string[] DefaultBottomLevelKeywords = { "level", "lv", "等级" };
        private static readonly string[] DefaultBottomWeightKeywords = { "weight", "bagweight", "负重", "背包负重" };
        private static readonly string[] DefaultBottomExpKeywords = { "exp", "experience", "经验", "进度" };
        private static readonly string[] DefaultBottomVolumeKeywords = { "volume", "sound", "音量", "声音" };
        private static readonly string[] DefaultBottomBatteryKeywords = { "battery", "power", "电量", "电池" };
        private static readonly string[] DefaultBottomSignalKeywords = { "signal", "network", "wifi", "信号", "网络" };

        private static GComponent _mobileMainHudBottomRoot;
        private static GTextField _mobileBottomLevel;
        private static GTextField _mobileBottomWeight;
        private static GTextField _mobileBottomExpText;
        private static GProgressBar _mobileBottomExpBar;
        private static GTextField _mobileBottomVolume;
        private static GTextField _mobileBottomBattery;
        private static GTextField _mobileBottomSignal;

        private static DateTime _nextMobileBottomBindAttemptUtc = DateTime.MinValue;
        private static int _mobileBottomLastHash;

        private static void ResetMobileMainHudBottomStatsBindings()
        {
            _mobileMainHudBottomRoot = null;
            _mobileBottomLevel = null;
            _mobileBottomWeight = null;
            _mobileBottomExpText = null;
            _mobileBottomExpBar = null;
            _mobileBottomVolume = null;
            _mobileBottomBattery = null;
            _mobileBottomSignal = null;

            _nextMobileBottomBindAttemptUtc = DateTime.MinValue;
            _mobileBottomLastHash = 0;
        }

        private static int ScoreBottomStatTextCandidate(GObject obj, string[] keywords)
        {
            int score = ScoreAnyField(obj, keywords, equalsWeight: 170, startsWithWeight: 85, containsWeight: 35);
            score += ScoreRect(obj, preferLower: true, areaDivisor: 1200, maxAreaScore: 160);
            if (obj.packageItem?.exported == true)
                score += 5;
            return score;
        }

        private static int ScoreBottomProgressCandidate(GObject obj, string[] keywords)
        {
            int score = ScoreAnyField(obj, keywords, equalsWeight: 170, startsWithWeight: 85, containsWeight: 35);
            score += ScoreRect(obj, preferLower: true, areaDivisor: 1600, maxAreaScore: 180);
            if (obj is GProgressBar)
                score += 20;
            if (obj.packageItem?.exported == true)
                score += 5;
            return score;
        }

        private static GTextField SelectBestText(GComponent root, string[] keywords, ISet<GObject> excluded, int minScore)
        {
            List<(int Score, GObject Target)> candidates = CollectMobileChatCandidates(
                root,
                obj => obj is GTextField && obj is not GTextInput,
                keywords,
                ScoreBottomStatTextCandidate);

            for (int i = 0; i < candidates.Count; i++)
            {
                (int score, GObject target) = candidates[i];
                if (score < minScore)
                    break;

                if (target is not GTextField tf || tf._disposed)
                    continue;

                if (excluded != null && excluded.Contains(tf))
                    continue;

                return tf;
            }

            return null;
        }

        private static GProgressBar SelectBestProgress(GComponent root, string[] keywords, ISet<GObject> excluded, int minScore)
        {
            List<(int Score, GObject Target)> candidates = CollectMobileChatCandidates(
                root,
                obj => obj is GProgressBar,
                keywords,
                ScoreBottomProgressCandidate);

            for (int i = 0; i < candidates.Count; i++)
            {
                (int score, GObject target) = candidates[i];
                if (score < minScore)
                    break;

                if (target is not GProgressBar bar || bar._disposed)
                    continue;

                if (excluded != null && excluded.Contains(bar))
                    continue;

                return bar;
            }

            return null;
        }

        private static void TryBindMobileMainHudBottomStatsIfDue()
        {
            if (_mobileMainHud == null || _mobileMainHud._disposed)
                return;

            if (DateTime.UtcNow < _nextMobileBottomBindAttemptUtc)
                return;

            _nextMobileBottomBindAttemptUtc = DateTime.UtcNow.AddSeconds(2);

            try
            {
                _mobileMainHudBottomRoot = TryFindChildByNameRecursive(_mobileMainHud, "BottomUI") as GComponent ?? _mobileMainHud;

                var used = new HashSet<GObject>();

                _mobileBottomLevel ??= SelectBestText(_mobileMainHudBottomRoot, DefaultBottomLevelKeywords, used, minScore: 35);
                if (_mobileBottomLevel != null) used.Add(_mobileBottomLevel);

                _mobileBottomWeight ??= SelectBestText(_mobileMainHudBottomRoot, DefaultBottomWeightKeywords, used, minScore: 35);
                if (_mobileBottomWeight != null) used.Add(_mobileBottomWeight);

                _mobileBottomExpText ??= SelectBestText(_mobileMainHudBottomRoot, DefaultBottomExpKeywords, used, minScore: 35);
                if (_mobileBottomExpText != null) used.Add(_mobileBottomExpText);

                _mobileBottomExpBar ??= SelectBestProgress(_mobileMainHudBottomRoot, DefaultBottomExpKeywords, used, minScore: 35);
                if (_mobileBottomExpBar != null) used.Add(_mobileBottomExpBar);

                _mobileBottomVolume ??= SelectBestText(_mobileMainHudBottomRoot, DefaultBottomVolumeKeywords, used, minScore: 35);
                if (_mobileBottomVolume != null) used.Add(_mobileBottomVolume);

                _mobileBottomBattery ??= SelectBestText(_mobileMainHudBottomRoot, DefaultBottomBatteryKeywords, used, minScore: 35);
                if (_mobileBottomBattery != null) used.Add(_mobileBottomBattery);

                _mobileBottomSignal ??= SelectBestText(_mobileMainHudBottomRoot, DefaultBottomSignalKeywords, used, minScore: 35);
                if (_mobileBottomSignal != null) used.Add(_mobileBottomSignal);

                if (Settings.DebugMode)
                {
                    if (_mobileBottomLevel != null) CMain.SaveLog("FairyGUI: HUD底栏绑定 Level=" + DescribeObject(_mobileMainHud, _mobileBottomLevel));
                    if (_mobileBottomWeight != null) CMain.SaveLog("FairyGUI: HUD底栏绑定 Weight=" + DescribeObject(_mobileMainHud, _mobileBottomWeight));
                    if (_mobileBottomExpText != null) CMain.SaveLog("FairyGUI: HUD底栏绑定 ExpText=" + DescribeObject(_mobileMainHud, _mobileBottomExpText));
                    if (_mobileBottomExpBar != null) CMain.SaveLog("FairyGUI: HUD底栏绑定 ExpBar=" + DescribeObject(_mobileMainHud, _mobileBottomExpBar));
                    if (_mobileBottomVolume != null) CMain.SaveLog("FairyGUI: HUD底栏绑定 Volume=" + DescribeObject(_mobileMainHud, _mobileBottomVolume));
                    if (_mobileBottomBattery != null) CMain.SaveLog("FairyGUI: HUD底栏绑定 Battery=" + DescribeObject(_mobileMainHud, _mobileBottomBattery));
                    if (_mobileBottomSignal != null) CMain.SaveLog("FairyGUI: HUD底栏绑定 Signal=" + DescribeObject(_mobileMainHud, _mobileBottomSignal));
                }
            }
            catch
            {
            }
        }

        private static void TryRefreshMobileMainHudBottomStatsIfDue(bool force)
        {
            if (_stage == null || !_initialized || !_packagesLoaded)
                return;

            if (_mobileMainHud == null || _mobileMainHud._disposed)
            {
                if (_mobileMainHudBottomRoot != null || _mobileBottomLevel != null || _mobileBottomWeight != null)
                    ResetMobileMainHudBottomStatsBindings();
                return;
            }

            TryBindMobileMainHudBottomStatsIfDue();

            UserObject user = GameScene.User;
            if (user == null)
                return;

            int level = 0;
            int curWeight = 0;
            int maxWeight = 0;
            long exp = 0;
            long maxExp = 0;
            int volume = Settings.Volume;
            int battery = Settings.RuntimeBatteryPercent;
            int signal = Settings.RuntimeSignalLevel;

            try
            {
                level = user.Level;
            }
            catch
            {
                level = 0;
            }

            try
            {
                curWeight = user.CurrentBagWeight;
                maxWeight = user.Stats[Stat.BagWeight];
            }
            catch
            {
                curWeight = 0;
                maxWeight = 0;
            }

            try
            {
                exp = user.Experience;
                maxExp = user.MaxExperience;
            }
            catch
            {
                exp = 0;
                maxExp = 0;
            }

            int hash = level;
            hash = (hash * 397) ^ curWeight;
            hash = (hash * 397) ^ maxWeight;
            hash = (hash * 397) ^ exp.GetHashCode();
            hash = (hash * 397) ^ maxExp.GetHashCode();
            hash = (hash * 397) ^ volume;
            hash = (hash * 397) ^ battery;
            hash = (hash * 397) ^ signal;

            if (!force && hash == _mobileBottomLastHash)
                return;

            _mobileBottomLastHash = hash;

            try
            {
                if (_mobileBottomLevel != null && !_mobileBottomLevel._disposed)
                    _mobileBottomLevel.text = level.ToString();
            }
            catch
            {
            }

            try
            {
                if (_mobileBottomWeight != null && !_mobileBottomWeight._disposed)
                    _mobileBottomWeight.text = maxWeight > 0 ? $"{curWeight}/{maxWeight}" : curWeight.ToString();
            }
            catch
            {
            }

            try
            {
                if (_mobileBottomExpText != null && !_mobileBottomExpText._disposed)
                {
                    if (maxExp > 0)
                    {
                        double percent = Math.Clamp(exp / (double)maxExp, 0.0, 1.0) * 100.0;
                        _mobileBottomExpText.text = $"{percent:0.##}%";
                    }
                    else
                    {
                        _mobileBottomExpText.text = string.Empty;
                    }
                }
            }
            catch
            {
            }

            try
            {
                if (_mobileBottomExpBar != null && !_mobileBottomExpBar._disposed)
                {
                    _mobileBottomExpBar.max = Math.Max(1, maxExp);
                    _mobileBottomExpBar.value = Math.Clamp(exp, 0, Math.Max(0, maxExp));
                }
            }
            catch
            {
            }

            try
            {
                if (_mobileBottomVolume != null && !_mobileBottomVolume._disposed)
                    _mobileBottomVolume.text = $"{volume}%";
            }
            catch
            {
            }

            try
            {
                if (_mobileBottomBattery != null && !_mobileBottomBattery._disposed)
                    _mobileBottomBattery.text = battery >= 0 ? $"{Math.Clamp(battery, 0, 100)}%" : "--";
            }
            catch
            {
            }

            try
            {
                if (_mobileBottomSignal != null && !_mobileBottomSignal._disposed)
                    _mobileBottomSignal.text = signal >= 0 ? Math.Clamp(signal, 0, 4).ToString() : "--";
            }
            catch
            {
            }
        }
    }
}
