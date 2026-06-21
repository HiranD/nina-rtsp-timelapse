using System.ComponentModel.Composition;
using System.Windows;

namespace NINA.RtspTimelapse.Plugin.Dockable {

    [Export(typeof(ResourceDictionary))]
    public partial class DockableTemplates : ResourceDictionary {
        public DockableTemplates() {
            InitializeComponent();
        }
    }
}
