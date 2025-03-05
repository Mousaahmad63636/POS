// QuickTechSystems.Application/Services/BaseService.cs
using AutoMapper;
using QuickTechSystems.Application.Events;
using QuickTechSystems.Application.Services.Interfaces;
using QuickTechSystems.Domain.Interfaces.Repositories;

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

        protected BaseService(
            IUnitOfWork unitOfWork,
            IMapper mapper,
            IGenericRepository<TEntity> repository,
            IEventAggregator eventAggregator)
        {
            _unitOfWork = unitOfWork;
            _mapper = mapper;
            _repository = repository;
            _eventAggregator = eventAggregator;
        }

        public virtual async Task<TDto?> GetByIdAsync(int id)
        {
            var entity = await _repository.GetByIdAsync(id);
            return _mapper.Map<TDto>(entity);
        }

        public virtual async Task<IEnumerable<TDto>> GetAllAsync()
        {
            var entities = await _repository.GetAllAsync();
            return _mapper.Map<IEnumerable<TDto>>(entities);
        }

        public virtual async Task<TDto> CreateAsync(TDto dto)
        {
            var entity = _mapper.Map<TEntity>(dto);
            var result = await _repository.AddAsync(entity);
            await _unitOfWork.SaveChangesAsync();
            var resultDto = _mapper.Map<TDto>(result);
            _eventAggregator.Publish(new EntityChangedEvent<TDto>("Create", resultDto));
            return resultDto;
        }

        public virtual async Task UpdateAsync(TDto dto)
        {
            using var transaction = await _unitOfWork.BeginTransactionAsync();
            try
            {
                var entity = _mapper.Map<TEntity>(dto);

                // Detach any existing entity with the same key that might be tracked
                var existingEntity = await _repository.GetByIdAsync(GetEntityId(entity));
                if (existingEntity != null)
                {
                    // Use the context to detach the entity
                    _unitOfWork.Context.Entry(existingEntity).State = EntityState.Detached;
                }

                await _repository.UpdateAsync(entity);
                await _unitOfWork.SaveChangesAsync();
                _eventAggregator.Publish(new EntityChangedEvent<TDto>("Update", dto));

                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }
        protected virtual int GetEntityId(TEntity entity)
        {
            // This is a placeholder - you need to implement this based on your entity structure
            // For most entities, you could use reflection to get the Id property
            var idProperty = typeof(TEntity).GetProperty($"{typeof(TEntity).Name}Id");
            if (idProperty != null)
            {
                return (int)idProperty.GetValue(entity);
            }

            var genericIdProperty = typeof(TEntity).GetProperty("Id");
            if (genericIdProperty != null)
            {
                return (int)genericIdProperty.GetValue(entity);
            }

            throw new InvalidOperationException($"Could not determine ID property for entity of type {typeof(TEntity).Name}");
        }
        public virtual async Task DeleteAsync(int id)
        {
            var entity = await _repository.GetByIdAsync(id);
            if (entity != null)
            {
                await _repository.DeleteAsync(entity);
                await _unitOfWork.SaveChangesAsync();
                var dto = _mapper.Map<TDto>(entity);
                _eventAggregator.Publish(new EntityChangedEvent<TDto>("Delete", dto));
            }
        }
    }
}