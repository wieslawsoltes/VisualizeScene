using SkiaSharp;
using Uno.WinUI.Graphics2DSK;
using Windows.Foundation;

namespace VisualizeScene.Controls.Uno;

public enum EditorIconKind { New, Open, Save, Undo, Redo, Cut, Copy, Paste, Delete, Select, Node, Edge, Pan, ZoomIn, ZoomOut, Fit, Group, Fold, Layout, Search }

/// <summary>Original, resolution-independent 18-unit editor icons. No platform icon font or third-party artwork required.</summary>
public sealed class EditorIcon : SKCanvasElement
{
    private EditorIconKind kind;
    public EditorIconKind Kind { get => kind; set { kind = value; Invalidate(); } }
    public EditorIcon() { Width = Height = 18; IsHitTestVisible = false; }
    protected override void RenderOverride(SKCanvas canvas, Size area)
    {
        var save = canvas.Save();
        try
        {
            canvas.Scale((float)(area.Width / 18), (float)(area.Height / 18));
            using var paint = new SKPaint { IsAntialias = true, Color = new SKColor(58, 69, 80), Style = SKPaintStyle.Stroke, StrokeWidth = 1.3f, StrokeJoin = SKStrokeJoin.Round, StrokeCap = SKStrokeCap.Round };
            void Path(string data, SKColor? fill = null)
            {
                using var path = SKPath.ParseSvgPathData(data);
                if (fill is { } color) { paint.Style = SKPaintStyle.Fill; paint.Color = color; canvas.DrawPath(path, paint); }
                paint.Color = new(58, 69, 80); paint.Style = SKPaintStyle.Stroke; canvas.DrawPath(path, paint);
            }
            switch (kind)
            {
                case EditorIconKind.New: Path("M4 1H11L15 5V17H4Z M11 1V5H15", SKColors.White); break;
                case EditorIconKind.Open: Path("M1 5V15H15V5H8L6 3H1Z", new(244, 193, 72)); Path("M1 8H17L14 15H1Z", new(255, 219, 113)); break;
                case EditorIconKind.Save: Path("M2 1H14L16 3V17H2Z", new(117, 157, 190)); Path("M5 1V7H13V1 M5 17V10H13V17", SKColors.White); break;
                case EditorIconKind.Undo: Path("M6 3L2 7L6 11 M2 7H11Q17 7 15 14"); break;
                case EditorIconKind.Redo: Path("M12 3L16 7L12 11 M16 7H7Q1 7 3 14"); break;
                case EditorIconKind.Copy: Path("M2 2H11V13H2Z", new(220, 233, 243)); Path("M6 5H15V16H6Z", SKColors.White); break;
                case EditorIconKind.Paste: Path("M4 3H15V17H4Z", new(238, 210, 151)); Path("M7 1H12V5H7Z M7 8H12 M7 11H12 M7 14H12", SKColors.White); break;
                case EditorIconKind.Cut: Path("M3 16A2 2 0 1 0 3 12A2 2 0 1 0 3 16 M15 16A2 2 0 1 0 15 12A2 2 0 1 0 15 16 M5 13L14 2 M13 13L4 2"); break;
                case EditorIconKind.Delete: paint.Color = new(178, 53, 53); canvas.DrawLine(4, 4, 14, 14, paint); canvas.DrawLine(14, 4, 4, 14, paint); break;
                case EditorIconKind.Select: Path("M3 1V15L7 11L10 17L13 15L10 9H16Z", SKColors.White); break;
                case EditorIconKind.Node: Path("M2 4H16V14H2Z", new(250, 203, 95)); break;
                case EditorIconKind.Edge: Path("M2 14H8V4H16 M12 1L16 4L12 7"); break;
                case EditorIconKind.Pan: Path("M5 9V5Q6 3 7 5V8V2Q8 0 9 2V8V3Q10 1 11 3V8V5Q13 3 13 5V10L15 8Q17 8 16 11L12 17H7L2 11Q1 8 3 9L5 11Z", new(251, 226, 190)); break;
                case EditorIconKind.ZoomIn: case EditorIconKind.ZoomOut:
                    canvas.DrawCircle(7, 7, 5, paint); canvas.DrawLine(11, 11, 16, 16, paint); canvas.DrawLine(4, 7, 10, 7, paint);
                    if (kind == EditorIconKind.ZoomIn) canvas.DrawLine(7, 4, 7, 10, paint); break;
                case EditorIconKind.Fit: Path("M1 6V1H6 M12 1H17V6 M17 12V17H12 M6 17H1V12 M5 5H13V13H5Z"); break;
                case EditorIconKind.Group: Path("M1 2H17V16H1Z", new(232, 238, 244)); Path("M4 6H8V10H4Z M10 10H14V14H10Z", new(246, 195, 80)); break;
                case EditorIconKind.Fold: Path("M2 2H16V16H2Z", new(226, 236, 245)); Path("M5 9H13 M9 5V13"); break;
                case EditorIconKind.Layout: Path("M6 1H12V5H6Z M1 12H7V17H1Z M11 12H17V17H11Z", new(165, 196, 219)); Path("M9 5V8 M4 12V8H14V12"); break;
                case EditorIconKind.Search: canvas.DrawCircle(7, 7, 5, paint); canvas.DrawLine(11, 11, 16, 16, paint); break;
            }
        }
        finally { canvas.RestoreToCount(save); }
    }
}
