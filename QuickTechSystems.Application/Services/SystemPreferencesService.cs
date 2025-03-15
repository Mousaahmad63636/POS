using AutoMapper;
using Microsoft.EntityFrameworkCore;
using QuickTechSystems.Application.DTOs;
using QuickTechSystems.Application.Interfaces;
using QuickTechSystems.Application.Services.Interfaces;
using QuickTechSystems.Domain.Entities;
using QuickTechSystems.Domain.Interfaces.Repositories;

namespace QuickTechSystems.Application.Services
{
    public class SystemPreferencesService : ISystemPreferencesService, IDisposable
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IMapper _mapper;
        private readonly IDbContextScopeService _dbContextScopeService;
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
        private bool _disposed;

        public SystemPreferencesService(
            IUnitOfWork unitOfWork,
            IMapper mapper,
            IDbContextScopeService dbContextScopeService)
        {
            _unitOfWork = unitOfWork;
            _mapper = mapper;
            _dbContextScopeService = dbContextScopeService;
        }

        public async Task<IEnumerable<SystemPreferenceDTO>> GetUserPreferencesAsync(string userId)
        {
            await _semaphore.WaitAsync();
            try
            {
                return await _dbContextScopeService.ExecuteInScopeAsync(async context =>
                {
                    var preferences = await _unitOfWork.SystemPreferences.Query()
                        .Where(p => p.UserId == userId)
                        .ToListAsync();
                    return _mapper.Map<IEnumerable<SystemPreferenceDTO>>(preferences);
                });
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task<string> GetPreferenceValueAsync(string userId, string key, string defaultValue = "")
        {
            await _semaphore.WaitAsync();
            try
            {
                return await _dbContextScopeService.ExecuteInScopeAsync(async context =>
                {
                    var preference = await _unitOfWork.SystemPreferences.Query()
                        .FirstOrDefaultAsync(p => p.UserId == userId && p.PreferenceKey == key);
                    return preference?.PreferenceValue ?? defaultValue;
                });
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task SavePreferenceAsync(string userId, string key, string value)
        {
            await _semaphore.WaitAsync();
            try
            {
                await _dbContextScopeService.ExecuteInScopeAsync(async context =>
                {
                    var preference = await _unitOfWork.SystemPreferences.Query()
                        .FirstOrDefaultAsync(p => p.UserId == userId && p.PreferenceKey == key);

                    if (preference == null)
                    {
                        preference = new SystemPreference
                        {
                            UserId = userId,
                            PreferenceKey = key,
                            PreferenceValue = value,
                            LastModified = DateTime.Now
                        };
                        await _unitOfWork.SystemPreferences.AddAsync(preference);
                    }
                    else
                    {
                        preference.PreferenceValue = value;
                        preference.LastModified = DateTime.Now;
                        await _unitOfWork.SystemPreferences.UpdateAsync(preference);
                    }

                    await _unitOfWork.SaveChangesAsync();
                });
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task SavePreferencesAsync(string userId, Dictionary<string, string> preferences)
        {
            await _semaphore.WaitAsync();
            try
            {
                await _dbContextScopeService.ExecuteInScopeAsync(async context =>
                {
                    foreach (var (key, value) in preferences)
                    {
                        var preference = await _unitOfWork.SystemPreferences.Query()
                            .FirstOrDefaultAsync(p => p.UserId == userId && p.PreferenceKey == key);

                        if (preference == null)
                        {
                            preference = new SystemPreference
                            {
                                UserId = userId,
                                PreferenceKey = key,
                                PreferenceValue = value,
                                LastModified = DateTime.Now
                            };
                            await _unitOfWork.SystemPreferences.AddAsync(preference);
                        }
                        else
                        {
                            preference.PreferenceValue = value;
                            preference.LastModified = DateTime.Now;
                            await _unitOfWork.SystemPreferences.UpdateAsync(preference);
                        }
                    }
                    await _unitOfWork.SaveChangesAsync();
                });
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task InitializeUserPreferencesAsync(string userId)
        {
            var defaultPreferences = new Dictionary<string, string>
            {
                { "Theme", "Light" },
                { "Language", "en-US" },
                { "TimeZone", "UTC" },
                { "DateFormat", "MM/dd/yyyy" },
                { "TimeFormat", "HH:mm:ss" },
                { "ReceiptPrinter", "Default" },
                { "BarcodeScannerEnabled", "true" },
                { "ShowGridLines", "true" },
                { "ItemsPerPage", "20" },
                { "AutoLogoutMinutes", "30" },
                { "EnableNotifications", "true" },
                { "SoundEffects", "true" }
            };

            await SavePreferencesAsync(userId, defaultPreferences);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _semaphore.Dispose();
                }
                _disposed = true;
            }
        }

        ~SystemPreferencesService()
        {
            Dispose(false);
        }
    }
}