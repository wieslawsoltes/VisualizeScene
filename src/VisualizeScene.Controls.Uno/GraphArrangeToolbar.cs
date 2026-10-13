using System.Collections.Immutable;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SkiaSharp;
using Uno.WinUI.Graphics2DSK;
using VisualizeScene.Core;
using VisualizeScene.Editing;
using Windows.Foundation;

namespace VisualizeScene.Controls.Uno;

public enum GraphArrangeCommand
{
    AlignLeft, AlignHorizontalCenter, AlignRight, AlignTop, AlignVerticalCenter, AlignBottom,
    DistributeHorizontal, DistributeVertical, SameWidth, SameHeight, SameSize, FitGroups, EnlargeGroups, FitNodeText
}

/// <summary>Compact reusable arrangement commands. Availability is cached by nodes, selection and keyboard focus, never per frame.</summary>
public sealed class GraphArrangeToolbar : StackPanel, IDisposable
{
    private sealed class ArrangeIcon(GraphArrangeCommand command) : SKCanvasElement
    {
        protected override void RenderOverride(SKCanvas canvas, Size area)
        {
            var saved = canvas.Save();
            try
            {
                canvas.Scale((float)(area.Width / 18), (float)(area.Height / 18));
                using var paint = new SKPaint { IsAntialias = true, StrokeWidth = 1 };
                void Line(float x, float y, float a, float b)
                { paint.Color = new(67, 91, 118); paint.Style = SKPaintStyle.Stroke; canvas.DrawLine(x, y, a, b, paint); }
                void Box(float x, float y, float w, float h, bool amber)
                {
                    paint.Color = amber ? new(255, 204, 102) : new(197, 218, 239); paint.Style = SKPaintStyle.Fill; canvas.DrawRect(x, y, w, h, paint);
                    paint.Color = new(77, 94, 113); paint.Style = SKPaintStyle.Stroke; canvas.DrawRect(x, y, w, h, paint);
                }
                if ((int)command <= (int)GraphArrangeCommand.AlignBottom)
                {
                    var type = (int)command;
                    if (type >= 3) { canvas.RotateDegrees(90, 9, 9); type -= 3; }
                    var anchor = type == 0 ? 2 : type == 1 ? 9 : 16;
                    Line(anchor, 1, anchor, 17);
                    float X(float width) => type == 0 ? anchor : type == 1 ? anchor - width / 2 : anchor - width;
                    Box(X(6), 3, 6, 4, true); Box(X(10), 11, 10, 4, false);
                }
                else if (command is GraphArrangeCommand.DistributeHorizontal or GraphArrangeCommand.DistributeVertical)
                {
                    if (command == GraphArrangeCommand.DistributeVertical) canvas.RotateDegrees(90, 9, 9);
                    Box(1, 7, 3, 8, true); Box(7, 4, 4, 11, false); Box(14, 8, 3, 7, true);
                    Line(2, 2, 16, 2); Line(2, 0, 2, 4); Line(9, 0, 9, 3); Line(16, 0, 16, 4);
                }
                else if (command is GraphArrangeCommand.FitGroups or GraphArrangeCommand.EnlargeGroups)
                {
                    Line(1, 5, 1, 1); Line(1, 1, 6, 1); Line(12, 1, 17, 1); Line(17, 1, 17, 5);
                    Line(1, 12, 1, 17); Line(1, 17, 6, 17); Line(12, 17, 17, 17); Line(17, 17, 17, 12);
                    Box(4, 5, 4, 5, true); Box(10, 9, 4, 5, false);
                    if (command == GraphArrangeCommand.EnlargeGroups)
                    {
                        Line(6, 6, 1, 1); Line(12, 12, 17, 17);
                        Line(1, 1, 1, 4); Line(1, 1, 4, 1);
                        Line(17, 17, 14, 17); Line(17, 17, 17, 14);
                    }
                }
                else if (command == GraphArrangeCommand.FitNodeText)
                {
                    Line(1, 5, 1, 1); Line(1, 1, 5, 1); Line(13, 1, 17, 1); Line(17, 1, 17, 5);
                    Line(1, 13, 1, 17); Line(1, 17, 5, 17); Line(13, 17, 17, 17); Line(17, 17, 17, 13);
                    Line(5, 5, 13, 5); Line(9, 5, 9, 13); Line(6, 13, 12, 13);
                }
                else
                {
                    if (command == GraphArrangeCommand.SameHeight) canvas.RotateDegrees(90, 9, 9);
                    Box(2, 2, 7, 5, true); Box(9, 11, 7, command == GraphArrangeCommand.SameSize ? 5 : 3, false);
                    Line(3, 9, 7, 9); Line(10, 8, 14, 8);
                }
            }
            finally { canvas.RestoreToCount(saved); }
        }
    }
    private readonly Dictionary<GraphArrangeCommand, Button> buttons = [];
    private GraphControl? target;
    private EditorSession? subscribedSession;
    private GraphDocument? lastDocument;
    private ImmutableArray<GraphNode> lastNodes;
    private IReadOnlySet<string>? lastSelection;
    private string? lastFocus;
    private bool disposed;
    public GraphArrangeState State { get; private set; }
    public long StateBuilds { get; private set; }
    public event EventHandler? StateChanged;
    public event EventHandler<string>? Error;
    public GraphControl? Target
    {
        get => target;
        set
        {
            ObjectDisposedException.ThrowIf(disposed, this); if (ReferenceEquals(target, value)) return;
            Detach(); target = value; lastDocument = null;
            if (target is not null)
            {
                target.SelectionChanged += Refresh; target.Document.Changed += Changed; target.DocumentChanged += Rebind;
                subscribedSession = target.Session; subscribedSession.FocusedItemChanged += Refresh;
            }
            Refresh(this, EventArgs.Empty);
        }
    }
    public FrameworkElement? FindButton(GraphArrangeCommand command) => buttons.GetValueOrDefault(command);
    public static string Caption(GraphArrangeCommand command) => command switch
    {
        GraphArrangeCommand.AlignLeft => "Align left", GraphArrangeCommand.AlignHorizontalCenter => "Align horizontal centers",
        GraphArrangeCommand.AlignRight => "Align right", GraphArrangeCommand.AlignTop => "Align top",
        GraphArrangeCommand.AlignVerticalCenter => "Align vertical centers", GraphArrangeCommand.AlignBottom => "Align bottom",
        GraphArrangeCommand.DistributeHorizontal => "Distribute horizontally", GraphArrangeCommand.DistributeVertical => "Distribute vertically",
        GraphArrangeCommand.SameWidth => "Make same width", GraphArrangeCommand.SameHeight => "Make same height", GraphArrangeCommand.SameSize => "Make same size",
        GraphArrangeCommand.FitGroups => "Fit groups to children",
        GraphArrangeCommand.EnlargeGroups => "Enlarge groups to children",
        GraphArrangeCommand.FitNodeText => "Fit nodes to text",
        _ => throw new ArgumentOutOfRangeException(nameof(command))
    };
    public static bool CanExecute(GraphArrangeCommand command, GraphArrangeState state) => command switch
    {
        GraphArrangeCommand.AlignLeft or GraphArrangeCommand.AlignHorizontalCenter or GraphArrangeCommand.AlignRight or
        GraphArrangeCommand.AlignTop or GraphArrangeCommand.AlignVerticalCenter or GraphArrangeCommand.AlignBottom => state.MovableRoots >= 2,
        GraphArrangeCommand.DistributeHorizontal or GraphArrangeCommand.DistributeVertical => state.MovableRoots >= 3,
        GraphArrangeCommand.SameWidth or GraphArrangeCommand.SameHeight or GraphArrangeCommand.SameSize => state.ResizableTargets >= 1,
        GraphArrangeCommand.FitGroups or GraphArrangeCommand.EnlargeGroups => state.FittableGroups > 0,
        GraphArrangeCommand.FitNodeText => state.CaptionFittableNodes > 0,
        _ => false
    };
    public GraphArrangeToolbar()
    {
        Orientation = Orientation.Horizontal; Spacing = 1;
        foreach (var command in Enum.GetValues<GraphArrangeCommand>())
        {
            if (command is GraphArrangeCommand.DistributeHorizontal or GraphArrangeCommand.SameWidth or GraphArrangeCommand.FitGroups)
                Children.Add(new Border { Width = 1, Height = 18, Margin = new(3, 0, 3, 0), Background = NodePalette.Brush("#CCD4DD"), VerticalAlignment = VerticalAlignment.Center });
            var button = new Button { Content = new ArrangeIcon(command) { Width = 18, Height = 18, IsHitTestVisible = false },
                Width = 25, Height = 25, MinWidth = 0, MinHeight = 0, Padding = new(3), CornerRadius = new(1),
                Background = NodePalette.Brush("#F6F8FB"), IsEnabled = false };
            AutomationProperties.SetName(button, Caption(command)); AutomationProperties.SetAutomationId(button, "Arrange" + command);
            ToolTipService.SetToolTip(button, Caption(command));
            button.Click += (_, _) =>
            {
                try { if (!disposed && target is not null) target.ArrangeNodes(command); }
                catch (Exception ex) { Error?.Invoke(this, ex.Message); }
            };
            buttons.Add(command, button); Children.Add(button);
        }
    }
    private void Refresh(object? sender, EventArgs args)
    {
        if (disposed) return;
        if (target is null) State = default;
        else
        {
            var document = target.Document; var selection = target.Session.Selection; var focus = target.Session.FocusedItemId;
            if (document == lastDocument && document.Snapshot.Nodes == lastNodes && ReferenceEquals(selection, lastSelection) && focus == lastFocus) return;
            State = GraphArrangeState.Calculate(document.Snapshot, selection, focus); StateBuilds++;
            lastDocument = document; lastNodes = document.Snapshot.Nodes; lastSelection = selection; lastFocus = focus;
        }
        foreach (var (command, button) in buttons)
        {
            button.IsEnabled = CanExecute(command, State);
            if (command is GraphArrangeCommand.SameWidth or GraphArrangeCommand.SameHeight or GraphArrangeCommand.SameSize)
                ToolTipService.SetToolTip(button, Caption(command) + (State.ReferenceId is null ? "" : " · reference: " + State.ReferenceId));
        }
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
    private void Changed(object? sender, GraphChangedEventArgs args) => Refresh(sender, EventArgs.Empty);
    private void Rebind(object? sender, GraphDocumentChangedEventArgs args)
    {
        args.Previous.Changed -= Changed; args.Current.Changed += Changed;
        if (subscribedSession is not null) subscribedSession.FocusedItemChanged -= Refresh;
        subscribedSession = target!.Session; subscribedSession.FocusedItemChanged += Refresh; lastDocument = null; Refresh(sender, EventArgs.Empty);
    }
    private void Detach()
    {
        if (target is null) return;
        target.SelectionChanged -= Refresh; target.Document.Changed -= Changed; target.DocumentChanged -= Rebind;
        if (subscribedSession is not null) subscribedSession.FocusedItemChanged -= Refresh;
        subscribedSession = null; lastDocument = null; lastNodes = default; lastSelection = null; lastFocus = null;
    }
    public new void Dispose()
    {
        if (disposed) return; Detach(); target = null; State = default;
        foreach (var button in buttons.Values) button.IsEnabled = false;
        disposed = true;
    }
}
