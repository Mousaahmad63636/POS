using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using QuickTechSystems.Application.DTOs;

namespace QuickTechSystems.Application.Events
{
    public class EntityChangedEvent<T>
    {
        public string Action { get; set; }
        public T Entity { get; set; }

        public EntityChangedEvent(string action, T entity)
        {
            Action = action;
            Entity = entity;
        }
    }
    public class LowStockWarningEvent
    {
        public ProductDTO Product { get; }
        public int MinimumStock { get; }

        public LowStockWarningEvent(ProductDTO product, int minimumStock)
        {
            Product = product;
            MinimumStock = minimumStock;
        }
    }
}
