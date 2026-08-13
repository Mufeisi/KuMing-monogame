using Xunit;

namespace Server.ContentAuthoringIntegration.Windows;

public sealed class MapContentAuthoringFormTests
{
    [Fact]
    public void 地图内容窗体提供显式编辑会话入口且构造不修改原地图()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                using var form = new global::Server.MirForms.VisualMapInfo.VForm();
                ToolStrip toolbar = Assert.IsType<ToolStrip>(Assert.Single(form.Controls.Find("ContentAuthoringToolbar", true)));
                Assert.Equal("撤销", Assert.Single(toolbar.Items.Find("UndoContentButton", true)).Text);
                Assert.Equal("重做", Assert.Single(toolbar.Items.Find("RedoContentButton", true)).Text);
                Assert.Equal("校验与差异", Assert.Single(toolbar.Items.Find("ReviewContentButton", true)).Text);
                Assert.Equal("保存", Assert.Single(toolbar.Items.Find("SaveContentButton", true)).Text);
                Assert.Equal("取消", Assert.Single(toolbar.Items.Find("CancelContentButton", true)).Text);
                Assert.Equal(DialogResult.None, form.DialogResult);
                Assert.False(form.HasCommittedChanges);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "地图内容窗体宿主测试超时。");
        Assert.Null(failure);
    }
}
