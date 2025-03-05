using AutoMapper;
using Microsoft.EntityFrameworkCore;
using QuickTechSystems.Application.DTOs;
using QuickTechSystems.Application.Events;
using QuickTechSystems.Application.Services.Interfaces;
using QuickTechSystems.Domain.Entities;
using QuickTechSystems.Domain.Interfaces.Repositories;

namespace QuickTechSystems.Application.Services
{
    public class SubscriptionTypeService : BaseService<SubscriptionType, SubscriptionTypeDTO>, ISubscriptionTypeService
    {
        public SubscriptionTypeService(
            IUnitOfWork unitOfWork,
            IMapper mapper,
            IEventAggregator eventAggregator)
            : base(unitOfWork, mapper, unitOfWork.SubscriptionTypes, eventAggregator)
        {
        }

        public async Task<IEnumerable<SubscriptionTypeDTO>> GetActiveTypesAsync()
        {
            var types = await _repository.Query()
                .Include(t => t.CustomerSubscriptions)
                .Where(t => t.IsActive)
                .ToListAsync();

            return _mapper.Map<IEnumerable<SubscriptionTypeDTO>>(types);
        }

        public async Task<SubscriptionTypeDTO?> GetByNameAsync(string name)
        {
            var type = await _repository.Query()
                .Include(t => t.CustomerSubscriptions)
                .FirstOrDefaultAsync(t => t.Name == name);

            return _mapper.Map<SubscriptionTypeDTO>(type);
        }

        public async Task<bool> IsNameUniqueAsync(string name, int? excludeId = null)
        {
            return !await _repository.Query()
                .AnyAsync(t => t.Name == name && (!excludeId.HasValue || t.Id != excludeId));
        }

        public async Task<int> GetCustomerCountAsync(int typeId)
        {
            return await _repository.Query()
                .Where(t => t.Id == typeId)
                .SelectMany(t => t.CustomerSubscriptions)
                .CountAsync();
        }

        public override async Task DeleteAsync(int id)
        {
            var type = await _repository.Query()
                .Include(t => t.CustomerSubscriptions)
                .FirstOrDefaultAsync(t => t.Id == id);

            if (type == null) return;

            if (type.CustomerSubscriptions.Any())
            {
                throw new InvalidOperationException(
                    "Cannot delete subscription type that has associated customers. " +
                    "Please reassign customers first or mark as inactive instead.");
            }

            await base.DeleteAsync(id);
        }
    }
}