namespace AyDisk.Viewer;

/// <summary>PictureBox that can take keyboard focus and receives ALL keys (Tab, arrows, etc.).</summary>
public class RemoteScreen : PictureBox
{
    public RemoteScreen()
    {
        SetStyle(ControlStyles.Selectable, true);
        TabStop = true;
        SizeMode = PictureBoxSizeMode.Zoom;
        BackColor = Color.Black;
    }

    protected override bool IsInputKey(Keys keyData) => true;

    /// <summary>Converts a point on this control to 0..1 coordinates on the remote screen (handles Zoom letterboxing).</summary>
    public bool TryMap(Point p, out double x, out double y)
    {
        x = y = 0;
        if (Image == null) return false;
        float ratio = Math.Min((float)Width / Image.Width, (float)Height / Image.Height);
        float w = Image.Width * ratio, h = Image.Height * ratio;
        float offX = (Width - w) / 2, offY = (Height - h) / 2;
        x = (p.X - offX) / w;
        y = (p.Y - offY) / h;
        return x >= 0 && x <= 1 && y >= 0 && y <= 1;
    }
}
