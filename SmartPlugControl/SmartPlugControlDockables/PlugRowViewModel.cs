using Crepusculum.NINA.SmartPlugControl.SmartPlugControlCloud;
using Crepusculum.NINA.SmartPlugControl.SmartPlugControlServices;
using CommunityToolkit.Mvvm.Input;
using NINA.Core.MyMessageBox;
using NINA.Core.Utility;
using NINA.Core.Utility.Notification;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Settings = Crepusculum.NINA.SmartPlugControl.Properties.Settings;

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

        // NINA's shared CheckBox theme renders IsChecked=null (IsThreeState's "indeterminate" state)
        // identically to an unchecked/off box - confirmed on real hardware, a plug the registry can't
        // currently reach (isOn: null) looked exactly like a normal, confirmed-off plug, with no visual
        // difference at all. The template swaps the switch for a plain "Off Line" label instead of
        // relying on an indeterminate CheckBox visual that isn't actually distinguishable.
        public bool IsStateKnown => IsOn != null;
        public bool IsOffline => !IsStateKnown;

        public bool HasPower => model.LastPower != null;
        public double? Watts => model.LastPower?.Watts;

        // Estimated, not measured - the plug only ever reports AC-side Watts; Amps here is a display
        // convenience derived via P = V x I using the configured line voltage (Options), not an
        // actual current reading, and doesn't account for the power factor of the load's own supply.
        public string PowerDisplay {
            get {
                if (Watts == null) {
                    return null;
                }
                double amps = Watts.Value / Settings.Default.LineVoltage;
                return $"{Watts.Value:F1} W / {amps:F2} A";
            }
        }

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

        // Invariant properties of whatever's plugged in here (e.g. a Pegasus Powerbox rated 8A@12V) -
        // configured once here rather than per sequence item; the consumption trigger/loop conditions
        // read these via the plug's own PlugViewModel.MaxThresholdWatts. Only meaningful, and only
        // shown in the equipment page, for plugs that support energy monitoring at all.
        public bool SupportsPowerMonitoring => model.SupportsPowerMonitoring;

        public double MaxAmpsAt12V {
            get => model.MaxAmpsAt12V;
            set {
                if (model.MaxAmpsAt12V == value) {
                    return;
                }
                model.MaxAmpsAt12V = value < 0 ? 0 : value;
                registry.SetMaxAmpsAt12V(PlugId, model.MaxAmpsAt12V);
                RaisePropertyChanged();
            }
        }

        public int PsuEfficiencyPercent {
            get => model.PsuEfficiencyPercent;
            set {
                int clamped = value < 1 ? 1 : value;
                if (model.PsuEfficiencyPercent == clamped) {
                    return;
                }
                model.PsuEfficiencyPercent = clamped;
                registry.SetPsuEfficiencyPercent(PlugId, clamped);
                RaisePropertyChanged();
            }
        }

        public ICommand ToggleOnOffCommand { get; }
        public ICommand ToggleLedCommand { get; }
        public ICommand ToggleProtectedCommand { get; }

        /// <summary>Called by the equipment page right after a bulk on/off action succeeds, so the
        /// switch updates immediately instead of waiting for the next poll tick - mirrors what
        /// ToggleOnOffAsync already does for a single plug.</summary>
        public void SetIsOnLocally(bool isOn) {
            model.IsOn = isOn;
            RaisePropertyChanged(nameof(IsOn));
        }

        /// <summary>Same as SetIsOnLocally, for a bulk LED action.</summary>
        public void SetIsLedOnLocally(bool isLedOn) {
            model.IsLedOn = isLedOn;
            RaisePropertyChanged(nameof(IsLedOn));
        }

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
