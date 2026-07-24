using ScribbleBot.ViewModels;
using ScribbleBot.Behaviors;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace ScribbleBot
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            DataContextChanged += MainWindow_DataContextChanged;
        }
        private void MainWindow_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue is MainViewModel vm)
            {
                vm.State.PropertyChanged += State_PropertyChanged;
            }
        }

        private void State_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(AgentState.IsSidebarOpen))
            {
                if (sender is AgentState state)
                {
                    AnimateSidebar(state.IsSidebarOpen);
                }
            }
        }

        private void AnimateSidebar(bool open)
        {
            double targetWidth = open ? 260 : 0;
            double startWidth = SidebarColumn.ActualWidth;

            AnimationTimeline animation = new GridLengthAnimation
            {
                From = new GridLength(startWidth),
                To = new GridLength(targetWidth),
                Duration = TimeSpan.FromSeconds(0.35), // Smooth 350ms duration
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            };

            SidebarColumn.BeginAnimation(ColumnDefinition.WidthProperty, animation);
        }
    }
}