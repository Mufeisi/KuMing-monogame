using System.Diagnostics;
using System.Drawing.Imaging;

namespace Launcher.ThemeRuntime;

internal sealed class AnnouncementCard : Panel
{
    private Image? _ownedImage;
    public AnnouncementCard(LauncherAnnouncement item, string assetRoot)
    {
        Padding = new Padding(10); BackColor = Color.FromArgb(38, 42, 55); Margin = new Padding(0, 0, 0, 8);
        var title = new Label { Text = item.Title, Font = new Font(SystemFonts.MessageBoxFont ?? Control.DefaultFont, FontStyle.Bold), AutoSize = true, ForeColor = Color.White };
        string imagePath = LauncherSnapshotValidator.ResolveAsset(assetRoot, item.Image);
        int textLeft = 10;
        if (!string.IsNullOrEmpty(imagePath))
        {
            using Image source = Image.FromFile(imagePath);
            _ownedImage = new Bitmap(source);
            Controls.Add(new PictureBox { Image = _ownedImage, SizeMode = PictureBoxSizeMode.Zoom, Location = new Point(8, 8), Size = new Size(82, 60) });
            textLeft = 100;
            title.Location = new Point(textLeft, 10);
        }
        var summary = new Label { Text = item.Summary, AutoEllipsis = true, ForeColor = Color.Gainsboro, Location = new Point(textLeft, 36), Width = 520 };
        var date = new Label { Text = item.Date, AutoSize = true, ForeColor = Color.Silver, Anchor = AnchorStyles.Top | AnchorStyles.Right, Location = new Point(Width - 120, 10) };
        Controls.AddRange(new Control[] { title, summary, date });
        if (!string.IsNullOrWhiteSpace(item.ExternalUrl)) { Cursor = Cursors.Hand; Click += (_, _) => Process.Start(new ProcessStartInfo(item.ExternalUrl) { UseShellExecute = true })?.Dispose(); }
    }
    protected override void Dispose(bool disposing) { if (disposing) { _ownedImage?.Dispose(); _ownedImage = null; } base.Dispose(disposing); }
}

internal sealed class ImageStateButton : Button
{
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Image? BaseImage { get; set; }
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Image? HoverImage { get; set; }
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Image? PressedImage { get; set; }
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Image? DisabledImage { get; set; }
    protected override void OnPaint(PaintEventArgs e)
    {
        if (BaseImage is null) { base.OnPaint(e); return; }
        bool inside = ClientRectangle.Contains(PointToClient(Cursor.Position));
        bool pressed = Enabled && MouseButtons == MouseButtons.Left && inside;
        Image image = !Enabled && DisabledImage is not null ? DisabledImage : pressed && PressedImage is not null ? PressedImage : Enabled && inside && HoverImage is not null ? HoverImage : BaseImage;
        bool customState = image != BaseImage;
        ColorMatrix matrix = customState ? new ColorMatrix() : Enabled
            ? (pressed ? Matrix(.82f) : inside ? Matrix(1.08f) : new ColorMatrix())
            : new ColorMatrix(new[] { new float[] {.3f,.3f,.3f,0,0},new float[] {.3f,.3f,.3f,0,0},new float[] {.3f,.3f,.3f,0,0},new float[] {0,0,0,.55f,0},new float[] {0,0,0,0,1} });
        using var attributes = new ImageAttributes();
        attributes.SetColorMatrix(matrix);
        e.Graphics.DrawImage(image, ClientRectangle, 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, attributes);
        TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, ForeColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }
    private static ColorMatrix Matrix(float value) => new(new[] { new float[] {value,0,0,0,0},new float[] {0,value,0,0,0},new float[] {0,0,value,0,0},new float[] {0,0,0,1,0},new float[] {0,0,0,0,1} });
    protected override void Dispose(bool disposing) { if (disposing) { BaseImage?.Dispose(); HoverImage?.Dispose(); PressedImage?.Dispose(); DisabledImage?.Dispose(); BaseImage = HoverImage = PressedImage = DisabledImage = null; } base.Dispose(disposing); }
}
