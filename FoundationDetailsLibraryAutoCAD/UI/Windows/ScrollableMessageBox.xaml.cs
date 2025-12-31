using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FoundationDetailer.UI.Windows
{
    public partial class ScrollableMessageBox
    {
        public static void Show(string message, string title = "Message")
        {
            // Create window
            Window window = new Window
            {
                Title = title,
                Width = 400,
                Height = 300,  // Window height constrains ScrollViewer
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.CanResize
            };

            // Create Grid with 2 rows
            Grid grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // scrollable content
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // button

            // ScrollViewer for the message
            ScrollViewer scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                Margin = new Thickness(10)
            };

            TextBlock textBlock = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap
            };
            scrollViewer.Content = textBlock;

            Grid.SetRow(scrollViewer, 0);
            grid.Children.Add(scrollViewer);

            // Close button
            Button closeButton = new Button
            {
                Content = "Close",
                Width = 80,
                Margin = new Thickness(10),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            closeButton.Click += (s, e) => window.Close();

            Grid.SetRow(closeButton, 1);
            grid.Children.Add(closeButton);

            window.Content = grid;

            window.ShowDialog();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Window.GetWindow(this)?.Close();
        }
    }
}
