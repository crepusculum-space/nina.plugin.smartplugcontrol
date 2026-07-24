using System.ComponentModel.Composition;
using System.Windows;

namespace Crepusculum.NINA.SmartPlugControl.SmartPlugControlSequenceItems {
    [Export(typeof(ResourceDictionary))]
    public partial class SmartPlugControlSequenceItemsTemplates : ResourceDictionary {
        public SmartPlugControlSequenceItemsTemplates() {
            InitializeComponent();
        }
    }
}
