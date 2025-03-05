using AutoMapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using QuickTechSystems.Application.DTOs;
using QuickTechSystems.Application.Events;
using QuickTechSystems.Application.Services.Interfaces;
using QuickTechSystems.Domain.Entities;
using QuickTechSystems.Domain.Enums;
using QuickTechSystems.Domain.Interfaces.Repositories;

namespace QuickTechSystems.Application.Services
{
    public class TransactionService : BaseService<Transaction, TransactionDTO>, ITransactionService
    {
        private readonly IProductService _productService;

        public TransactionService(
            IUnitOfWork unitOfWork,
            IMapper mapper,
            IProductService productService,
            IEventAggregator eventAggregator)
            : base(unitOfWork, mapper, unitOfWork.Transactions, eventAggregator)
        {
            _productService = productService;
        }

        public async Task<IEnumerable<TransactionDTO>> GetByCustomerAsync(int customerId)
        {
            var transactions = await _repository.Query()
                .Include(t => t.Customer)
                .Include(t => t.TransactionDetails)
                .ThenInclude(td => td.Product)
                .Where(t => t.CustomerId == customerId)
                .ToListAsync();
            return _mapper.Map<IEnumerable<TransactionDTO>>(transactions);
        }

        public async Task<TransactionDTO> ProcessPaymentTransactionAsync(TransactionDTO transaction)
        {
            if (transaction == null)
                throw new ArgumentNullException(nameof(transaction));

            var entity = _mapper.Map<Transaction>(transaction);
            await _repository.AddAsync(entity);
            await _unitOfWork.SaveChangesAsync();

            return _mapper.Map<TransactionDTO>(entity);
        }
        public async Task<int> GetLatestTransactionIdAsync()
        {
            var latestTransaction = await _repository.Query()
                .OrderByDescending(t => t.TransactionId)
                .FirstOrDefaultAsync();

            return latestTransaction?.TransactionId ?? 0;
        }
        public async Task<TransactionDTO> ProcessRefundAsync(TransactionDTO transaction)
        {
            using var dbTransaction = await _unitOfWork.BeginTransactionAsync();
            try
            {
                transaction.TransactionType = TransactionType.Return;
                transaction.TransactionDate = DateTime.Now;
                transaction.Status = TransactionStatus.Completed;

                var entity = _mapper.Map<Transaction>(transaction);
                await _repository.AddAsync(entity);
                await _unitOfWork.SaveChangesAsync();

                // Update stock levels
                foreach (var detail in transaction.Details)
                {
                    await _productService.UpdateStockAsync(detail.ProductId, detail.Quantity);
                }

                await dbTransaction.CommitAsync();
                return _mapper.Map<TransactionDTO>(entity);
            }
            catch
            {
                await dbTransaction.RollbackAsync();
                throw;
            }
        }
        public async Task<TransactionDTO?> GetLastTransactionAsync()
        {
            try
            {
                var lastTransaction = await _repository.Query()
                    .Include(t => t.Customer)
                    .Include(t => t.TransactionDetails)
                        .ThenInclude(td => td.Product)
                    .Where(t => t.Status == TransactionStatus.Completed)
                    .OrderByDescending(t => t.TransactionDate)
                    .FirstOrDefaultAsync();

                return _mapper.Map<TransactionDTO>(lastTransaction);
            }
            catch (Exception)
            {
                throw;
            }
        }

        public async Task<IEnumerable<TransactionDTO>> GetByDateRangeAsync(DateTime startDate, DateTime endDate)
        {
            try
            {
                var transactions = await _repository.Query()
                    .Include(t => t.Customer)
                    .Include(t => t.TransactionDetails)
                        .ThenInclude(td => td.Product)
                            .ThenInclude(p => p.Category)  // Include Category information
                    .Where(t => t.TransactionDate >= startDate &&
                               t.TransactionDate <= endDate &&
                               t.Status == TransactionStatus.Completed)
                    .OrderByDescending(t => t.TransactionDate)
                    .ToListAsync();

                return _mapper.Map<IEnumerable<TransactionDTO>>(transactions);
            }
            catch (Exception)
            {
                throw;
            }
        }

        public async Task<IEnumerable<TransactionDTO>> GetByTypeAsync(TransactionType type)
        {
            var transactions = await _repository.Query()
                .Include(t => t.Customer)
                .Include(t => t.TransactionDetails)
                .ThenInclude(td => td.Product)
                .Where(t => t.TransactionType == type)
                .ToListAsync();
            return _mapper.Map<IEnumerable<TransactionDTO>>(transactions);
        }

        public async Task<bool> UpdateStatusAsync(int id, TransactionStatus status)
        {
            var transaction = await _repository.GetByIdAsync(id);
            if (transaction == null) return false;

            transaction.Status = status;
            await _repository.UpdateAsync(transaction);
            await _unitOfWork.SaveChangesAsync();
            return true;
        }

        public async Task<decimal> GetTotalSalesAsync(DateTime startDate, DateTime endDate)
        {
            return await _repository.Query()
                .Where(t => t.TransactionType == TransactionType.Sale
                       && t.Status == TransactionStatus.Completed
                       && t.TransactionDate >= startDate
                       && t.TransactionDate <= endDate)
                .SumAsync(t => t.TotalAmount);
        }

