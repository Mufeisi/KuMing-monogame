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

        if (snapshot.Diagnostics.Count > 0)
        {
            text.AppendLine();
            text.AppendLine("诊断：");
            foreach (string diagnostic in snapshot.Diagnostics)
                text.AppendLine($"- {diagnostic}");
        }

        _skillInspectionText.Text = text.ToString();
    }
}
