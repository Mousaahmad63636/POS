using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AutoMapper;
using Microsoft.EntityFrameworkCore;
using QuickTechSystems.Application.DTOs;
using QuickTechSystems.Application.Events;
using QuickTechSystems.Application.Services.Interfaces;
using QuickTechSystems.Domain.Entities;
using QuickTechSystems.Domain.Interfaces.Repositories;

namespace QuickTechSystems.Application.Services
{
    public class DrawerService : BaseService<Drawer, DrawerDTO>, IDrawerService
    {
        private readonly IUnitOfWork _unitOfWork;

        public DrawerService(
            IUnitOfWork unitOfWork,
            IMapper mapper,
            IEventAggregator eventAggregator)
            : base(unitOfWork, mapper, unitOfWork.Drawers, eventAggregator)
        {
            _unitOfWork = unitOfWork;
        }

        private async Task LogDrawerAction(string actionType, string description, decimal amount, decimal balance)
        {
            var historyEntry = new DrawerHistoryEntry
            {
                Timestamp = DateTime.Now,
                ActionType = actionType,
                Description = description,
                Amount = amount,
                ResultingBalance = balance,
                UserId = "CurrentUser" // Replace with actual user system
            };

            await _unitOfWork.Context.Set<DrawerHistoryEntry>().AddAsync(historyEntry);
            await _unitOfWork.SaveChangesAsync();
        }

        public async Task<DrawerDTO?> GetCurrentDrawerAsync()
        {
            var drawer = await _repository.Query()
                .Where(d => d.Status == "Open")
                .OrderByDescending(d => d.OpenedAt)
                .FirstOrDefaultAsync();

            return _mapper.Map<DrawerDTO>(drawer);
        }

        public async Task<DrawerDTO> OpenDrawerAsync(decimal openingBalance, string cashierId, string cashierName)
        {
            var currentDrawer = await GetCurrentDrawerAsync();
            if (currentDrawer != null)
            {
                throw new InvalidOperationException("There is already an open drawer");
            }

            var drawer = new Drawer
            {
                OpeningBalance = openingBalance,
                CurrentBalance = openingBalance,
                OpenedAt = DateTime.Now,
                Status = "Open",
                CashierId = cashierId,
                CashierName = cashierName,
                CashIn = 0,
                CashOut = 0
            };

            var result = await _repository.AddAsync(drawer);
            await _unitOfWork.SaveChangesAsync();

            await LogDrawerAction("Open", $"Drawer opened by {cashierName}", openingBalance, openingBalance);

            var drawerDto = _mapper.Map<DrawerDTO>(result);
            _eventAggregator.Publish(new EntityChangedEvent<DrawerDTO>("Create", drawerDto));

            return drawerDto;
        }

        public async Task<DrawerDTO> CloseDrawerAsync(decimal finalBalance, string? notes)
        {
            var drawer = await _repository.Query()
                .FirstOrDefaultAsync(d => d.Status == "Open");

            if (drawer == null)
            {
                throw new InvalidOperationException("No open drawer found");
            }

            drawer.CurrentBalance = finalBalance;
            drawer.ClosedAt = DateTime.Now;
            drawer.Status = "Closed";
            drawer.Notes = notes;

            await _repository.UpdateAsync(drawer);
            await _unitOfWork.SaveChangesAsync();

            var difference = finalBalance - (drawer.OpeningBalance + drawer.CashIn - drawer.CashOut);
            var description = $"Drawer closed with {(difference >= 0 ? "surplus" : "shortage")} of {Math.Abs(difference):C2}";
            await LogDrawerAction("Close", description, difference, finalBalance);

            var drawerDto = _mapper.Map<DrawerDTO>(drawer);
            _eventAggregator.Publish(new EntityChangedEvent<DrawerDTO>("Update", drawerDto));

            return drawerDto;
        }

        public async Task<DrawerDTO> AddCashTransactionAsync(decimal amount, bool isIn)
        {
            var drawer = await _repository.Query()
                .FirstOrDefaultAsync(d => d.Status == "Open");

            if (drawer == null)
            {
                throw new InvalidOperationException("No open drawer found");
            }

            if (!isIn && amount > drawer.CurrentBalance)
            {
                throw new InvalidOperationException("Cannot remove more cash than current balance");
            }

            if (isIn)
            {
                drawer.CashIn += amount;
                drawer.CurrentBalance += amount;
            }
            else
            {
                drawer.CashOut += amount;
                drawer.CurrentBalance -= amount;
            }

            await _repository.UpdateAsync(drawer);
            await _unitOfWork.SaveChangesAsync();

            var actionType = isIn ? "Cash In" : "Cash Out";
            await LogDrawerAction(actionType, $"{actionType} transaction", amount, drawer.CurrentBalance);

            var drawerDto = _mapper.Map<DrawerDTO>(drawer);
            _eventAggregator.Publish(new EntityChangedEvent<DrawerDTO>("Update", drawerDto));

            return drawerDto;
        }

        public async Task<IEnumerable<DrawerTransactionDTO>> GetDrawerHistoryAsync(int drawerId)
        {
            var history = await _unitOfWork.Context.Set<DrawerHistoryEntry>()
                .Where(h => h.Timestamp.Date == DateTime.Today)
                .OrderByDescending(h => h.Timestamp)
                .ToListAsync();

            return _mapper.Map<IEnumerable<DrawerTransactionDTO>>(history);
        }

        public async Task<DrawerDTO> AdjustBalanceAsync(int drawerId, decimal newBalance, string reason)
        {
            var drawer = await _repository.GetByIdAsync(drawerId);
            if (drawer == null)
            {
                throw new InvalidOperationException("Drawer not found");
            }

            var adjustment = newBalance - drawer.CurrentBalance;
            drawer.CurrentBalance = newBalance;

            await _repository.UpdateAsync(drawer);
            await _unitOfWork.SaveChangesAsync();

            await LogDrawerAction("Balance Adjustment", reason, adjustment, newBalance);

            var drawerDto = _mapper.Map<DrawerDTO>(drawer);
            _eventAggregator.Publish(new EntityChangedEvent<DrawerDTO>("Update", drawerDto));

            return drawerDto;
        }
    }
}