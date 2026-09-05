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

        private string pendingUsername;
        private string pendingPassword;

        [ImportingConstructor]
        public SmartPlugControl(IProfileService profileService, IPlugRegistryService registry) {
            this.registry = registry;
            if (Settings.Default.UpdateSettings) {
                Settings.Default.Upgrade();
                Settings.Default.UpdateSettings = false;
                CoreUtil.SaveSettings(Settings.Default);
            }
            pendingUsername = Settings.Default.TpLinkUsername;
            pendingPassword = SecureCredentialStore.Unprotect(Settings.Default.TpLinkPasswordProtected);
            RefreshPlugsCommand = new AsyncRelayCommand(RefreshPlugsAsync);
            SaveCredentialsCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(SaveCredentials);
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

        public ICommand SaveCredentialsCommand { get; }

        // Deliberately buffered locally, not written to Settings.Default until SaveCredentials runs.
        // These setters used to save on every keystroke (PasswordBoxAssistant fires PasswordChanged
        // per character), which meant every partial, incomplete value typed while changing a password
        // got written straight to the shared settings the equipment page's background poll reads. A
        // poll tick sees ANY credential change as "new, worth an immediate retry" (see
        // PlugRegistryService's cooldown-reset logic) - so simply typing a new password could fire a
        // rapid string of failed login attempts against TP-Link's cloud, one per keystroke that
        // happened to still be visible at the next ~10s poll tick. This got a real user's TP-Link
        // account locked - the exact login-retry-storm CLAUDE.md already warns about, just triggered by
        // normal typing this time instead of a bug in the retry logic itself. Nothing is persisted, and
        // the equipment page's poll loop never sees anything change, until the user explicitly clicks
        // Save.
        public string TpLinkUsername {
            get => pendingUsername;
            set {
                pendingUsername = value ?? string.Empty;
                RaisePropertyChanged();
            }
        }

        public string TpLinkPassword {
            get => pendingPassword;
            set {
                pendingPassword = value ?? string.Empty;
                RaisePropertyChanged();
            }
        }

        private void SaveCredentials() {
            // Checked proactively (not just wrapped in try/catch) because WPF silently swallows
            // exceptions thrown from a bound command by default - without this check, a DPAPI failure
            // here (e.g. a temporary/roaming Windows profile with no usable key material) would leave
            // the password permanently unsaved with zero indication anything went wrong. See CLAUDE.md
            // for the real report this was originally written for.
            if (!SecureCredentialStore.IsAvailable()) {
                Logger.Error("Can't save TP-Link password: Windows DPAPI (credential encryption) failed a round-trip test on this system.");
                Notification.ShowError("Can't save the TP-Link password: Windows' credential encryption isn't working on this system. This usually means your Windows user profile is temporary, roaming, or otherwise unable to store encryption keys. Smart Plug Control can't function without it - see NINA's log for details, or contact the plugin author.");
                return;
            }
            try {
                Settings.Default.TpLinkUsername = pendingUsername ?? string.Empty;
                Settings.Default.TpLinkPasswordProtected = SecureCredentialStore.Protect(pendingPassword ?? string.Empty);
                CoreUtil.SaveSettings(Settings.Default);
                Notification.ShowSuccess("TP-Link credentials saved.");
            } catch (System.Exception ex) {
                Logger.Error("Failed to save TP-Link credentials", ex);
                Notification.ShowError($"Failed to save TP-Link credentials: {ex.Message}");
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

        // The right value depends on the specific power strip model/electrical load - not something
        // that can be guessed correctly for every user, so it's exposed here rather than hardcoded.
        public int PowerStripCommandDelayMs {
            get => Settings.Default.PowerStripCommandDelayMs;
            set {
                Settings.Default.PowerStripCommandDelayMs = value < 0 ? 0 : value;
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
