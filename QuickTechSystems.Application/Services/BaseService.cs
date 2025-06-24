using AutoMapper;
using QuickTechSystems.Application.Events;
using QuickTechSystems.Application.Mappings;
using QuickTechSystems.Application.Services.Interfaces;
using QuickTechSystems.Domain.Interfaces;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;

namespace QuickTechSystems.Application.Services
{
    public abstract class BaseService<TEntity, TDto> : IBaseService<TDto>
        where TEntity : class
        where TDto : class
    {
        protected readonly IUnitOfWork _unitOfWork;
        protected readonly IMapper _mapper;
        protected readonly IGenericRepository<TEntity> _repository;
        protected readonly IEventAggregator _eventAggregator;
        protected readonly IDbContextScopeService _dbContextScopeService;
        private readonly SemaphoreSlim _operationLock = new SemaphoreSlim(1, 1);
        private readonly Dictionary<string, SemaphoreSlim> _namedLocks = new Dictionary<string, SemaphoreSlim>();
        private readonly Dictionary<int, DateTime> _entityAccessTimestamps = new Dictionary<int, DateTime>();
        private readonly HashSet<int> _activeEntityOperations = new HashSet<int>();
        private readonly object _lockManager = new object();

        protected BaseService(
            IUnitOfWork unitOfWork,
            IMapper mapper,
            IEventAggregator eventAggregator,
            IDbContextScopeService dbContextScopeService)
        {
            _unitOfWork = unitOfWork;
            _mapper = mapper;
            _repository = unitOfWork.GetRepository<TEntity>();
            _eventAggregator = eventAggregator;
            _dbContextScopeService = dbContextScopeService;
        }

        public virtual async Task<TDto?> GetByIdAsync(int id)
        {
            return await ExecuteServiceOperationAsync(async () =>
            {
                var entity = await _repository.GetByIdAsync(id);
                return _mapper.Map<TDto>(entity);
            }, $"GetById_{id}");
        }

        public virtual async Task<IEnumerable<TDto>> GetAllAsync()
        {
            return await ExecuteServiceOperationAsync(async () =>
            {
                var entities = await _repository.GetAllAsync();
                return _mapper.Map<IEnumerable<TDto>>(entities);
            }, "GetAll");
        }

        public virtual async Task<TDto> CreateAsync(TDto dto)
        {
            return await ExecuteServiceOperationAsync(async () =>
            {
                using var transaction = await _unitOfWork.BeginTransactionAsync();
                try
                {
                    var entity = _mapper.Map<TEntity>(dto);
                    var result = await _repository.AddAsync(entity);
                    await _unitOfWork.SaveChangesAsync();
                    await transaction.CommitAsync();

                    var resultDto = _mapper.Map<TDto>(result);
                    _eventAggregator.Publish(new EntityChangedEvent<TDto>("Create", resultDto));
                    return resultDto;
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            }, "Create");
        }

        public virtual async Task UpdateAsync(TDto dto)
        {
            await ExecuteServiceOperationAsync(async () =>
            {
                using var transaction = await _unitOfWork.BeginTransactionAsync();
                try
                {
                    var entity = _mapper.Map<TEntity>(dto);
                    await _repository.UpdateAsync(entity);
                    await _unitOfWork.SaveChangesAsync();
                    await transaction.CommitAsync();

                    _eventAggregator.Publish(new EntityChangedEvent<TDto>("Update", dto));
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            }, "Update");
        }

        public virtual async Task DeleteAsync(int id)
        {
            await ExecuteServiceOperationAsync(async () =>
            {
                using var transaction = await _unitOfWork.BeginTransactionAsync();
                try
                {
                    var entity = await _repository.GetByIdAsync(id);
                    if (entity != null)
                    {
                        await _repository.DeleteAsync(entity);
                        await _unitOfWork.SaveChangesAsync();
                        await transaction.CommitAsync();

                        var dto = _mapper.Map<TDto>(entity);
                        _eventAggregator.Publish(new EntityChangedEvent<TDto>("Delete", dto));
                    }
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            }, $"Delete_{id}");
        }

        protected async Task<T> ExecuteServiceOperationAsync<T>(Func<Task<T>> operation, string operationName = "Service Operation")
        {
            var operationLock = GetOrCreateNamedLock(operationName);

            if (!await operationLock.WaitAsync(100))
            {
                return default(T);
            }

            try
            {
                return await _dbContextScopeService.ExecuteInScopeAsync(async context =>
                {
                    try
                    {
                        _unitOfWork.DetachAllEntities();
                        var result = await operation();
                        _unitOfWork.DetachAllEntities();
                        return result;
                    }
                    catch (Exception)
                    {
                        _unitOfWork.DetachAllEntities();
                        throw;
                    }
                });
            }
            finally
            {
                operationLock.Release();
            }
        }

        protected async Task ExecuteServiceOperationAsync(Func<Task> operation, string operationName = "Service Operation")
        {
            await ExecuteServiceOperationAsync(async () =>
            {
                await operation();
                return true;
            }, operationName);
        }

        protected async Task<bool> ExecuteWithEntityLockAsync<TKey>(TKey entityKey, Func<Task<bool>> operation, string operationName = "Entity Operation")
        {
            var keyString = entityKey?.ToString() ?? "null";
            var lockKey = $"{operationName}_{keyString}";

            lock (_lockManager)
            {
                var keyHash = keyString.GetHashCode();
                if (_activeEntityOperations.Contains(keyHash))
                {
                    return false;
                }
                _activeEntityOperations.Add(keyHash);
                _entityAccessTimestamps[keyHash] = DateTime.Now;
            }

            try
            {
                return await ExecuteServiceOperationAsync(operation, lockKey);
            }
            finally
            {
                lock (_lockManager)
                {
                    var keyHash = keyString.GetHashCode();
                    _activeEntityOperations.Remove(keyHash);
                    _entityAccessTimestamps.Remove(keyHash);
                }
            }
        }

        private SemaphoreSlim GetOrCreateNamedLock(string operationName)
        {
            lock (_lockManager)
            {
                if (!_namedLocks.ContainsKey(operationName))
                {
                    _namedLocks[operationName] = new SemaphoreSlim(1, 1);
                }
                return _namedLocks[operationName];
            }
        }

        protected async Task<bool> ExecuteEntityOperationSafelyAsync<TKey>(TKey entityKey, Func<Task> operation, string operationName)
        {
            return await ExecuteWithEntityLockAsync(entityKey, async () =>
            {
                await operation();
                return true;
            }, operationName);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                _operationLock?.Dispose();

                lock (_lockManager)
                {
                    foreach (var namedLock in _namedLocks.Values)
                    {
                        namedLock?.Dispose();
                    }
                    _namedLocks.Clear();
                    _activeEntityOperations.Clear();
                    _entityAccessTimestamps.Clear();
                }
            }
        }

        public virtual void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}