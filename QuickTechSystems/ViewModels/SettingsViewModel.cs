// QuickTechSystems.WPF/ViewModels/SettingsViewModel.cs
using Microsoft.VisualBasic;
using QuickTechSystems.Application.Events;
using QuickTechSystems.Application.Helpers;
using QuickTechSystems.Helpers;
using QuickTechSystems.WPF;
using System.Windows.Forms;
using Microsoft.Win32;
using System.IO;

namespace QuickTechSystems.WPF.ViewModels
{
    public class SettingsViewModel : ViewModelBase
{
        private readonly LanguageManager _languageManager;
        private string _selectedLanguage;

        public ObservableCollection<LanguageInfo> AvailableLanguages => LanguageManager.AvailableLanguages;

        private readonly IBusinessSettingsService _businessSettingsService;
    private readonly ISystemPreferencesService _systemPreferencesService;
    private ObservableCollection<BusinessSettingDTO> _businessSettings;
    private BusinessSettingDTO? _selectedBusinessSetting;
    private string _selectedGroup = "All";
    private bool _isEditing;
    private Action<EntityChangedEvent<BusinessSettingDTO>> _settingChangedHandler;
        private decimal _exchangeRate;

        // System Preferences Properties
        private readonly IBackupService _backupService;
        private bool _autoBackupEnabled;
        private string _selectedDay;
        private string _backupTime;
        private string _backupPath;
        private string _lastBackupStatus;

        private string _currentTheme = "Light";
    private string _currentLanguage = "en-US";
    private bool _soundEffectsEnabled = true;
    private bool _notificationsEnabled = true;
    private string _dateFormat = "MM/dd/yyyy";
    private string _timeFormat = "HH:mm:ss";

        public SettingsViewModel(
    IBusinessSettingsService businessSettingsService,
    ISystemPreferencesService systemPreferencesService,
    IBackupService backupService,
    LanguageManager languageManager,
    IEventAggregator eventAggregator) : base(eventAggregator)
        {
            _businessSettingsService = businessSettingsService;
            _systemPreferencesService = systemPreferencesService;
            _backupService = backupService;
            _languageManager = languageManager;
            _settingChangedHandler = HandleSettingChanged;

            InitializeCommands();
            _ = LoadDataAsync();
            _ = LoadExchangeRate();
        }
        public decimal ExchangeRate
        {
            get => _exchangeRate;
            set => SetProperty(ref _exchangeRate, value);
        }
        public string SelectedLanguage
        {
            get => _selectedLanguage;
            set
            {
                if (SetProperty(ref _selectedLanguage, value))
                {
                    _ = _languageManager.SetLanguage(value);
                }
            }
        }
        private void InitializeCommands()
        {
            LoadCommand = new AsyncRelayCommand(async _ => await LoadDataAsync());
            SaveBusinessSettingCommand = new AsyncRelayCommand(async _ => await SaveBusinessSettingAsync());
            InitializeBusinessSettingsCommand = new AsyncRelayCommand(async _ => await InitializeBusinessSettingsAsync());
            ResetPreferencesCommand = new AsyncRelayCommand(async _ => await ResetPreferencesAsync());
            BackupNowCommand = new AsyncRelayCommand(async _ => await BackupNowAsync());
            BrowseCommand = new RelayCommand(_ => BrowseForBackupPath());
            SaveScheduleCommand = new AsyncRelayCommand(async _ => await SaveScheduleAsync());
            RestoreCommand = new AsyncRelayCommand(async _ => await RestoreBackupAsync());
            SaveExchangeRateCommand = new AsyncRelayCommand(async _ => await SaveExchangeRate());
        }
        private async Task LoadBackupSettingsAsync()
        {
            try
            {
                var schedule = await _backupService.GetBackupScheduleAsync();
                if (schedule.HasValue)
                {
                    AutoBackupEnabled = schedule.Value.IsEnabled;
                    SelectedDay = schedule.Value.Day.ToString();
                    BackupTime = schedule.Value.Time.ToString(@"hh\:mm");
                }

                var lastBackup = await _backupService.GetLastBackupTimeAsync();
                LastBackupStatus = lastBackup.HasValue
                    ? $"Last backup: {lastBackup.Value:g}"
                    : "No backup has been performed yet";
            }
            catch (Exception ex)
            {
                await ShowErrorMessageAsync($"Error loading backup settings: {ex.Message}");
            }
        }
        private async Task BackupNowAsync()
        {
            try
            {
                if (string.IsNullOrEmpty(BackupPath))
                {
                    BackupPath = "C:\\QuickTechBackup"; // Default backup location
                }

                // Create the backup directory if it doesn't exist
                Directory.CreateDirectory(BackupPath);

                await _backupService.CreateBackupAsync(BackupPath);
                await LoadBackupSettingsAsync();
                MessageBox.Show("Backup completed successfully.", "Success",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                await ShowErrorMessageAsync($"Error performing backup: {ex.Message}");
            }
        }

        private void BrowseForBackupPath()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                DefaultExt = ".bak",
                Filter = "Backup files (*.bak)|*.bak|All files (*.*)|*.*",
                Title = "Select Backup Location"
            };

