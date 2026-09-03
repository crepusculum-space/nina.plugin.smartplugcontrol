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

        // Deliberately manual-only, not kept live in sync with the equipment page's own background
        // poll - NINA composes IPluginManifest (this class) and IDockableVM/ISequenceItem (the
        // equipment page, sequencer items) via two separate MEF CompositionContainers
        // (PluginLoader.LoadPlugin vs. PluginLoader.Compose, see CLAUDE.md), so this class actually
        // gets its own separate IPlugRegistryService instance - subscribing to registry.PlugsUpdated
        // here would only ever fire from this instance's own manual refreshes, never from the
        // equipment page's, so it wouldn't accomplish anything a plain post-refresh sync doesn't
        // already do. A real fix would mean this page polling TP-Link's cloud API on its own too,
        // doubling the login/poll traffic for a cosmetic nicety - not worth it, especially with a real
        // account lockout already on record from over-polling (see CLAUDE.md). Selecting which plugs
        // to manage is a rare, deliberate action anyway (a remote-observatory user isn't expected to
        // reshuffle this list often), so requiring an explicit click here is a fine tradeoff.
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
                // Checked proactively (not just wrapped in try/catch) because WPF silently swallows
                // exceptions thrown from a TwoWay-bound property setter by default (no
                // ValidatesOnExceptions on this binding) - without this check, a DPAPI failure here
                // (e.g. a temporary/roaming Windows profile with no usable key material) would leave
                // the password permanently unsaved with zero indication anything went wrong: no
                // exception surfaces, nothing reaches the log, and every later plug refresh just
                // silently no-ops since the credentials look "not yet configured" instead of "failed
                // to save". See CLAUDE.md for the real report this was written for.
                // Diagnostic only (length, never content) - confirms whether this setter is even
                // being reached at all, and with what, when tracking down a report of the password
                // silently never saving.
                Logger.Debug($"SmartPlugControl: TpLinkPassword setter called (length={value?.Length ?? 0}).");

                if (!SecureCredentialStore.IsAvailable()) {
                    Logger.Error("Can't save TP-Link password: Windows DPAPI (credential encryption) failed a round-trip test on this system.");
                    Notification.ShowError("Can't save the TP-Link password: Windows' credential encryption isn't working on this system. This usually means your Windows user profile is temporary, roaming, or otherwise unable to store encryption keys. Smart Plug Control can't function without it - see NINA's log for details, or contact the plugin author.");
                    return;
                }
                try {
                    Settings.Default.TpLinkPasswordProtected = SecureCredentialStore.Protect(value ?? string.Empty);
                    CoreUtil.SaveSettings(Settings.Default);
                    RaisePropertyChanged();
                } catch (System.Exception ex) {
                    Logger.Error("Failed to encrypt/save the TP-Link password", ex);
                    Notification.ShowError($"Failed to save the TP-Link password: {ex.Message}");
                }
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
