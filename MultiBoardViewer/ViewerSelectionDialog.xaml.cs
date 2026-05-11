using System.Windows;

namespace MultiBoardViewer
{
    public partial class ViewerSelectionDialog : Window
    {
        public enum ViewerResult
        {
            NexusBV,
            OpenBoardView,

            Cancel
        }

        public ViewerResult Result { get; private set; }

        public ViewerSelectionDialog(string fileName)
        {
            InitializeComponent();
            FileNameText.Text = $"Choose viewer for {fileName}";
            Result = ViewerResult.Cancel;
        }

        private void NexusBvButton_Click(object sender, RoutedEventArgs e)
        {
            Result = ViewerResult.NexusBV;
            DialogResult = true;
            Close();
        }

        private void OpenBoardViewButton_Click(object sender, RoutedEventArgs e)
        {
            Result = ViewerResult.OpenBoardView;
            DialogResult = true;
            Close();
        }


    }
}