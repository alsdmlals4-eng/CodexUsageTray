using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using CodexUsageTray.Core;

namespace CodexUsageTray;

internal static partial class TrayIconRenderer
{
    private const int IconSize = 32;

    public static Icon CreateNumericIcon(int remainingPercent, UsageSeverity severity) =>
        CreateIcon(Math.Clamp(remainingPercent, 0, 100).ToString(), GetBackgroundColor(severity));

    public static Icon CreateStatusIcon(string symbol) =>
        CreateIcon(symbol, Color.FromArgb(108, 117, 125));

    private static Icon CreateIcon(string text, Color background)
    {
        using var bitmap = new Bitmap(IconSize, IconSize);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        graphics.Clear(Color.Transparent);
        using var brush = new SolidBrush(background);
        graphics.FillEllipse(brush, 1, 1, IconSize - 2, IconSize - 2);

        var fontSize = text.Length switch
        {
            >= 3 => 13f,
            2 => 17f,
            _ => 20f
        };
        var textColor = background.GetBrightness() > 0.65f ? Color.FromArgb(24, 24, 24) : Color.White;
        using var textBrush = new SolidBrush(textColor);
        using var font = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };
        graphics.DrawString(text, font, textBrush, new RectangleF(0, 0, IconSize, IconSize - 1), format);

        var handle = bitmap.GetHicon();
        try
        {
            using var borrowed = Icon.FromHandle(handle);
            return (Icon)borrowed.Clone();
        }
        finally
        {
            _ = DestroyIcon(handle);
        }
    }

    private static Color GetBackgroundColor(UsageSeverity severity) => severity switch
    {
        UsageSeverity.Normal => Color.FromArgb(39, 174, 96),
        UsageSeverity.Warning => Color.FromArgb(242, 201, 76),
        UsageSeverity.Critical => Color.FromArgb(235, 87, 87),
        _ => Color.FromArgb(108, 117, 125)
    };

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial int DestroyIcon(IntPtr handle);
}