        public async Task<TransactionDTO> ProcessSaleAsync(TransactionDTO transactionDto)
        {
            using var dbTransaction = await _unitOfWork.BeginTransactionAsync();
            try
            {
                foreach (var detail in transactionDto.Details)
                {
                    var product = await _productService.GetByIdAsync(detail.ProductId);
                    if (product == null || product.CurrentStock < detail.Quantity)
                    {
                        throw new InvalidOperationException($"Insufficient stock for product: {product?.Name ?? "Unknown"}");
                    }
                }

                var transaction = _mapper.Map<Transaction>(transactionDto);
                await _repository.AddAsync(transaction);
                await _unitOfWork.SaveChangesAsync();

                foreach (var detail in transactionDto.Details)
                {
                    await _productService.UpdateStockAsync(detail.ProductId, -detail.Quantity);

                    await _unitOfWork.Context.Set<InventoryHistory>().AddAsync(new InventoryHistory
                    {
                        ProductId = detail.ProductId,
                        QuantityChanged = -detail.Quantity,
                        OperationType = TransactionType.Sale,
                        Date = DateTime.Now,
                        Reference = $"Sale-{transaction.TransactionId}",
                        Notes = $"Sale transaction #{transaction.TransactionId}"
                    });
                }

                await _unitOfWork.SaveChangesAsync();
                await dbTransaction.CommitAsync();

                return _mapper.Map<TransactionDTO>(transaction);
            }
            catch
            {
                await dbTransaction.RollbackAsync();
                throw;
            }
        }

        public async Task<TransactionDTO?> GetTransactionForReturnAsync(int transactionId)
        {
            var transaction = await _repository.Query()
                .Include(t => t.Customer)
                .Include(t => t.TransactionDetails)
                    .ThenInclude(td => td.Product)
                .FirstOrDefaultAsync(t => t.TransactionId == transactionId);

            if (transaction == null)
                return null;

            if (transaction.Status != TransactionStatus.Completed ||
                transaction.TransactionDetails.All(td => td.IsReturned))
                return null;

            return _mapper.Map<TransactionDTO>(transaction);
        }

        public async Task<TransactionDTO> ProcessReturnAsync(int originalTransactionId, List<ReturnItemDTO> returnItems)
        {
            using var dbTransaction = await _unitOfWork.BeginTransactionAsync();
            try
            {
                var originalTransaction = await _repository.Query()
                    .Include(t => t.Customer)
                    .Include(t => t.TransactionDetails)
                        .ThenInclude(td => td.Product)
                    .FirstOrDefaultAsync(t => t.TransactionId == originalTransactionId);

                if (originalTransaction == null)
                    throw new InvalidOperationException("Original transaction not found");

                decimal totalRefundAmount = 0;

                foreach (var returnItem in returnItems)
                {
                    var detail = originalTransaction.TransactionDetails
                        .FirstOrDefault(d => d.ProductId == returnItem.ProductId);

                    if (detail == null)
                        throw new InvalidOperationException($"Product {returnItem.ProductId} not found in original transaction");

                    int availableToReturn = detail.Quantity - detail.ReturnedQuantity;
                    if (returnItem.QuantityToReturn > availableToReturn)
                        throw new InvalidOperationException($"Cannot return more items than available. Available: {availableToReturn}");

                    detail.ReturnedQuantity += returnItem.QuantityToReturn;
                    detail.IsReturned = detail.ReturnedQuantity == detail.Quantity;
                    detail.ReturnDate = DateTime.Now;
                    detail.ReturnReason = returnItem.ReturnReason;

                    await _productService.UpdateStockAsync(
                        returnItem.ProductId,
                        returnItem.QuantityToReturn);

                    totalRefundAmount += returnItem.RefundAmount;

                    await _unitOfWork.Context.Set<InventoryHistory>().AddAsync(new InventoryHistory
                    {
                        ProductId = returnItem.ProductId,
                        QuantityChanged = returnItem.QuantityToReturn,
                        OperationType = TransactionType.Return,
                        Date = DateTime.Now,
                        Reference = $"Return-{originalTransactionId}",
                        Notes = returnItem.ReturnReason
                    });
                }

                originalTransaction.TotalAmount -= totalRefundAmount;

                await _unitOfWork.SaveChangesAsync();
                await dbTransaction.CommitAsync();

                return _mapper.Map<TransactionDTO>(originalTransaction);
            }
            catch
            {
                await dbTransaction.RollbackAsync();
                throw;
            }
        }

        public override async Task<IEnumerable<TransactionDTO>> GetAllAsync()
        {
            var transactions = await _repository.Query()
                .Include(t => t.Customer)
                .Include(t => t.TransactionDetails)
                    .ThenInclude(td => td.Product)
                .ToListAsync();

            return _mapper.Map<IEnumerable<TransactionDTO>>(transactions);
        }

        public override async Task<TransactionDTO?> GetByIdAsync(int id)
        {
            var transaction = await _repository.Query()
                .Include(t => t.Customer)
                .Include(t => t.TransactionDetails)
                    .ThenInclude(td => td.Product)
                .FirstOrDefaultAsync(t => t.TransactionId == id);

            return _mapper.Map<TransactionDTO>(transaction);
        }
    }
}