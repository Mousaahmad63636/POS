// QuickTechSystems/Helpers/LanguageManager.cs
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using QuickTechSystems.Application.Services.Interfaces;

namespace QuickTechSystems.Helpers
{
    public class LanguageManager
    {
        private readonly ISystemPreferencesService _preferencesService;
        private const string DefaultLanguage = "en-US";
        public event EventHandler<string>? LanguageChanged;

        public LanguageManager(ISystemPreferencesService preferencesService)
        {
            _preferencesService = preferencesService;
            InitializeDefaultLanguageAsync();
        }

        private async void InitializeDefaultLanguageAsync()
        {
            try
            {
                var savedLanguage = await _preferencesService.GetPreferenceValueAsync("default", "Language", DefaultLanguage);
                await SetLanguage(savedLanguage);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error initializing default language: {ex.Message}");
                await SetLanguage(DefaultLanguage);
            }
        }

        public static ObservableCollection<LanguageInfo> AvailableLanguages { get; } = new()
        {
            new LanguageInfo { Code = "en-US", Name = "English", NativeName = "English" },
            new LanguageInfo { Code = "fr-FR", Name = "French", NativeName = "Français" },
            new LanguageInfo { Code = "ar-SA", Name = "Arabic", NativeName = "العربية", FlowDirection = FlowDirection.RightToLeft }
        };

        public async Task SetLanguage(string languageCode)
        {
            try
            {
                var resourceDictionary = new ResourceDictionary
                {
                    Source = new Uri($"/Resources/Dictionaries/Languages/{languageCode}.xaml", UriKind.Relative)
                };

                // Remove existing language dictionary
                var existingDict = System.Windows.Application.Current.Resources.MergedDictionaries
                    .FirstOrDefault(d => d.Source?.OriginalString.Contains("/Languages/") ?? false);

                if (existingDict != null)
                    System.Windows.Application.Current.Resources.MergedDictionaries.Remove(existingDict);

                System.Windows.Application.Current.Resources.MergedDictionaries.Add(resourceDictionary);

                // Save preference
                await _preferencesService.SavePreferenceAsync("default", "Language", languageCode);

                // Update FlowDirection for the entire application
                var languageInfo = AvailableLanguages.FirstOrDefault(l => l.Code == languageCode);
                if (languageInfo != null && System.Windows.Application.Current.MainWindow != null)
                {
                    System.Windows.Application.Current.MainWindow.FlowDirection = languageInfo.FlowDirection;
                }

                // Notify subscribers about the language change
                LanguageChanged?.Invoke(this, languageCode);

                Debug.WriteLine($"Language changed to: {languageCode}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error setting language: {ex.Message}");

                // Fallback to default language if there's an error
                if (languageCode != DefaultLanguage)
                {
                    Debug.WriteLine("Falling back to default language");
                    await SetLanguage(DefaultLanguage);
                }
            }
        }

        public FlowDirection GetFlowDirection(string languageCode)
        {
            var languageInfo = AvailableLanguages.FirstOrDefault(l => l.Code == languageCode);
            return languageInfo?.FlowDirection ?? FlowDirection.LeftToRight;
        }
    }

    public class LanguageInfo
    {
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string NativeName { get; set; } = string.Empty;
        public FlowDirection FlowDirection { get; set; } = FlowDirection.LeftToRight;
    }
}