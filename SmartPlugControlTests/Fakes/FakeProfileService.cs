using NINA.Core.Utility;
using NINA.Profile;
using NINA.Profile.Interfaces;
using System;
using System.Globalization;

namespace SmartPlugControlTests.Fakes {
    /// <summary>Minimal IProfileService fake - just enough for DockableVM/BaseVM to construct without a real NINA host.</summary>
    public class FakeProfileService : IProfileService {
        public bool ProfileWasSpecifiedFromCommandLineArgs => false;
        public AsyncObservableCollection<ProfileMeta> Profiles { get; } = new AsyncObservableCollection<ProfileMeta>();
        public IProfile ActiveProfile => null;

        public event EventHandler LocaleChanged;
        public event EventHandler LocationChanged;
        public event EventHandler BeforeProfileChanging;
        public event EventHandler ProfileChanged;
        public event EventHandler HorizonChanged;

        public bool Clone(ProfileMeta p) => false;
        public void Add() { }
        public bool SelectProfile(ProfileMeta p) => false;
        public bool RemoveProfile(ProfileMeta p) => false;
        public void ChangeLocale(CultureInfo culture) { }
        public void ChangeLatitude(double latitude) { }
        public void ChangeLongitude(double longitude) { }
        public void ChangeElevation(double elevation) { }
        public void ChangeHorizon(string filePath) { }
        public void Release() { }
    }
}
