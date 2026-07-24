using Crepusculum.NINA.SmartPlugControl.Properties;
using Crepusculum.NINA.SmartPlugControl.SmartPlugControlCloud;
using NINA.Core.Utility;
using NINA.Plugin;
using NINA.Plugin.Interfaces;
using NINA.Profile.Interfaces;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Crepusculum.NINA.SmartPlugControl {
    /// <summary>
    /// Exports the IPluginManifest interface used for the general plugin information and options.
    /// PluginBase populates the manifest metadata from the AssemblyInfo attributes.
    /// </summary>
    [Export(typeof(IPluginManifest))]
    public class SmartPlugControl : PluginBase, INotifyPropertyChanged {
        [ImportingConstructor]
        public SmartPlugControl(IProfileService profileService) {
            if (Settings.Default.UpdateSettings) {
                Settings.Default.Upgrade();
                Settings.Default.UpdateSettings = false;
                CoreUtil.SaveSettings(Settings.Default);
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

        public override Task Teardown() {
            return base.Teardown();
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void RaisePropertyChanged([CallerMemberName] string propertyName = null) {
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
