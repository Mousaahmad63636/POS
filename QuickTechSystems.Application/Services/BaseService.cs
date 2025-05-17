// QuickTechSystems.Application/Services/BaseService.cs
using AutoMapper;
using System.Diagnostics;
using QuickTechSystems.Application.Events;
using QuickTechSystems.Application.Interfaces;
using QuickTechSystems.Application.Services.Interfaces;
using QuickTechSystems.Domain.Interfaces.Repositories;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

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
            return await _dbContextScopeService.ExecuteInScopeAsync(async context =>
            {
                var entity = await _repository.GetByIdAsync(id);
                return _mapper.Map<TDto>(entity);
            });
        }

        public virtual async Task<IEnumerable<TDto>> GetAllAsync()
        {
            return await _dbContextScopeService.ExecuteInScopeAsync(async context =>
            {
                var entities = await _repository.GetAllAsync();
                return _mapper.Map<IEnumerable<TDto>>(entities);
            });
        }

        public virtual async Task<TDto> CreateAsync(TDto dto)
        {
            return await _dbContextScopeService.ExecuteInScopeAsync(async context =>
            {
                var entity = _mapper.Map<TEntity>(dto);
                var result = await _repository.AddAsync(entity);
                await _unitOfWork.SaveChangesAsync();
                var resultDto = _mapper.Map<TDto>(result);
                _eventAggregator.Publish(new EntityChangedEvent<TDto>("Create", resultDto));
                return resultDto;
            });
        }

        // Updated UpdateAsync method - matches interface return type and fixes naming conflict
        public virtual async Task UpdateAsync(TDto dto)
        {
            await _dbContextScopeService.ExecuteInScopeAsync(async context =>
            {
                try
                {
                    // Find the ID property - using reflection to be generic across entity types
                    var idProperty = typeof(TDto).GetProperty("ProductId") ??
                                     typeof(TDto).GetProperty("Id") ??
                                     typeof(TDto).GetProperty($"{typeof(TEntity).Name}Id");

                    if (idProperty == null)
                    {
                        Debug.WriteLine($"Warning: Cannot find ID property in DTO of type {typeof(TDto).Name}");
                        // Fallback to traditional update without detaching
                        var entityToUpdate = _mapper.Map<TEntity>(dto);
                        await _repository.UpdateAsync(entityToUpdate);
                        await _unitOfWork.SaveChangesAsync();
                        _eventAggregator.Publish(new EntityChangedEvent<TDto>("Update", dto));
                        return;
                    }

                    var id = idProperty.GetValue(dto);
                    Debug.WriteLine($"BaseService.UpdateAsync: Updating entity of type {typeof(TEntity).Name} with ID {id}");

                    // Get existing entity - we need this to properly detach it
                    var existingEntity = await _repository.GetByIdAsync((int)id);
                    if (existingEntity == null)
                    {
                        Debug.WriteLine($"Entity with ID {id} not found for update.");
                        throw new Exception($"Entity with ID {id} not found.");
                    }

                    // Detach existing entity to avoid tracking conflicts
                    _unitOfWork.DetachEntity(existingEntity);

                    // Map DTO to fresh entity and update
                    var updatedEntity = _mapper.Map<TEntity>(dto);
                    await _repository.UpdateAsync(updatedEntity);
                    await _unitOfWork.SaveChangesAsync();

                    // Publish update event
                    _eventAggregator.Publish(new EntityChangedEvent<TDto>("Update", dto));

                    Debug.WriteLine($"Successfully updated entity of type {typeof(TEntity).Name}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error in BaseService.UpdateAsync: {ex.Message}");
                    if (ex.InnerException != null)
                    {
                        Debug.WriteLine($"Inner exception: {ex.InnerException.Message}");
                    }
                    throw;
                }
            });
        }

        public virtual async Task DeleteAsync(int id)
        {
            await _dbContextScopeService.ExecuteInScopeAsync(async context =>
            {
                var entity = await _repository.GetByIdAsync(id);
                if (entity != null)
                {
                    await _repository.DeleteAsync(entity);
                    await _unitOfWork.SaveChangesAsync();
                    var dto = _mapper.Map<TDto>(entity);
                    _eventAggregator.Publish(new EntityChangedEvent<TDto>("Delete", dto));
                }
            });
        }
    }
}