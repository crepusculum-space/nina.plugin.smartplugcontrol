namespace Crepusculum.NINA.SmartPlugControl.SmartPlugControlDrivers {
    /// <summary>
    /// A point-in-time electrical reading from a plug that supports energy monitoring.
    /// </summary>
    public class PlugPowerReading {
        public double Watts { get; set; }
        public double Volts { get; set; }
        public double Amps { get; set; }
    }
}
