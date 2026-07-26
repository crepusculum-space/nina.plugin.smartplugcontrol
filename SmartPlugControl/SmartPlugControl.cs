using Crepusculum.NINA.SmartPlugControl.Properties;
using Crepusculum.NINA.SmartPlugControl.SmartPlugControlCloud;
using Crepusculum.NINA.SmartPlugControl.SmartPlugControlServices;
using CommunityToolkit.Mvvm.Input;
using NINA.Core.Utility;
using NINA.Core.Utility.Notification;
using NINA.Plugin;
using NINA.Plugin.Interfaces;
using NINA.Profile.Interfaces;
using NINA.Sequencer.Interfaces.Mediator;
using System;
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
        private readonly ISequenceMediator sequenceMediator;

        [ImportingConstructor]
        public SmartPlugControl(IProfileService profileService, IPlugRegistryService registry, ISequenceMediator sequenceMediator) {
            this.registry = registry;
            this.sequenceMediator = sequenceMediator;
            if (Settings.Default.UpdateSettings) {
                Settings.Default.Upgrade();
                Settings.Default.UpdateSettings = false;
                CoreUtil.SaveSettings(Settings.Default);
            }
            RefreshPlugsCommand = new AsyncRelayCommand(RefreshPlugsAsync);

            sequenceMediator.SequenceStarting += SequenceMediator_SequenceStarting;
            sequenceMediator.SequenceFinished += SequenceMediator_SequenceFinished;
        }

        private async Task SequenceMediator_SequenceStarting(object sender, EventArgs e) {
            if (Settings.Default.LedsOffAtSequenceStart) {
                try {
                    await registry.SetAllLedsAsync(false);
                } catch (System.Exception ex) {
                    Notification.ShowError($"Failed to turn off LEDs at sequence start: {ex.Message}");
                }
            }
        }

        private async Task SequenceMediator_SequenceFinished(object sender, EventArgs e) {
            if (Settings.Default.LedsOnAtSequenceFinish) {
                try {
                    await registry.SetAllLedsAsync(true);
                } catch (System.Exception ex) {
                    Notification.ShowError($"Failed to turn on LEDs at sequence finish: {ex.Message}");
                }
            }
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

        // Minimal stand-in for the Phase 7 settings page (thresholds, refresh interval, LED
        // start/end-of-sequence options, and a proper masked password control) - just enough to
        // unblock testing the equipment page. The password field is a plain TextBox for now.
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

        public bool LedsOffAtSequenceStart {
            get => Settings.Default.LedsOffAtSequenceStart;
            set {
                Settings.Default.LedsOffAtSequenceStart = value;
                CoreUtil.SaveSettings(Settings.Default);
                RaisePropertyChanged();
            }
        }

        public bool LedsOnAtSequenceFinish {
            get => Settings.Default.LedsOnAtSequenceFinish;
            set {
                Settings.Default.LedsOnAtSequenceFinish = value;
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

        public override Task Teardown() {
            sequenceMediator.SequenceStarting -= SequenceMediator_SequenceStarting;
            sequenceMediator.SequenceFinished -= SequenceMediator_SequenceFinished;
            return base.Teardown();
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void RaisePropertyChanged([CallerMemberName] string propertyName = null) {
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
