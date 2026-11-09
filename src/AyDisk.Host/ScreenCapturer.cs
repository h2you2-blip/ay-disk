using System.Drawing.Imaging;

namespace AyDisk.Host;

/// <summary>Captures the primary screen (with mouse cursor) as JPEG.</summary>
public sealed class ScreenCapturer
{
    private readonly ImageCodecInfo _jpeg =
        ImageCodecInfo.GetImageEncoders().First(e => e.FormatID == ImageFormat.Jpeg.Guid);

    public long Quality { get; set; } = 55; // 1..100

    public (byte[] Data, int Width, int Height) Capture()
    {
        var b = Screen.PrimaryScreen!.Bounds;
        using var bmp = new Bitmap(b.Width, b.Height, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.CopyFromScreen(b.Location, Point.Empty, b.Size);
            var pos = Cursor.Position;
            if (b.Contains(pos))
            {
                var c = Cursors.Default;
                c.Draw(g, new Rectangle(pos.X - b.X, pos.Y - b.Y, c.Size.Width, c.Size.Height));
            }
        }

        using var ep = new EncoderParameters(1);
        ep.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, Quality);
        using var ms = new MemoryStream();
        bmp.Save(ms, _jpeg, ep);
        return (ms.ToArray(), b.Width, b.Height);
    }
}
