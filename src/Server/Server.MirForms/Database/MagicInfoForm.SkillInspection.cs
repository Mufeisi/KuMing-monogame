using System.Text;
using Server.Authoring;

namespace Server;

public partial class MagicInfoForm
{
    private RichTextBox _skillInspectionText;

    private void InitializeSkillInspection()
    {
        var page = new TabPage
        {
            Name = "SkillInspectionTab",
            Text = "只读理解",
            Padding = new Padding(10)
        };

        var notice = new Label
        {
            Dock = DockStyle.Top,
            Height = 38,
            Text = "此页只读取当前 MagicInfo，不保存也不参与战斗计算。实际战斗结果始终由服务端决定。",
            ForeColor = Color.DarkSlateBlue
        };

        _skillInspectionText = new RichTextBox
        {
            Name = "SkillInspectionText",
            Dock = DockStyle.Fill,
            ReadOnly = true,
            DetectUrls = false,
            BackColor = SystemColors.Window,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font(FontFamily.GenericMonospace, 9F),
            AccessibleName = "技能只读理解结果"
        };

        page.Controls.Add(_skillInspectionText);
        page.Controls.Add(notice);
        tabControl1.TabPages.Add(page);
    }

    private void RefreshSkillInspection()
    {
        if (_skillInspectionText == null)
            return;

        if (_selectedMagicInfo == null)
        {
            _skillInspectionText.Text = "请从左侧选择一个技能。";
            return;
        }

        ItemInfo book = Envir.GetBook((short)_selectedMagicInfo.Spell);
        SkillInspectionSnapshot snapshot = SkillInspector.Build(_selectedMagicInfo, book?.Name);
        var text = new StringBuilder();
        text.AppendLine($"技能：{snapshot.Name}  ({snapshot.Spell})");
        text.AppendLine($"图标：{snapshot.Icon}    配置距离：{snapshot.Range}");
        text.AppendLine($"技能书：{(snapshot.BookResolved ? snapshot.BookName : "未解析")}");
        text.AppendLine();
        text.AppendLine("等级  角色等级  熟练度  MP消耗  冷却(ms)  结果区间");
        foreach (SkillLevelInspection level in snapshot.Levels)
        {
            text.AppendLine($"{level.SkillLevel,4}  {level.RequiredCharacterLevel,8}  {level.RequiredExperience,6}  {level.MpCost,6}  {level.CooldownMilliseconds,8}  {level.MinimumResult}-{level.MaximumResult}");
        }

        text.AppendLine();
        text.AppendLine($"配置事实源：{SkillInspectionSnapshot.ConfigurationOwner}");
        text.AppendLine($"服务端行为拥有者：{SkillInspectionSnapshot.RuntimeOwner}");
        text.AppendLine($"客户端数据：{SkillInspectionSnapshot.ClientProjection}");

        SkillSpatialProfile spatial = SkillSpatialInspector.Build(snapshot.Spell, skillLevel: 3);
        text.AppendLine();
        text.AppendLine("空间档案（3 级，朝上预览）：");
        text.AppendLine($"建模状态：{(spatial.IsModeled ? "已核对" : "未建模")}");
        text.AppendLine($"目标条件：{FormatTargetCondition(spatial.TargetCondition)}");
        text.AppendLine($"中心类型：{FormatCenterKind(spatial.CenterKind)}");
        text.AppendLine($"方向：{spatial.Orientation}");
        text.AppendLine(spatial.RenderGrid());
        text.AppendLine("图例：中=中心，主=主要作用点，附=等级附加点，·=不在档案内");
        text.AppendLine($"说明：{spatial.Explanation}");
        text.AppendLine($"行为证据：{spatial.BehaviorEvidence}");

        SkillTimelineProfile timeline = SkillTimelineInspector.Build(snapshot.Spell, sampleDistance: 5);
        text.AppendLine();
        text.AppendLine("表现时间线（距离 5 格样例）：");
        text.AppendLine($"建模状态：{(timeline.IsModeled ? "已核对" : "未建模")}");
        if (timeline.IsModeled)
        {
            foreach (SkillTimelineEvent item in timeline.Events)
            {
                string duration = item.DurationMilliseconds.HasValue ? $"，持续 {item.DurationMilliseconds.Value} ms" : string.Empty;
                text.AppendLine($"- {FormatTimelinePhase(item.Phase)} @ {item.Timing}{duration}：{item.Description} [{(item.ServerAuthoritative ? "服务端权威" : "客户端表现")}] ");
            }

            text.AppendLine("资源引用：");
            foreach (SkillResourceReference resource in timeline.Resources)
            {
                text.AppendLine($"- {resource.Kind}：{resource.LogicalReference}");
                text.AppendLine($"  PC={resource.PcReference}；Android={resource.AndroidReference}；代码一致={resource.CodeParityVerified}；实体已核验={resource.PhysicalAssetVerified}");
                text.AppendLine($"  {resource.VerificationNote}");
            }
        }
        text.AppendLine($"说明：{timeline.Explanation}");
        text.AppendLine($"行为证据：{timeline.BehaviorEvidence}");

        if (snapshot.Diagnostics.Count > 0)
        {
            text.AppendLine();
            text.AppendLine("诊断：");
            foreach (string diagnostic in snapshot.Diagnostics)
                text.AppendLine($"- {diagnostic}");
        }

        _skillInspectionText.Text = text.ToString();
    }

    private static string FormatTargetCondition(SkillTargetCondition condition) => condition switch
    {
        SkillTargetCondition.HostileObject => "敌对对象（服务端复核）",
        SkillTargetCondition.HostileObjectWithFlightPath => "敌对对象且飞行路径可达（服务端复核）",
        SkillTargetCondition.MapLocation => "地图坐标（对象仍由服务端过滤）",
        SkillTargetCondition.SelfDirection => "施法者与朝向",
        _ => "未知，不推断"
    };

    private static string FormatCenterKind(SkillCenterKind center) => center switch
    {
        SkillCenterKind.Target => "目标格",
        SkillCenterKind.SelectedLocation => "选定地图格",
        SkillCenterKind.Caster => "施法者格",
        _ => "未知，不推断"
    };

    private static string FormatTimelinePhase(SkillTimelinePhase phase) => phase switch
    {
        SkillTimelinePhase.Cast => "施法",
        SkillTimelinePhase.Flight => "飞行",
        SkillTimelinePhase.Hit => "命中",
        SkillTimelinePhase.PersistentEffect => "持续效果",
        SkillTimelinePhase.Sound => "音效",
        _ => "未知"
    };
}
