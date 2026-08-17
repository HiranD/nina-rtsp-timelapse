using System.ComponentModel.Composition;
using System.Windows;

namespace NINA.RtspTimelapse.Plugin.Triggers {

    [Export(typeof(ResourceDictionary))]
    public partial class TriggerTemplates : ResourceDictionary {
        public TriggerTemplates() {
            InitializeComponent();
        }
    }
}
