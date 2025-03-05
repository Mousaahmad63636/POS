using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using QuickTechSystems.Application.DTOs;

namespace QuickTechSystems.Application.Services.Interfaces
{
    public interface IDrawerService : IBaseService<DrawerDTO>
    {
        Task<DrawerDTO?> GetCurrentDrawerAsync();
        Task<DrawerDTO> OpenDrawerAsync(decimal openingBalance, string cashierId, string cashierName);
        Task<DrawerDTO> CloseDrawerAsync(decimal finalBalance, string? notes);
        Task<DrawerDTO> AddCashTransactionAsync(decimal amount, bool isIn);
        Task<IEnumerable<DrawerTransactionDTO>> GetDrawerHistoryAsync(int drawerId);
    }
}