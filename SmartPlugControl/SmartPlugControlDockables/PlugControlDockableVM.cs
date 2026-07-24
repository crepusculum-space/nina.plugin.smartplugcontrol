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
    /// Polls IPlugRegistryService in the background, only while the panel is visible.
    /// </summary>
    [Export(typeof(IDockableVM))]
    public class PlugControlDockableVM : DockableVM {
        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(10);

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

            TurnOnAllCommand = new AsyncRelayCommand(() => RunGlobalActionAsync(registry.TurnOnAllAsync, "turn on all plugs"));
            TurnOffAllCommand = new AsyncRelayCommand(() => RunGlobalActionAsync(registry.TurnOffAllAsync, "turn off all plugs"));
            LedsOnCommand = new AsyncRelayCommand(() => RunGlobalActionAsync(t => registry.SetAllLedsAsync(true, t), "turn on all LEDs"));
            LedsOffCommand = new AsyncRelayCommand(() => RunGlobalActionAsync(t => registry.SetAllLedsAsync(false, t), "turn off all LEDs"));

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
                Notification.ShowError($"Failed to {description}: {ex.Message}");
            }
        }

        private async Task PollLoopAsync(CancellationToken token) {
            while (!token.IsCancellationRequested) {
                if (IsVisible) {
                    try {
                        await registry.RefreshAsync(token);
                        SyncPlugsToUi();
                    } catch (OperationCanceledException) {
                        break;
                    } catch (Exception ex) {
                        Notification.ShowError($"Smart Plug Control refresh failed: {ex.Message}");
                    }
                }

                try {
                    await Task.Delay(PollInterval, token);
                } catch (OperationCanceledException) {
                    break;
                }
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
