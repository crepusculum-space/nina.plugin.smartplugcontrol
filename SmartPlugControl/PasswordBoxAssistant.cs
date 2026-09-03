using NINA.Core.Utility;
using System.Windows;
using System.Windows.Controls;

namespace Crepusculum.NINA.SmartPlugControl {
    // PasswordBox.Password is intentionally not a DependencyProperty (WPF avoids keeping plaintext
    // passwords in the binding/data-context graph), so it can't be bound directly like a TextBox.
    // This attached property bridges it to a normal MVVM binding while keeping the display masked.
    public static class PasswordBoxAssistant {
        // The registered default MUST be a value no real bound password can ever equal (null, not
        // string.Empty). WPF's dependency property system can skip invoking OnBoundPasswordChanged
        // entirely when the value pushed by the binding on first load already equals the registered
        // default - and for every first-time setup (no password saved yet), TpLinkPassword's getter
        // returns "" on the very first bind, which used to be exactly equal to this property's old
        // default (string.Empty). When that happened, PasswordChanged below was never subscribed at
        // all, so typing into the box did nothing - forever - for exactly the users who need this the
        // most. A real password is never null, so a null default guarantees the very first bind is
        // always seen as a genuine change, and the callback (hence the subscription) always fires.
        // Reported by a real user: entering credentials and refreshing did nothing, no error, no log
        // entry beyond "password not configured" - reproduced on two separate machines/NINA versions.
        public static readonly DependencyProperty BoundPassword = DependencyProperty.RegisterAttached(
            "BoundPassword", typeof(string), typeof(PasswordBoxAssistant), new PropertyMetadata(null, OnBoundPasswordChanged));

        private static readonly DependencyProperty UpdatingPassword = DependencyProperty.RegisterAttached(
            "UpdatingPassword", typeof(bool), typeof(PasswordBoxAssistant), new PropertyMetadata(false));

        public static void SetBoundPassword(DependencyObject dp, string value) => dp.SetValue(BoundPassword, value);
        public static string GetBoundPassword(DependencyObject dp) => (string)dp.GetValue(BoundPassword);

        private static bool GetUpdatingPassword(DependencyObject dp) => (bool)dp.GetValue(UpdatingPassword);
        private static void SetUpdatingPassword(DependencyObject dp, bool value) => dp.SetValue(UpdatingPassword, value);

        private static void OnBoundPasswordChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
            if (d is not PasswordBox box) {
                return;
            }

            // Diagnostic only (length, never content) - left in deliberately in case the null-default
            // fix above isn't the whole story; confirms at least that this callback (and therefore the
            // PasswordChanged subscription below) actually ran.
            Logger.Debug($"SmartPlugControl: PasswordBoxAssistant subscribing PasswordChanged (bound value length={((string)e.NewValue)?.Length.ToString() ?? "null"}).");

            box.PasswordChanged -= PasswordChanged;

            if (!GetUpdatingPassword(box)) {
                box.Password = (string)e.NewValue ?? string.Empty;
            }

            box.PasswordChanged += PasswordChanged;
        }

        private static void PasswordChanged(object sender, RoutedEventArgs e) {
            var box = (PasswordBox)sender;
            Logger.Debug($"SmartPlugControl: PasswordBox.PasswordChanged fired (length={box.Password?.Length ?? 0}).");
            SetUpdatingPassword(box, true);
            SetBoundPassword(box, box.Password);
            SetUpdatingPassword(box, false);
        }
    }
}
