namespace QuickTechSystems.Application.DTOs
{
    public class PagedResultDTO<T>
    {
        public IEnumerable<T> Items { get; set; } = new List<T>();
        public int TotalCount { get; set; }
        public int PageNumber { get; set; }
        public int PageSize { get; set; }
        public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
        public bool HasPrevious => PageNumber > 1;
        public bool HasNext => PageNumber < TotalPages;
        public int StartRecord => ((PageNumber - 1) * PageSize) + 1;
        public int EndRecord => Math.Min(PageNumber * PageSize, TotalCount);
    }
}