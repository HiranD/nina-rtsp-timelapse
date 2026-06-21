using System.ComponentModel.Composition;
using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;

namespace NINA.RtspTimelapse.Plugin {

    /// <summary>
    /// Exporting the ResourceDictionary via MEF lets N.I.N.A. merge the options
    /// DataTemplate into its resources.
    /// </summary>
    [Export(typeof(ResourceDictionary))]
    public partial class Options : ResourceDictionary {
        public Options() {
            InitializeComponent();
        }

        // WPF Hyperlinks don't navigate on their own - open the URL in the default browser.
        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e) {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
            e.Handled = true;
        }
    }
}
