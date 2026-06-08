using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using QSightClient.Models;

namespace QSightClient.Pages
{
    public sealed partial class LogDetailPage : Page
    {
        public LogDetailPage()
        {
            InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            if (e.Parameter is ScanLog log)
            {
                FileText.Text = log.FilePath;
                TimeText.Text = log.ScanTime.ToString();
                ResultText.Text = log.Result;
            }
        }

        //private void BackButton_Click(object sender, RoutedEventArgs e)
        //{
        //    if (Frame.CanGoBack)
        //    {
        //        Frame.GoBack();
        //    }
        //}
    }
}