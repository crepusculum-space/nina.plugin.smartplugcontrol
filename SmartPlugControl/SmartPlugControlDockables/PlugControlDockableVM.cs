using Settings = Crepusculum.NINA.SmartPlugControl.Properties.Settings;
using Crepusculum.NINA.SmartPlugControl.SmartPlugControlServices;
using CommunityToolkit.Mvvm.Input;
using NINA.Core.Utility;
using NINA.Core.Utility.Notification;
using NINA.Equipment.Interfaces.ViewModel;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.ViewModel;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Crepusculum.NINA.SmartPlugControl.SmartPlugControlDockables {
    /// <summary>
    /// The plugin's equipment page: a live table of every discovered plug/socket plus global actions.
    /// Polls IPlugRegistryService in the background unconditionally - not gated on whether the panel
    /// is currently visible, since sequencer triggers/conditions depend on the same registry staying
    /// fresh even while the equipment page is hidden.
    /// </summary>
    [Export(typeof(IDockableVM))]
    public class PlugControlDockableVM : DockableVM {
        private readonly IPlugRegistryService registry;
        private readonly CancellationTokenSource pollCts = new CancellationTokenSource();

        [ImportingConstructor]
        public PlugControlDockableVM(IProfileService profileService, IPlugRegistryService registry) : base(profileService) {
            this.registry = registry;

            Title = "Smart Plug Control";
            IsVisible = true;
            try {
                var dict = new ResourceDictionary {
                    Source = new Uri("Crepusculum.NINA.SmartPlugControl;component/SmartPlugControlDockables/PlugControlDockableTemplates.xaml", UriKind.Relative)
                };
                var icon = (GeometryGroup)dict["Crepusculum.NINA.SmartPlugControl_PlugIconSVG"];
                // GeometryGroup is a Freezable/DependencyObject - it's built here in the VM's
                // constructor, which MEF may run on a composition thread rather than the UI thread.
                // Freezing makes it thread-independent; without this, WPF throws "Must create
                // DependencySource on same Thread as the DependencyObject" the first time the panel
                // is actually rendered on the UI thread.
                if (icon.CanFreeze) {
                    icon.Freeze();
                }
                ImageGeometry = icon;
            } catch (Exception) {
                // Non-fatal - the panel still works without a custom tab icon.
            }

            // Each action updates the visible rows itself right after the registry call succeeds,
            // instead of waiting for the next poll tick to pick up the change - matches what
            // PlugRowViewModel.ToggleOnOffAsync/ToggleLedAsync already do for a single plug. Mirrors
            // the registry's own skip rules (TurnOffAllAsync skips protected plugs; LED actions only
            // apply to plugs that actually support LED control) so the optimistic UI update never
            // shows a state the registry didn't actually set.
            TurnOnAllCommand = new AsyncRelayCommand(() => RunGlobalActionAsync(async t => {
                await registry.TurnOnAllAsync(t);
                foreach (var row in Plugs) {
                    row.SetIsOnLocally(true);
                }
            }, "turn on all plugs"));
            TurnOffAllCommand = new AsyncRelayCommand(() => RunGlobalActionAsync(async t => {
                await registry.TurnOffAllAsync(t);
                foreach (var row in Plugs.Where(p => !p.IsProtected)) {
                    row.SetIsOnLocally(false);
                }
            }, "turn off all plugs"));
            LedsOnCommand = new AsyncRelayCommand(() => RunGlobalActionAsync(async t => {
                await registry.SetAllLedsAsync(true, t);
                foreach (var row in Plugs.Where(p => p.SupportsLed)) {
                    row.SetIsLedOnLocally(true);
                }
            }, "turn on all LEDs"));
            LedsOffCommand = new AsyncRelayCommand(() => RunGlobalActionAsync(async t => {
                await registry.SetAllLedsAsync(false, t);
                foreach (var row in Plugs.Where(p => p.SupportsLed)) {
                    row.SetIsLedOnLocally(false);
                }
            }, "turn off all LEDs"));

            _ = PollLoopAsync(pollCts.Token);
        }

        public ObservableCollection<PlugRowViewModel> Plugs { get; } = new ObservableCollection<PlugRowViewModel>();

        public ICommand TurnOnAllCommand { get; }
        public ICommand TurnOffAllCommand { get; }
        public ICommand LedsOnCommand { get; }
        public ICommand LedsOffCommand { get; }

        /// <summary>Sum of every currently-known power reading - no threshold/progress bar yet, that needs the settings page (later phase).</summary>
        public double TotalWatts => Plugs.Where(p => p.HasPower).Sum(p => p.Watts ?? 0);

        private async Task RunGlobalActionAsync(Func<CancellationToken, Task> action, string description) {
            try {
                await action(pollCts.Token);
            } catch (Exception ex) {
                Logger.Error($"Failed to {description}", ex);
                Notification.ShowError($"Failed to {description}: {ex.Message}");
            }
        }

        private async Task PollLoopAsync(CancellationToken token) {
            while (!token.IsCancellationRequested) {
                try {
                    await RefreshTickAsync(token);
                } catch (OperationCanceledException) {
                    break;
                }

                try {
                    // Read fresh each cycle so a change in Options takes effect on the next tick,
                    // without requiring a NINA restart.
                    var interval = TimeSpan.FromSeconds(Math.Max(1, Settings.Default.RefreshIntervalSeconds));
                    await Task.Delay(interval, token);
                } catch (OperationCanceledException) {
                    break;
                }
            }
        }

        /// <summary>
        /// Refreshes the registry (and, if the dock happens to be visible, syncs the UI grid)
        /// exactly once. Deliberately NOT gated on <see cref="DockableVM.IsVisible"/> - sequencer
        /// triggers/conditions (PlugStateChangedTrigger, ConsumptionThresholdTrigger, etc.) read live
        /// state from the same IPlugRegistryService this polls, regardless of whether the equipment
        /// page happens to be open. Gating the refresh on dock visibility previously meant hiding the
        /// panel silently froze that data - a sequencer trigger running against a hidden dock would
        /// keep evaluating stale cached state instead of detecting real changes. Internal (not
        /// private) so SmartPlugControlTests can exercise a single tick without waiting on the full
        /// poll loop's delay.
        /// </summary>
        internal async Task RefreshTickAsync(CancellationToken token) {
            try {
                // isBackgroundPoll: true - after a login failure, this automatic loop backs off for
                // BackgroundLoginRetryCooldown before retrying on its own (see PlugRegistryService);
                // an explicit "Refresh Plug List" click always retries immediately regardless.
                await registry.RefreshAsync(isBackgroundPoll: true, token);
                SyncPlugsToUi();
            } catch (OperationCanceledException) {
                throw;
            } catch (Exception ex) {
                Logger.Error("Smart Plug Control refresh failed", ex);
                Notification.ShowError($"Smart Plug Control refresh failed: {ex.Message}");
            }
        }

        private void SyncPlugsToUi() {
            var latest = registry.Plugs;

            void Apply() {
                var latestIds = latest.Select(p => p.PlugId).ToHashSet();

                for (int i = Plugs.Count - 1; i >= 0; i--) {
                    if (!latestIds.Contains(Plugs[i].PlugId)) {
                        Plugs.RemoveAt(i);
                    }
                }

                foreach (var plugViewModel in latest) {
                    var existingRow = Plugs.FirstOrDefault(p => p.PlugId == plugViewModel.PlugId);
                    if (existingRow != null) {
                        existingRow.UpdateFrom(plugViewModel);
                    } else {
                        Plugs.Add(new PlugRowViewModel(plugViewModel, registry));
                    }
                }

                RaisePropertyChanged(nameof(TotalWatts));
            }

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess()) {
                Apply();
            } else {
                dispatcher.Invoke(Apply);
            }
        }
    }
}
