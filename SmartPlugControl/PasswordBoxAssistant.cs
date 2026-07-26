using System.Windows;
using System.Windows.Controls;

namespace Crepusculum.NINA.SmartPlugControl {
    // PasswordBox.Password is intentionally not a DependencyProperty (WPF avoids keeping plaintext
    // passwords in the binding/data-context graph), so it can't be bound directly like a TextBox.
    // This attached property bridges it to a normal MVVM binding while keeping the display masked.
    public static class PasswordBoxAssistant {
        public static readonly DependencyProperty BoundPassword = DependencyProperty.RegisterAttached(
            "BoundPassword", typeof(string), typeof(PasswordBoxAssistant), new PropertyMetadata(string.Empty, OnBoundPasswordChanged));

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

            box.PasswordChanged -= PasswordChanged;

            if (!GetUpdatingPassword(box)) {
                box.Password = (string)e.NewValue ?? string.Empty;
            }

            box.PasswordChanged += PasswordChanged;
        }

        private static void PasswordChanged(object sender, RoutedEventArgs e) {
            var box = (PasswordBox)sender;
            SetUpdatingPassword(box, true);
            SetBoundPassword(box, box.Password);
            SetUpdatingPassword(box, false);
        }
    }
}
