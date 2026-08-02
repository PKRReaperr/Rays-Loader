using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace RaysLoader.Controls;

public sealed partial class TrimRangeSlider : UserControl
{
    private const double HandleWidth = 18;
    private const double TrackTop = 14;
    private const double SelectionTop = 12;
    private const double ThumbTop = 5;
    private bool _updatingValues;

    public TrimRangeSlider()
    {
        InitializeComponent();
        Loaded += (_, _) => UpdateVisuals();
    }

    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
        nameof(Minimum), typeof(double), typeof(TrimRangeSlider), new PropertyMetadata(0d, RangePropertyChanged));

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(double), typeof(TrimRangeSlider), new PropertyMetadata(100d, RangePropertyChanged));

    public static readonly DependencyProperty StartValueProperty = DependencyProperty.Register(
        nameof(StartValue), typeof(double), typeof(TrimRangeSlider), new PropertyMetadata(0d, RangePropertyChanged));

    public static readonly DependencyProperty EndValueProperty = DependencyProperty.Register(
        nameof(EndValue), typeof(double), typeof(TrimRangeSlider), new PropertyMetadata(100d, RangePropertyChanged));

    public double Minimum
    {
        get => (double)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public double StartValue
    {
        get => (double)GetValue(StartValueProperty);
        set => SetValue(StartValueProperty, value);
    }

    public double EndValue
    {
        get => (double)GetValue(EndValueProperty);
        set => SetValue(EndValueProperty, value);
    }

    private static void RangePropertyChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        ((TrimRangeSlider)sender).NormalizeValuesAndUpdate();
    }

    private void NormalizeValuesAndUpdate()
    {
        if (_updatingValues) return;
        _updatingValues = true;
        try
        {
            var minimum = double.IsFinite(Minimum) ? Minimum : 0;
            var maximum = double.IsFinite(Maximum) ? Math.Max(minimum, Maximum) : minimum;
            var start = Math.Clamp(double.IsFinite(StartValue) ? StartValue : minimum, minimum, maximum);
            var end = Math.Clamp(double.IsFinite(EndValue) ? EndValue : maximum, start, maximum);
            if (!Minimum.Equals(minimum)) SetValue(MinimumProperty, minimum);
            if (!Maximum.Equals(maximum)) SetValue(MaximumProperty, maximum);
            if (!StartValue.Equals(start)) SetValue(StartValueProperty, start);
            if (!EndValue.Equals(end)) SetValue(EndValueProperty, end);
        }
        finally
        {
            _updatingValues = false;
        }
        UpdateVisuals();
    }

    private void TimelineCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateVisuals();

    private void UpdateVisuals()
    {
        if (TimelineCanvas is null || TimelineCanvas.ActualWidth <= HandleWidth) return;
        var trackStart = HandleWidth / 2;
        var trackWidth = TimelineCanvas.ActualWidth - HandleWidth;
        var startCenter = ValueToPosition(StartValue);
        var endCenter = ValueToPosition(EndValue);

        Canvas.SetLeft(Track, trackStart);
        Canvas.SetTop(Track, TrackTop);
        Track.Width = trackWidth;

        Canvas.SetLeft(BeforeSelection, trackStart);
        Canvas.SetTop(BeforeSelection, TrackTop + 1);
        BeforeSelection.Width = Math.Max(0, startCenter - trackStart);

        Canvas.SetLeft(AfterSelection, endCenter);
        Canvas.SetTop(AfterSelection, TrackTop + 1);
        AfterSelection.Width = Math.Max(0, trackStart + trackWidth - endCenter);

        Canvas.SetLeft(Selection, startCenter);
        Canvas.SetTop(Selection, SelectionTop);
        Selection.Width = Math.Max(0, endCenter - startCenter);

        Canvas.SetLeft(StartThumb, startCenter - (HandleWidth / 2));
        Canvas.SetTop(StartThumb, ThumbTop);
        Canvas.SetLeft(EndThumb, endCenter - (HandleWidth / 2));
        Canvas.SetTop(EndThumb, ThumbTop);
    }

    private double ValueToPosition(double value)
    {
        var width = Math.Max(0, TimelineCanvas.ActualWidth - HandleWidth);
        var span = Maximum - Minimum;
        var ratio = span <= 0 ? 0 : (value - Minimum) / span;
        return (HandleWidth / 2) + (Math.Clamp(ratio, 0, 1) * width);
    }

    private double PositionToValue(double position)
    {
        var width = Math.Max(0, TimelineCanvas.ActualWidth - HandleWidth);
        if (width <= 0 || Maximum <= Minimum) return Minimum;
        var ratio = (position - (HandleWidth / 2)) / width;
        return Minimum + (Math.Clamp(ratio, 0, 1) * (Maximum - Minimum));
    }

    private void StartThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        var center = ValueToPosition(StartValue) + e.HorizontalChange;
        StartValue = Math.Min(PositionToValue(center), EndValue);
    }

    private void EndThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        var center = ValueToPosition(EndValue) + e.HorizontalChange;
        EndValue = Math.Max(PositionToValue(center), StartValue);
    }

    private void TimelineCanvas_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (!IsEnabled) return;
        var value = PositionToValue(e.GetPosition(TimelineCanvas).X);
        if (Math.Abs(value - StartValue) <= Math.Abs(value - EndValue)) StartValue = Math.Min(value, EndValue);
        else EndValue = Math.Max(value, StartValue);
    }

    private void StartThumb_KeyDown(object sender, KeyRoutedEventArgs e) => AdjustWithKeyboard(true, e);
    private void EndThumb_KeyDown(object sender, KeyRoutedEventArgs e) => AdjustWithKeyboard(false, e);

    private void AdjustWithKeyboard(bool startHandle, KeyRoutedEventArgs e)
    {
        if (e.Key is not VirtualKey.Left and not VirtualKey.Right) return;
        var step = Math.Max(0.1, (Maximum - Minimum) / 500);
        var delta = e.Key is VirtualKey.Left ? -step : step;
        if (startHandle) StartValue = Math.Clamp(StartValue + delta, Minimum, EndValue);
        else EndValue = Math.Clamp(EndValue + delta, StartValue, Maximum);
        e.Handled = true;
    }
}
