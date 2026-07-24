using System.ComponentModel.Composition;
using System.Windows;

namespace Crepusculum.NINA.SmartPlugControl.SmartPlugControlDockables {
    [Export(typeof(ResourceDictionary))]
    public partial class PlugControlDockableTemplates : ResourceDictionary {
        public PlugControlDockableTemplates() {
            InitializeComponent();
        }
    }
}
