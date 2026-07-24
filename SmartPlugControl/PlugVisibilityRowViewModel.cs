using Crepusculum.NINA.SmartPlugControl.SmartPlugControlCloud;
using Crepusculum.NINA.SmartPlugControl.SmartPlugControlServices;
using NINA.Core.Utility;

namespace Crepusculum.NINA.SmartPlugControl {
    /// <summary>
    /// One row in the Options page's "which plugs should NINA see" management table. The TP-Link
    /// account may include plugs unrelated to the observatory (a home TV, a printer...) - this lets
    /// the user opt individual plugs out of the equipment page/sequencer entirely.
    /// </summary>
    public class PlugVisibilityRowViewModel : BaseINPC {
        private readonly IPlugRegistryService registry;
        private readonly PlugViewModel model;

        public PlugVisibilityRowViewModel(PlugViewModel model, IPlugRegistryService registry) {
            this.model = model;
            this.registry = registry;
        }

        public string PlugId => model.PlugId;
        public string Alias => model.Alias;
        public PlugBrand Brand => model.Brand;

        public bool IsVisibleInNina {
            get => model.IsVisibleInNina;
            set {
                if (model.IsVisibleInNina == value) {
                    return;
                }
                model.IsVisibleInNina = value;
                registry.SetVisibleInNina(PlugId, value);
                RaisePropertyChanged();
            }
        }
    }
}
