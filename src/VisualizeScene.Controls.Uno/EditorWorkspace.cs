using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace VisualizeScene.Controls.Uno;

/// <summary>Reusable three-pane workspace. Resizers support mouse/touch and keyboard, with responsive rails and preserved preferred widths.</summary>
public sealed class EditorWorkspace : Grid
{
    private readonly ContentControl left = Host(), center = Host(), right = Host();
    private readonly Thumb leftGrip = new(), rightGrip = new();
    private double leftWidth = 210, rightWidth = 280;
    private bool showLeft = true, showRight = true;
    public event EventHandler? PaneLayoutChanged;
    public UIElement? LeftContent { get => left.Content as UIElement; set => left.Content = value; }
    public UIElement? MainContent { get => center.Content as UIElement; set => center.Content = value; }
    public UIElement? RightContent { get => right.Content as UIElement; set => right.Content = value; }
    public double LeftPaneWidth { get => leftWidth; set { Validate(value); var next = Math.Clamp(value, 180, 420); if (leftWidth == next) return; leftWidth = next; ArrangePanes(); } }
    public double RightPaneWidth { get => rightWidth; set { Validate(value); var next = Math.Clamp(value, 240, 480); if (rightWidth == next) return; rightWidth = next; ArrangePanes(); } }
    public bool ShowLeftPane { get => showLeft; set { if (showLeft == value) return; showLeft = value; ArrangePanes(); } }
    public bool ShowRightPane { get => showRight; set { if (showRight == value) return; showRight = value; ArrangePanes(); } }
    public bool IsLeftPaneVisible => left.Visibility == Visibility.Visible;
    public bool IsRightPaneVisible => right.Visibility == Visibility.Visible;
    public FrameworkElement LeftResizeGrip => leftGrip;
    public FrameworkElement RightResizeGrip => rightGrip;
    public EditorWorkspace()
    {
        for (var i = 0; i < 5; i++) ColumnDefinitions.Add(new() { Width = i == 2 ? new(1, GridUnitType.Star) : GridLength.Auto });
        Add(left, 0); Add(leftGrip, 1); Add(center, 2); Add(rightGrip, 3); Add(right, 4);
        Setup(leftGrip, true); Setup(rightGrip, false);
        SizeChanged += (_, _) => ArrangePanes(); Loaded += (_, _) => ArrangePanes();
    }
    private static ContentControl Host() => new() { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch, IsTabStop = false };
    private void Add(UIElement element, int column) { SetColumn(element, column); Children.Add(element); }
    private static void Validate(double value) { if (!double.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value)); }
    private void Setup(Thumb grip, bool isLeft)
    {
        grip.Width = 5; grip.IsTabStop = true; grip.Background = new SolidColorBrush(Microsoft.UI.Colors.LightGray);
        AutomationProperties.SetName(grip, isLeft ? "Resize palette pane" : "Resize properties pane");
        AutomationProperties.SetAutomationId(grip, isLeft ? "ResizeLeftPane" : "ResizeRightPane");
        // Thumb consumes pointer input for dragging. Also observe its handled press so keyboard resizing has a focused target.
        grip.AddHandler(PointerPressedEvent, new PointerEventHandler((_, _) => grip.Focus(FocusState.Pointer)), true);
        var initial = 0d;
        grip.DragStarted += (_, _) => initial = isLeft ? leftWidth : rightWidth;
        grip.DragDelta += (_, args) => { if (isLeft) LeftPaneWidth += args.HorizontalChange; else RightPaneWidth -= args.HorizontalChange; };
        grip.DragCompleted += (_, args) => { if (args.Canceled) { if (isLeft) LeftPaneWidth = initial; else RightPaneWidth = initial; } };
        grip.KeyDown += (_, args) =>
        {
            if (args.Key is not (VirtualKey.Left or VirtualKey.Right or VirtualKey.Home)) return;
            if (args.Key == VirtualKey.Home) { if (isLeft) LeftPaneWidth = 210; else RightPaneWidth = 280; }
            else
            {
                var delta = (args.Key == VirtualKey.Right ? 8 : -8) * (isLeft ? 1 : -1);
                if (isLeft) LeftPaneWidth += delta; else RightPaneWidth += delta;
            }
            args.Handled = true;
        };
    }
    public void ResetPanes() { leftWidth = 210; rightWidth = 280; showLeft = showRight = true; ArrangePanes(); }
    private void ArrangePanes()
    {
        var available = ActualWidth > 0 ? ActualWidth : 1440;
        var leftVisible = showLeft && available >= 760; var rightVisible = showRight && available >= 1050;
        var a = leftVisible ? leftWidth : 0; var b = rightVisible ? rightWidth : 0;
        var budget = Math.Max(0, available - 370 - (leftVisible ? 5 : 0) - (rightVisible ? 5 : 0));
        if (a + b > budget) { var factor = budget / (a + b); a *= factor; b *= factor; }
        ColumnDefinitions[0].Width = new(a); ColumnDefinitions[1].Width = new(leftVisible ? 5 : 0);
        ColumnDefinitions[3].Width = new(rightVisible ? 5 : 0); ColumnDefinitions[4].Width = new(b);
        left.Visibility = leftGrip.Visibility = leftVisible ? Visibility.Visible : Visibility.Collapsed;
        right.Visibility = rightGrip.Visibility = rightVisible ? Visibility.Visible : Visibility.Collapsed;
        PaneLayoutChanged?.Invoke(this, EventArgs.Empty);
    }
}
