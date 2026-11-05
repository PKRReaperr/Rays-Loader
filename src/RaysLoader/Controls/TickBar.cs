using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace RaysLoader.Controls;

public enum TickTone
{
    Idle,
    Active,
    Complete,
    Failed,
}

/// <summary>
/// A progress bar drawn as a row of vertical ticks. Lit ticks take the brush for
/// the current <see cref="Tone"/>; the rest use <see cref="TrackBrush"/>.
/// </summary>
public sealed class TickBar : Grid
{
    public static readonly DependencyProperty ValueProperty = Register(nameof(Value), typeof(double), 0d);
    public static readonly DependencyProperty TickCountProperty = Register(nameof(TickCount), typeof(int), 32);
    public static readonly DependencyProperty ToneProperty = Register(nameof(Tone), typeof(TickTone), TickTone.Active);
    public static readonly DependencyProperty ActiveBrushProperty = Register(nameof(ActiveBrush), typeof(Brush), null);
    public static readonly DependencyProperty CompleteBrushProperty = Register(nameof(CompleteBrush), typeof(Brush), null);
    public static readonly DependencyProperty IdleBrushProperty = Register(nameof(IdleBrush), typeof(Brush), null);
    public static readonly DependencyProperty TrackBrushProperty = Register(nameof(TrackBrush), typeof(Brush), null);

    private readonly List<Rectangle> _ticks = [];

    public TickBar()
    {
        ColumnSpacing = 2;
        IsHitTestVisible = false;
        AutomationProperties.SetAccessibilityView(this, AccessibilityView.Raw);
        Rebuild();
    }

    /// <summary>Progress from 0 to 100.</summary>
    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public int TickCount { get => (int)GetValue(TickCountProperty); set => SetValue(TickCountProperty, value); }
    public TickTone Tone { get => (TickTone)GetValue(ToneProperty); set => SetValue(ToneProperty, value); }
    public Brush? ActiveBrush { get => (Brush?)GetValue(ActiveBrushProperty); set => SetValue(ActiveBrushProperty, value); }
    public Brush? CompleteBrush { get => (Brush?)GetValue(CompleteBrushProperty); set => SetValue(CompleteBrushProperty, value); }
    public Brush? IdleBrush { get => (Brush?)GetValue(IdleBrushProperty); set => SetValue(IdleBrushProperty, value); }
    public Brush? TrackBrush { get => (Brush?)GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }

    private static DependencyProperty Register(string name, Type type, object? defaultValue) =>
        DependencyProperty.Register(name, type, typeof(TickBar), new PropertyMetadata(defaultValue, OnChanged));

    private static void OnChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var bar = (TickBar)sender;
        if (args.Property == TickCountProperty) bar.Rebuild();
        else bar.Paint();
    }

    private void Rebuild()
    {
        Children.Clear();
        ColumnDefinitions.Clear();
        _ticks.Clear();
        var count = Math.Clamp(TickCount, 1, 200);
        for (var index = 0; index < count; index++)
        {
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var tick = new Rectangle();
            SetColumn(tick, index);
            Children.Add(tick);
            _ticks.Add(tick);
        }
        Paint();
    }

    private void Paint()
    {
        var lit = (int)Math.Round(Math.Clamp(Value, 0, 100) / 100 * _ticks.Count, MidpointRounding.AwayFromZero);
        var on = Tone switch
        {
            TickTone.Active => ActiveBrush,
            TickTone.Complete => CompleteBrush,
            _ => IdleBrush,
        };
        for (var index = 0; index < _ticks.Count; index++)
            _ticks[index].Fill = index < lit ? on : TrackBrush;
    }
}
