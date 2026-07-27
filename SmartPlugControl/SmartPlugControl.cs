using Crepusculum.NINA.SmartPlugControl.Properties;
using Crepusculum.NINA.SmartPlugControl.SmartPlugControlCloud;
using Crepusculum.NINA.SmartPlugControl.SmartPlugControlServices;
using CommunityToolkit.Mvvm.Input;
using NINA.Core.Utility;
using NINA.Core.Utility.Notification;
using NINA.Plugin;
using NINA.Plugin.Interfaces;
using NINA.Profile.Interfaces;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Crepusculum.NINA.SmartPlugControl {
    /// <summary>
    /// Exports the IPluginManifest interface used for the general plugin information and options.
    /// PluginBase populates the manifest metadata from the AssemblyInfo attributes.
    /// </summary>
    [Export(typeof(IPluginManifest))]
    public class SmartPlugControl : PluginBase, INotifyPropertyChanged {
        private readonly IPlugRegistryService registry;

        [ImportingConstructor]
        public SmartPlugControl(IProfileService profileService, IPlugRegistryService registry) {
            this.registry = registry;
            if (Settings.Default.UpdateSettings) {
                Settings.Default.Upgrade();
                Settings.Default.UpdateSettings = false;
                CoreUtil.SaveSettings(Settings.Default);
            }
            RefreshPlugsCommand = new AsyncRelayCommand(RefreshPlugsAsync);
        }

        /// <summary>Every plug on the account, with a toggle for whether it should show up in the equipment page/sequencer.</summary>
        public ObservableCollection<PlugVisibilityRowViewModel> AllPlugs { get; } = new ObservableCollection<PlugVisibilityRowViewModel>();

        public ICommand RefreshPlugsCommand { get; }

        private async Task RefreshPlugsAsync() {
            try {
                await registry.RefreshAsync();
                AllPlugs.Clear();
                foreach (var plug in registry.AllPlugs) {
                    AllPlugs.Add(new PlugVisibilityRowViewModel(plug, registry));
                }
            } catch (System.Exception ex) {
                Notification.ShowError($"Failed to refresh plug list: {ex.Message}");
            }
        }

        // Minimal stand-in for the Phase 7 settings page (thresholds, refresh interval, and a proper
        // masked password control) - just enough to unblock testing the equipment page. The password
        // field is a plain TextBox for now.
        public string TpLinkUsername {
            get => Settings.Default.TpLinkUsername;
            set {
                Settings.Default.TpLinkUsername = value ?? string.Empty;
                CoreUtil.SaveSettings(Settings.Default);
                RaisePropertyChanged();
            }
        }

        public string TpLinkPassword {
            get => SecureCredentialStore.Unprotect(Settings.Default.TpLinkPasswordProtected);
            set {
                Settings.Default.TpLinkPasswordProtected = SecureCredentialStore.Protect(value ?? string.Empty);
                CoreUtil.SaveSettings(Settings.Default);
                RaisePropertyChanged();
            }
        }

        public double MaxConsumptionThresholdWatts {
            get => Settings.Default.MaxConsumptionThresholdWatts;
            set {
                Settings.Default.MaxConsumptionThresholdWatts = value;
                CoreUtil.SaveSettings(Settings.Default);
                RaisePropertyChanged();
            }
        }

        public int PreventiveAlertPercent {
            get => Settings.Default.PreventiveAlertPercent;
            set {
                Settings.Default.PreventiveAlertPercent = value;
                CoreUtil.SaveSettings(Settings.Default);
                RaisePropertyChanged();
            }
        }

        // Most astro equipment (Pegasus Powerboxes, etc.) is rated in Amps at 12V DC, not Watts - but
        // a Kasa/Tapo plug only ever measures Watts on the AC side, upstream of the DC power supply.
        // This is a convenience calculator: entering an Amps rating here computes and overwrites
        // MaxConsumptionThresholdWatts, using PsuEfficiencyPercent to estimate the AC-side draw needed
        // to deliver that much DC power. It's always an estimate - the plugin has no way to measure a
        // specific supply's actual efficiency - so treat the resulting Watts value as approximate and
        // set PreventiveAlertPercent conservatively to leave margin for that uncertainty.
        public double MaxConsumptionThresholdAmps {
            get => Settings.Default.MaxConsumptionThresholdAmps;
            set {
                Settings.Default.MaxConsumptionThresholdAmps = value;
                CoreUtil.SaveSettings(Settings.Default);
                RaisePropertyChanged();
                RecomputeWattsFromAmps();
            }
        }

        public int PsuEfficiencyPercent {
            get => Settings.Default.PsuEfficiencyPercent;
            set {
                // 0% would divide by zero below; clamp to a sane minimum instead.
                Settings.Default.PsuEfficiencyPercent = value < 1 ? 1 : value;
                CoreUtil.SaveSettings(Settings.Default);
                RaisePropertyChanged();
                RecomputeWattsFromAmps();
            }
        }

        private const double DcSupplyVolts = 12.0;

        private void RecomputeWattsFromAmps() {
            double amps = Settings.Default.MaxConsumptionThresholdAmps;
            if (amps <= 0) {
                // Calculator not in use - leave a directly-entered Watts value alone.
                return;
            }
            double dcWatts = amps * DcSupplyVolts;
            double acWatts = dcWatts / (Settings.Default.PsuEfficiencyPercent / 100.0);
            MaxConsumptionThresholdWatts = acWatts;
        }

        public int RefreshIntervalSeconds {
            get => Settings.Default.RefreshIntervalSeconds;
            set {
                // A refresh interval of 0 or less would spin the poll loop pointlessly fast.
                Settings.Default.RefreshIntervalSeconds = value < 1 ? 1 : value;
                CoreUtil.SaveSettings(Settings.Default);
                RaisePropertyChanged();
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void RaisePropertyChanged([CallerMemberName] string propertyName = null) {
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
