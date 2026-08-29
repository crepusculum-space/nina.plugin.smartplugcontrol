using Crepusculum.NINA.SmartPlugControl.SmartPlugControlDockables;
using SmartPlugControlTests.Fakes;
using System.Threading.Tasks;
using Xunit;

namespace SmartPlugControlTests {
    /// <summary>
    /// Regression coverage for a real bug found in code review (isbeorn, PR #589): the equipment
    /// page's poll loop used to only refresh IPlugRegistryService while the dock was visible, so
    /// sequencer triggers/conditions (which read the same registry) would silently keep evaluating
    /// stale data whenever the user hid the equipment page. Fixed by making the refresh unconditional.
    /// </summary>
    public class PlugControlDockableVMTests {
        [Fact]
        public async Task RefreshTickAsync_RefreshesRegistry_EvenWhenDockIsHidden() {
            var registry = new FakePlugRegistryService();
            var vm = new PlugControlDockableVM(new FakeProfileService(), registry);
            vm.IsVisible = false;

            await vm.RefreshTickAsync(default);

            Assert.True(registry.RefreshCallCount > 0, "RefreshAsync should be called regardless of dock visibility.");
        }

        [Fact]
        public async Task RefreshTickAsync_RefreshesRegistry_WhenDockIsVisible() {
            var registry = new FakePlugRegistryService();
            var vm = new PlugControlDockableVM(new FakeProfileService(), registry);
            vm.IsVisible = true;

            await vm.RefreshTickAsync(default);

            Assert.True(registry.RefreshCallCount > 0);
        }
    }
}
