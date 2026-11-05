using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace RaysLoader.Controls;

/// <summary>A faint square grid drawn behind page content.</summary>
public sealed class BlueprintGrid : Canvas
{
    public static readonly DependencyProperty LineBrushProperty = DependencyProperty.Register(
        nameof(LineBrush), typeof(Brush), typeof(BlueprintGrid), new PropertyMetadata(null, (sender, _) => ((BlueprintGrid)sender).Draw()));

    public static readonly DependencyProperty CellSizeProperty = DependencyProperty.Register(
        nameof(CellSize), typeof(double), typeof(BlueprintGrid), new PropertyMetadata(48d, (sender, _) => ((BlueprintGrid)sender).Draw()));

    public BlueprintGrid()
    {
        IsHitTestVisible = false;
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw);
        SizeChanged += (_, _) => Draw();
    }

    public Brush? LineBrush { get => (Brush?)GetValue(LineBrushProperty); set => SetValue(LineBrushProperty, value); }
    public double CellSize { get => (double)GetValue(CellSizeProperty); set => SetValue(CellSizeProperty, value); }

    private void Draw()
    {
        Children.Clear();
        var size = Math.Max(8, CellSize);
        if (ActualWidth <= 0 || ActualHeight <= 0 || LineBrush is null) return;
        for (var x = size; x < ActualWidth; x += size)
            Add(new Rectangle { Width = 1, Height = ActualHeight, Fill = LineBrush }, x, 0);
        for (var y = size; y < ActualHeight; y += size)
            Add(new Rectangle { Width = ActualWidth, Height = 1, Fill = LineBrush }, 0, y);
    }

    private void Add(UIElement line, double left, double top)
    {
        SetLeft(line, left);
        SetTop(line, top);
        Children.Add(line);
    }
}
