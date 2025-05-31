using System;
using System.Collections.Generic;

namespace QuickTechSystems.Application.Services.Interfaces
{
    /// <summary>
    /// Helper class for paginated results
    /// </summary>
    /// <typeparam name="T">The type of items in the result</typeparam>
    public class PagedResult<T>
    {
        /// <summary>
        /// The items for the current page
        /// </summary>
        public IEnumerable<T> Items { get; set; } = new List<T>();

        /// <summary>
        /// Total number of items across all pages
        /// </summary>
        public int TotalCount { get; set; }

        /// <summary>
        /// Current page number (1-based)
        /// </summary>
        public int PageNumber { get; set; }

        /// <summary>
        /// Number of items per page
        /// </summary>
        public int PageSize { get; set; }

        /// <summary>
        /// Total number of pages
        /// </summary>
        public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);

        /// <summary>
        /// Whether there is a previous page
        /// </summary>
        public bool HasPreviousPage => PageNumber > 1;

        /// <summary>
        /// Whether there is a next page
        /// </summary>
        public bool HasNextPage => PageNumber < TotalPages;

        /// <summary>
        /// Starting index for the current page (0-based)
        /// </summary>
        public int StartIndex => (PageNumber - 1) * PageSize;

        /// <summary>
        /// Ending index for the current page (0-based)
        /// </summary>
        public int EndIndex => Math.Min(StartIndex + PageSize - 1, TotalCount - 1);
    }
}