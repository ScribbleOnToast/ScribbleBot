using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace ScribbleBot.UI.Behaviors
{
    public static class ScrollParentBehavior
    {
        public static readonly DependencyProperty FixScrollProperty =
            DependencyProperty.RegisterAttached(
                "FixScroll",
                typeof(bool),
                typeof(ScrollParentBehavior),
                new PropertyMetadata(false, OnFixScrollChanged));

        public static bool GetFixScroll(DependencyObject obj) => (bool)obj.GetValue(FixScrollProperty);
        public static void SetFixScroll(DependencyObject obj, bool value) => obj.SetValue(FixScrollProperty, value);

        private static void OnFixScrollChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is UIElement element)
            {
                if ((bool)e.NewValue)
                    element.PreviewMouseWheel += Element_PreviewMouseWheel;
                else
                    element.PreviewMouseWheel -= Element_PreviewMouseWheel;
            }
        }

        private static void Element_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is UIElement uiElement)
            {
                e.Handled = true;

                // Tunnel the mouse wheel event directly up to the parent ScrollViewer
                var parentScrollViewer = FindParentScrollViewer(uiElement);
                if (parentScrollViewer != null)
                {
                    var eventArg = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
                    {
                        RoutedEvent = UIElement.MouseWheelEvent,
                        Source = sender
                    };
                    parentScrollViewer.RaiseEvent(eventArg);
                }
            }
        }

        private static UIElement? FindParentScrollViewer(DependencyObject child)
        {
            var parent = VisualTreeHelper.GetParent(child);
            while (parent != null && parent is not System.Windows.Controls.ScrollViewer)
            {
                parent = VisualTreeHelper.GetParent(parent);
            }
            return parent as UIElement;
        }
    }
}
