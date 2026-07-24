using System;
using System.Windows;
using System.Windows.Media.Animation;

namespace ScribbleBot.Behaviors;

public class GridLengthAnimation : AnimationTimeline
{
    public static readonly DependencyProperty FromProperty =
        DependencyProperty.Register("From", typeof(GridLength), typeof(GridLengthAnimation));

    public static readonly DependencyProperty ToProperty =
        DependencyProperty.Register("To", typeof(GridLength), typeof(GridLengthAnimation));

    public GridLength From
    {
        get => (GridLength)GetValue(FromProperty);
        set => SetValue(FromProperty, value);
    }

    public GridLength To
    {
        get => (GridLength)GetValue(ToProperty);
        set => SetValue(ToProperty, value);
    }

    public override Type TargetPropertyType => typeof(GridLength);

    protected override Freezable CreateInstanceCore() => new GridLengthAnimation();

    public override object GetCurrentValue(object defaultOriginValue, object defaultDestinationValue, AnimationClock clock)
    {
        double fromVal = From.Value;
        double toVal = To.Value;

        if (clock.CurrentProgress == null) return From;

        double progress = clock.CurrentProgress.Value;

        if (EasingFunction != null)
        {
            progress = EasingFunction.Ease(progress);
        }

        return new GridLength(fromVal + (toVal - fromVal) * progress, GridUnitType.Pixel);
    }

    public IEasingFunction? EasingFunction { get; set; }
}

