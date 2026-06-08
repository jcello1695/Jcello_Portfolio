using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace QSightClient.Pages
{
    public sealed partial class LogsPage : Page
    {
        public LogsPage()
        {
            InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            //LogsList.ItemsSource = null;
            //LogsList.ItemsSource = App.Logs.Logs;

            App.Logs.LoadFromDisk();
            LogsList.ItemsSource = App.Logs.Logs;
        }

        private void LogsList_ItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is QSightClient.Models.ScanLog log)
            {
                Frame.Navigate(typeof(LogDetailPage), log);
            }
        }
    }
}