using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

// [MANDATORY] The following GUID is used as a unique identifier of the plugin. Generate a fresh one for your plugin!
[assembly: Guid("22f9cea5-3456-4594-856e-73840c20c30a")]

// [MANDATORY] The assembly versioning
//Should be incremented for each new release build of a plugin
[assembly: AssemblyVersion("0.0.0.1")]
[assembly: AssemblyFileVersion("0.0.0.1")]

// [MANDATORY] The name of your plugin
[assembly: AssemblyTitle("Smart Plug Control")]
// [MANDATORY] A short description of your plugin
[assembly: AssemblyDescription("Control smart Wi-Fi power strips (Kasa/Tapo) individually from NINA sequences")]

// The following attributes are not required for the plugin per se, but are required by the official manifest meta data

// Your name
[assembly: AssemblyCompany("Crepusculum")]
// The product name that this plugin is part of
[assembly: AssemblyProduct("Smart Plug Control")]
[assembly: AssemblyCopyright("Copyright © 2026 Crepusculum")]

// The minimum Version of N.I.N.A. that this plugin is compatible with
[assembly: AssemblyMetadata("MinimumApplicationVersion", "3.0.0.2017")]

// The license your plugin code is using
[assembly: AssemblyMetadata("License", "MIT")]
// The url to the license
[assembly: AssemblyMetadata("LicenseURL", "https://github.com/crepusculum-space/nina.plugin.smartplugcontrol/blob/main/LICENSE")]
// The repository where your pluggin is hosted
[assembly: AssemblyMetadata("Repository", "https://github.com/crepusculum-space/nina.plugin.smartplugcontrol")]

// The following attributes are optional for the official manifest meta data

//[Optional] Your plugin homepage URL - omit if not applicaple
[assembly: AssemblyMetadata("Homepage", "https://crepusculum.space")]

//[Optional] Common tags that quickly describe your plugin
[assembly: AssemblyMetadata("Tags", "Kasa,Tapo,Smart Plug,Smart Power Strip,TP-Link,Remote Observatory,Power Management,Automation")]

//[Optional] A link that will show a log of all changes in between your plugin's versions
[assembly: AssemblyMetadata("ChangelogURL", "https://github.com/crepusculum-space/nina.plugin.smartplugcontrol/blob/main/CHANGELOG.md")]

//[Optional] The url to a featured logo that will be displayed in the plugin list next to the name
[assembly: AssemblyMetadata("FeaturedImageURL", "")]
//[Optional] A url to an example screenshot of your plugin in action
[assembly: AssemblyMetadata("ScreenshotURL", "https://raw.githubusercontent.com/crepusculum-space/nina.plugin.smartplugcontrol/main/docs/screenshots/equipment-page.png")]
//[Optional] An additional url to an example example screenshot of your plugin in action
[assembly: AssemblyMetadata("AltScreenshotURL", "")]
//[Optional] An in-depth description of your plugin
[assembly: AssemblyMetadata("LongDescription", @"Control TP-Link Kasa smart plugs and power strips from your NINA imaging sequences - turn equipment on/off, toggle indicator LEDs, and react to plug state from the Advanced Sequencer.

Built for multi-tenant commercial remote observatories: all control goes exclusively through the TP-Link cloud API, never a direct local-network connection to your devices, so one client's NINA instance can never reach another client's equipment on a shared network.

Includes a dockable equipment page in the Imaging tab, 8 sequencer instructions (turn plugs/LEDs on/off, individually or all at once, with an optional delay), 4 loop conditions (plug on/off, total consumption above/below a threshold), and a plug-state-changed trigger.

Tapo devices are discovered alongside Kasa, but control isn't implemented yet.")]

// Setting ComVisible to false makes the types in this assembly not visible
// to COM components.  If you need to access a type in this assembly from
// COM, set the ComVisible attribute to true on that type.
[assembly: ComVisible(false)]
// [Unused]
[assembly: AssemblyConfiguration("")]
// [Unused]
[assembly: AssemblyTrademark("")]
// [Unused]
[assembly: AssemblyCulture("")]