            if (dialog.ShowDialog() == true)
            {
                BackupPath = dialog.FileName;
            }
        }

        private async Task SaveScheduleAsync()
        {
            try
            {
                if (AutoBackupEnabled)
                {
                    if (string.IsNullOrEmpty(BackupPath))
                    {
                        BackupPath = "C:\\QuickTechBackup"; // Default backup location
                    }

                    // Create the backup directory if it doesn't exist
                    Directory.CreateDirectory(BackupPath);

                    if (!TimeSpan.TryParse(BackupTime, out TimeSpan scheduledTime))
                    {
                        MessageBox.Show("Please enter a valid time in HH:mm format.", "Validation Error",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    if (string.IsNullOrEmpty(SelectedDay))
                    {
                        MessageBox.Show("Please select a day for automatic backup.", "Validation Error",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    var day = Enum.Parse<DayOfWeek>(SelectedDay);
                    await _backupService.ScheduleAutomaticBackupAsync(BackupPath, day, scheduledTime);
                    MessageBox.Show("Automatic backup schedule saved successfully.", "Success",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    await _backupService.DisableAutomaticBackupAsync();
                    MessageBox.Show("Automatic backup disabled.", "Success",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                await ShowErrorMessageAsync($"Error saving backup schedule: {ex.Message}");
            }
        }

        private async Task RestoreBackupAsync()
        {
            try
            {
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    DefaultExt = ".bak",
                    Filter = "Backup files (*.bak)|*.bak|All files (*.*)|*.*",
                    Title = "Select Backup File to Restore"
                };

                if (dialog.ShowDialog() == true)
                {
                    var result = MessageBox.Show(
                        "Restoring a backup will replace all current data. Are you sure you want to continue?",
                        "Confirm Restore",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                    if (result == MessageBoxResult.Yes)
                    {
                        await _backupService.RestoreBackupAsync(dialog.FileName);
                        MessageBox.Show("Database restored successfully. The application will now restart.",
                            "Success", MessageBoxButton.OK, MessageBoxImage.Information);

                        // Restart application
                        System.Windows.Application.Current.Shutdown();
                        System.Diagnostics.Process.Start(System.Windows.Application.ResourceAssembly.Location);
                    }
                }
            }
            catch (Exception ex)
            {
                await ShowErrorMessageAsync($"Error restoring backup: {ex.Message}");
            }
        }
        protected override void SubscribeToEvents()
    {
        _eventAggregator.Subscribe<EntityChangedEvent<BusinessSettingDTO>>(_settingChangedHandler);
    }
        private async Task LoadExchangeRate()
        {
            var rateSetting = await _businessSettingsService.GetByKeyAsync("ExchangeRate");
            if (rateSetting != null && decimal.TryParse(rateSetting.Value, out decimal rate))
            {
                ExchangeRate = rate;
                CurrencyHelper.UpdateExchangeRate(rate);
            }
        }
        protected override void UnsubscribeFromEvents()
    {
        _eventAggregator.Unsubscribe<EntityChangedEvent<BusinessSettingDTO>>(_settingChangedHandler);
    }

        private async void HandleSettingChanged(EntityChangedEvent<BusinessSettingDTO> evt)
        {
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                switch (evt.Action)
                {
                    case "Create":
                        BusinessSettings.Add(evt.Entity);
                        break;
                    case "Update":
                        var existingSetting = BusinessSettings.FirstOrDefault(s => s.Id == evt.Entity.Id);
                        if (existingSetting != null)
                        {
                            var index = BusinessSettings.IndexOf(existingSetting);
                            BusinessSettings[index] = evt.Entity;
                        }
                        break;
                    case "Delete":
                        var settingToRemove = BusinessSettings.FirstOrDefault(s => s.Id == evt.Entity.Id);
                        if (settingToRemove != null)
                        {
                            BusinessSettings.Remove(settingToRemove);
                        }
                        break;
                }
            });
        }
        public bool AutoBackupEnabled
        {
            get => _autoBackupEnabled;
            set => SetProperty(ref _autoBackupEnabled, value);
        }

        public string SelectedDay
        {
            get => _selectedDay;
            set => SetProperty(ref _selectedDay, value);
        }

        public string BackupTime
        {
            get => _backupTime;
            set => SetProperty(ref _backupTime, value);
        }

        public string BackupPath
        {
            get => _backupPath;
            set => SetProperty(ref _backupPath, value);
        }

        public string LastBackupStatus
        {
            get => _lastBackupStatus;
            set => SetProperty(ref _lastBackupStatus, value);
        }

        protected override async Task LoadDataAsync()
    {
        try
        {
            await LoadBusinessSettingsAsync();
            await LoadSystemPreferencesAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error loading settings: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task LoadBusinessSettingsAsync()
    {
        var settings = await _businessSettingsService.GetAllAsync();
        BusinessSettings = new ObservableCollection<BusinessSettingDTO>(settings);
    }
        public ObservableCollection<string> DaysOfWeek { get; } = new(
            Enum.GetNames(typeof(DayOfWeek)));

        private async Task LoadSettingsByGroupAsync()
    {
        try
        {
            if (SelectedGroup == "All")
            {
                await LoadBusinessSettingsAsync();
                return;
            }

            var settings = await _businessSettingsService.GetByGroupAsync(SelectedGroup);
            BusinessSettings = new ObservableCollection<BusinessSettingDTO>(settings);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error loading settings: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task SaveBusinessSettingAsync()
    {
        try
        {
            if (SelectedBusinessSetting == null) return;

            if (string.IsNullOrWhiteSpace(SelectedBusinessSetting.Value))
            {
                MessageBox.Show("Setting value cannot be empty.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            await _businessSettingsService.UpdateSettingAsync(
                SelectedBusinessSetting.Key,
                SelectedBusinessSetting.Value,
                "CurrentUser" // Replace with actual user info when authentication is implemented
            );

            await LoadSettingsByGroupAsync();
            MessageBox.Show("Setting updated successfully.", "Success",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error saving setting: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task InitializeBusinessSettingsAsync()
    {
        try
        {
            if (MessageBox.Show("This will initialize default business settings. Continue?",
                "Confirm Initialize", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                await _businessSettingsService.InitializeDefaultSettingsAsync();
                await LoadBusinessSettingsAsync();
                MessageBox.Show("Default settings initialized successfully.", "Success",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error initializing settings: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

        private async Task LoadSystemPreferencesAsync()
        {
            try
            {
                const string userId = "default";
                CurrentTheme = await _systemPreferencesService.GetPreferenceValueAsync(userId, "Theme", "Light");

                // Load and set the selected language
                var savedLanguage = await _systemPreferencesService.GetPreferenceValueAsync(userId, "Language", "en-US");
                SelectedLanguage = savedLanguage;  // This will trigger the language change through the property setter

                SoundEffectsEnabled = bool.Parse(await _systemPreferencesService.GetPreferenceValueAsync(userId, "SoundEffects", "true"));
                NotificationsEnabled = bool.Parse(await _systemPreferencesService.GetPreferenceValueAsync(userId, "EnableNotifications", "true"));
                DateFormat = await _systemPreferencesService.GetPreferenceValueAsync(userId, "DateFormat", "MM/dd/yyyy");
                TimeFormat = await _systemPreferencesService.GetPreferenceValueAsync(userId, "TimeFormat", "HH:mm:ss");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading preferences: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task SavePreferenceAsync(string key, string value)
    {
        try
        {
            const string userId = "default";
            await _systemPreferencesService.SavePreferenceAsync(userId, key, value);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error saving preference: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
        private async Task SaveExchangeRate()
        {
            try
            {
                await _businessSettingsService.UpdateSettingAsync(
                    "ExchangeRate",
                    ExchangeRate.ToString(),
                    "System");

                CurrencyHelper.UpdateExchangeRate(ExchangeRate);
                MessageBox.Show("Exchange rate updated successfully", "Success",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving exchange rate: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

    private async Task ResetPreferencesAsync()
    {
        try
        {
            if (MessageBox.Show("Are you sure you want to reset all preferences to default?",
                "Confirm Reset", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                const string userId = "default";
                await _systemPreferencesService.InitializeUserPreferencesAsync(userId);
                await LoadSystemPreferencesAsync();
                MessageBox.Show("Preferences have been reset to default values.", "Success",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error resetting preferences: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // Properties
    public ObservableCollection<BusinessSettingDTO> BusinessSettings
    {
        get => _businessSettings;
        set => SetProperty(ref _businessSettings, value);
    }

    public BusinessSettingDTO? SelectedBusinessSetting
    {
        get => _selectedBusinessSetting;
        set
        {
            SetProperty(ref _selectedBusinessSetting, value);
            IsEditing = value != null;
        }
    }

    public string SelectedGroup
    {
        get => _selectedGroup;
        set
        {
            SetProperty(ref _selectedGroup, value);
            _ = LoadSettingsByGroupAsync();
        }
    }

    public bool IsEditing
    {
        get => _isEditing;
        set => SetProperty(ref _isEditing, value);
    }

    public string CurrentTheme
    {
        get => _currentTheme;
        set
        {
            if (SetProperty(ref _currentTheme, value))
            {
                _ = SavePreferenceAsync("Theme", value);
            }
        }
    }

    public string CurrentLanguage
    {
        get => _currentLanguage;
        set
        {
            if (SetProperty(ref _currentLanguage, value))
            {
                _ = SavePreferenceAsync("Language", value);
            }
        }
    }

    public bool SoundEffectsEnabled
    {
        get => _soundEffectsEnabled;
        set
        {
            if (SetProperty(ref _soundEffectsEnabled, value))
            {
                _ = SavePreferenceAsync("SoundEffects", value.ToString());
            }
        }
    }

    public bool NotificationsEnabled
    {
        get => _notificationsEnabled;
        set
        {
            if (SetProperty(ref _notificationsEnabled, value))
            {
                _ = SavePreferenceAsync("EnableNotifications", value.ToString());
            }
        }
    }

    public string DateFormat
    {
        get => _dateFormat;
        set
        {
            if (SetProperty(ref _dateFormat, value))
            {
                _ = SavePreferenceAsync("DateFormat", value);
            }
        }
    }

    public string TimeFormat
    {
        get => _timeFormat;
        set
        {
            if (SetProperty(ref _timeFormat, value))
            {
                _ = SavePreferenceAsync("TimeFormat", value);
            }
        }
    }

    public ObservableCollection<string> AvailableThemes { get; } = new()
{
   "Light", "Dark", "System"
};



        public ObservableCollection<string> DateFormats { get; } = new()
{
   "MM/dd/yyyy", "dd/MM/yyyy", "yyyy-MM-dd"
};

    public ObservableCollection<string> TimeFormats { get; } = new()
{
   "HH:mm:ss", "hh:mm:ss tt"
};

    public ObservableCollection<string> Groups { get; } = new()
{
   "All", "General", "Financial", "Inventory", "Sales"
};

    // Commands
    public ICommand LoadCommand { get; private set; }
    public ICommand SaveBusinessSettingCommand { get; private set; }
    public ICommand InitializeBusinessSettingsCommand { get; private set; }
    public ICommand ResetPreferencesCommand { get; private set; }
        public ICommand BackupNowCommand { get; private set; }
        public ICommand BrowseCommand { get; private set; }
        public ICommand SaveScheduleCommand { get; private set; }
        public ICommand RestoreCommand { get; private set; }
        public ICommand SaveExchangeRateCommand { get; private set; }

    }
}