using ScribbleBot.UI.Behaviors;
using ScribbleBot.ViewModels;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using ScribbleBot.Agents;
using System.IO;
using ScribbleBot.Services;
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

        private void OnPreviewDragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
            }
        }

        private async void OnTextBoxDrop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;

            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
            var vm = DataContext as MainViewModel;
            if (vm == null || files == null) return;

            foreach (var path in files)
            {
                // Offload parsing to ingestion service
                var attachment = await vm._fileIngestionService.ProcessFileAsync(path);
                vm.AttachedFiles.Add(attachment);
            }

            e.Handled = true;
        }
    }
}