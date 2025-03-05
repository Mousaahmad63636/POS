namespace QuickTechSystems.Application.DTOs
{
    public class CategoryDTO : BaseDTO
    {
        public int CategoryId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public int ProductCount { get; set; }
    }
}