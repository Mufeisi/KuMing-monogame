using Server.Authoring;
using Server.MirDatabase;

namespace Server;

public partial class MagicInfoForm
{
    private readonly Dictionary<MagicInfo, SkillEditingSession> _skillEditingSessions = new();
    private SkillEditingSession? _skillEditingSession;
    private Label? _skillEditingStatus;
    private bool _skillSafeEditingEnabled;

    private void InitializeSkillSafeEditing()
    {
        _skillSafeEditingEnabled = true;
        MakeCombatFieldsReadOnly();

        var toolbar = new FlowLayoutPanel
        {
            Name = "SkillEditingToolbar",
            Dock = DockStyle.Bottom,
            Height = 42,
            Padding = new Padding(6, 6, 6, 4),
            WrapContents = false
        };
        toolbar.Controls.Add(CreateSkillEditingButton("SkillReviewButton", "校验/差异", ReviewSkillDraft));
        toolbar.Controls.Add(CreateSkillEditingButton("SkillSaveButton", "显式保存", SaveSkillDraft));
        toolbar.Controls.Add(CreateSkillEditingButton("SkillCancelButton", "取消草稿", CancelSkillDraft));
        toolbar.Controls.Add(CreateSkillEditingButton("SkillReloadButton", "重载事实", ReloadSkillDraft));
        _skillEditingStatus = new Label
        {
            Name = "SkillEditingStatus",
            AutoSize = true,
            Margin = new Padding(12, 5, 0, 0),
            Text = "只允许编辑技能名称和图标；战斗字段为只读。"
        };
        toolbar.Controls.Add(_skillEditingStatus);
        tabPage1.Controls.Add(toolbar);
        toolbar.BringToFront();
    }

    private static Button CreateSkillEditingButton(string name, string text, EventHandler handler)
    {
        var button = new Button { Name = name, Text = text, AutoSize = true };
        button.Click += handler;
        return button;
    }

    private void BeginSkillEditingSession(MagicInfo? source)
    {
        if (source == null)
        {
            _skillEditingSession = null;
            return;
        }

        if (!_skillEditingSessions.TryGetValue(source, out SkillEditingSession? session))
        {
            session = new SkillEditingSession(source);
            _skillEditingSessions.Add(source, session);
        }
        _skillEditingSession = session;
        UpdateSkillEditingStatus();
    }

    private void ObserveSkillDraft(string name, byte icon)
    {
        if (_skillEditingSession == null) return;
        _skillEditingSession.Observe(new SkillSafeDraft(name, icon));
        UpdateSkillEditingStatus();
    }

    private void ReviewSkillDraft(object? sender, EventArgs e)
    {
        Label? status = _skillEditingStatus;
        if (_skillEditingSession == null || status == null) return;
        SkillEditReview review = _skillEditingSession.Review();
        status.ForeColor = review.HasErrors ? Color.Firebrick : SystemColors.ControlText;
        status.Text = review.HasErrors
            ? $"校验失败：{string.Join("；", review.Diagnostics.Select(item => $"{item.Code} {item.Message}"))}"
            : review.HasChanges
                ? $"待保存差异：{string.Join("；", review.Differences.Select(item => $"{item.Field}: {item.Before} → {item.After}"))}"
                : "没有白名单字段差异。";
    }

    private void SaveSkillDraft(object? sender, EventArgs e)
    {
        Label? status = _skillEditingStatus;
        if (_skillEditingSession == null || status == null) return;
        SkillEditCommitResult result = _skillEditingSession.TryCommit(() => Envir.SaveDB());
        status.ForeColor = result.Completed ? Color.DarkGreen : Color.Firebrick;
        status.Text = result.Completed ? "保存成功；事实对象和持久化已同步。" : result.Error;
        if (result.Completed)
        {
            MagiclistBox.Refresh();
            UpdateMagicForm();
        }
    }

    private void CancelSkillDraft(object? sender, EventArgs e)
    {
        Label? status = _skillEditingStatus;
        if (_skillEditingSession == null || status == null) return;
        _skillEditingSession.Cancel();
        UpdateMagicForm();
        status.ForeColor = SystemColors.ControlText;
        status.Text = "草稿已取消，事实对象未改变。";
    }

    private void ReloadSkillDraft(object? sender, EventArgs e)
    {
        Label? status = _skillEditingStatus;
        if (_skillEditingSession == null || status == null) return;
        _skillEditingSession.ReloadFromSource();
        UpdateMagicForm();
        status.ForeColor = SystemColors.ControlText;
        status.Text = "已从事实对象重载，未执行保存。";
    }

    private void UpdateSkillEditingStatus()
    {
        if (_skillEditingStatus == null || _skillEditingSession == null) return;
        _skillEditingStatus.ForeColor = SystemColors.ControlText;
        _skillEditingStatus.Text = _skillEditingSession.IsDirty
            ? "白名单草稿有未保存变更。"
            : "只允许编辑技能名称和图标；战斗字段为只读。";
    }

    private void MakeCombatFieldsReadOnly()
    {
        foreach (TextBox input in new[]
        {
            txtSkillLvl1Req, txtSkillLvl2Req, txtSkillLvl3Req,
            txtSkillLvl1Points, txtSkillLvl2Points, txtSkillLvl3Points,
            txtMPBase, txtMPIncrease, txtDmgBaseMin, txtDmgBaseMax,
            txtDmgBonusMin, txtDmgBonusMax, txtDelayBase, txtDelayReduction,
            txtRange, txtDmgMultBase, txtDmgMultBoost
        })
        {
            input.ReadOnly = true;
            input.TabStop = false;
            input.BackColor = SystemColors.Control;
        }
    }
}
