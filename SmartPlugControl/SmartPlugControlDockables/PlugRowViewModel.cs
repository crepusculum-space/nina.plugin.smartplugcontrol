using Crepusculum.NINA.SmartPlugControl.SmartPlugControlCloud;
using Crepusculum.NINA.SmartPlugControl.SmartPlugControlServices;
using CommunityToolkit.Mvvm.Input;
using NINA.Core.MyMessageBox;
using NINA.Core.Utility;
using NINA.Core.Utility.Notification;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace Crepusculum.NINA.SmartPlugControl.SmartPlugControlDockables {
    /// <summary>
    /// UI-facing wrapper around a plain <see cref="PlugViewModel"/> snapshot: exposes bindable
    /// properties and the per-row commands (on/off, LED, protected flag, equipment name edit) that
    /// call back into <see cref="IPlugRegistryService"/>.
    /// </summary>
    public class PlugRowViewModel : BaseINPC {
        private readonly IPlugRegistryService registry;
        private PlugViewModel model;

        public PlugRowViewModel(PlugViewModel model, IPlugRegistryService registry) {
            this.model = model;
            this.registry = registry;
            ToggleOnOffCommand = new AsyncRelayCommand(ToggleOnOffAsync);
            ToggleLedCommand = new AsyncRelayCommand(ToggleLedAsync);
            ToggleProtectedCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(ToggleProtected);
        }

        /// <summary>Called by the parent VM after each refresh to swap in fresh live state without losing command wiring.</summary>
        public void UpdateFrom(PlugViewModel updated) {
            model = updated;
            RaiseAllPropertiesChanged();
        }

        public string PlugId => model.PlugId;
        public string Alias => model.Alias;
        public PlugBrand Brand => model.Brand;

        public bool? IsOn => model.IsOn;
        public bool SupportsLed => model.SupportsLed;
        public bool? IsLedOn => model.IsLedOn;
        public bool IsProtected => model.IsProtected;

        public bool HasPower => model.LastPower != null;
        public double? Watts => model.LastPower?.Watts;

        public string EquipmentName {
            get => model.EquipmentName;
            set {
                if (model.EquipmentName == value) {
                    return;
                }
                model.EquipmentName = value ?? string.Empty;
                registry.SetEquipmentName(PlugId, model.EquipmentName);
                RaisePropertyChanged();
            }
        }

        public ICommand ToggleOnOffCommand { get; }
        public ICommand ToggleLedCommand { get; }
        public ICommand ToggleProtectedCommand { get; }

        private async Task ToggleOnOffAsync() {
            bool turningOff = model.IsOn == true;

            if (turningOff && IsProtected && !ConfirmProtectedShutdown()) {
                return;
            }

            try {
                if (turningOff) {
                    await registry.TurnOffAsync(PlugId);
                } else {
                    await registry.TurnOnAsync(PlugId);
                }
                model.IsOn = !turningOff;
                RaisePropertyChanged(nameof(IsOn));
            } catch (System.Exception ex) {
                Logger.Error($"Failed to toggle '{Alias}'", ex);
                Notification.ShowError($"'{Alias}': {ex.Message}");
            }
        }

        private async Task ToggleLedAsync() {
            bool turningOn = model.IsLedOn != true;
            try {
                await registry.SetLedAsync(PlugId, turningOn);
                model.IsLedOn = turningOn;
                RaisePropertyChanged(nameof(IsLedOn));
            } catch (System.Exception ex) {
                Logger.Error($"Failed to toggle '{Alias}' LED", ex);
                Notification.ShowError($"'{Alias}' LED: {ex.Message}");
            }
        }

        private void ToggleProtected() {
            model.IsProtected = !model.IsProtected;
            registry.SetProtected(PlugId, model.IsProtected);
            RaisePropertyChanged(nameof(IsProtected));
        }

        /// <summary>Two separate confirmations, per the plugin's security requirements for protected plugs.</summary>
        private bool ConfirmProtectedShutdown() {
            var first = MyMessageBox.Show(
                $"'{Alias}' is marked as protected equipment. Turn it off anyway?",
                "Protected plug",
                MessageBoxButton.YesNo,
                MessageBoxResult.No);
            if (first != MessageBoxResult.Yes) {
                return false;
            }

            var second = MyMessageBox.Show(
                $"This will cut power to '{Alias}'. Confirm again to proceed.",
                "Confirm again",
                MessageBoxButton.YesNo,
                MessageBoxResult.No);
            return second == MessageBoxResult.Yes;
        }
    }
}
