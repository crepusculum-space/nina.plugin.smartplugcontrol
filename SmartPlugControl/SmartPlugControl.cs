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
                Logger.Error("Failed to refresh plug list", ex);
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

        public int RefreshIntervalSeconds {
            get => Settings.Default.RefreshIntervalSeconds;
            set {
                // A refresh interval of 0 or less would spin the poll loop pointlessly fast.
                Settings.Default.RefreshIntervalSeconds = value < 1 ? 1 : value;
                CoreUtil.SaveSettings(Settings.Default);
                RaisePropertyChanged();
            }
        }

        // Display-only: lets the equipment page show an estimated Amps figure (P = V / I) next to the
        // measured Watts reading. Unrelated to the per-plug consumption-alert thresholds (equipment
        // page) or the sequencer trigger/conditions, which work in Amps@12V DC, a different voltage
        // domain entirely (downstream DC equipment vs. this AC line voltage).
        public double LineVoltage {
            get => Settings.Default.LineVoltage;
            set {
                // 0V would divide by zero when PlugRowViewModel estimates Amps from this.
                Settings.Default.LineVoltage = value < 1 ? 1 : value;
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
