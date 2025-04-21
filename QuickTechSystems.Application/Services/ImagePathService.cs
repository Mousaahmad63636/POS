using System;
using System.IO;
using Microsoft.Extensions.Configuration;

namespace QuickTechSystems.Application.Services
{
    public interface IImagePathService
    {
        string GetProductImagesDirectory();
        string SaveProductImage(string sourcePath);
        bool DeleteProductImage(string imagePath);
        string GetFullImagePath(string relativePath);
    }

    public class ImagePathService : IImagePathService
    {
        private readonly string _baseProductImagePath;
        
        public ImagePathService(IConfiguration configuration)
        {
            try
            {
                // Get image storage path from configuration or use default
                string configPath = null;

                // First, try to get it from configuration section
                var imageStorageSection = configuration.GetSection("ImageStorage");
                if (imageStorageSection.Exists())
                {
                    configPath = imageStorageSection["ProductImagesPath"];
                }

                if (!string.IsNullOrEmpty(configPath))
                {
                    _baseProductImagePath = configPath;
                }
                else
                {
                    // Default to application directory
                    _baseProductImagePath = Path.Combine(
                        AppDomain.CurrentDomain.BaseDirectory, 
                        "ProductImages"
                    );
                }
                
                // Ensure directory exists
                Directory.CreateDirectory(_baseProductImagePath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error initializing ImagePathService: {ex.Message}");
                // Fallback to a safe path
                _baseProductImagePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ProductImages");
                Directory.CreateDirectory(_baseProductImagePath);
            }
        }
        
        public string GetProductImagesDirectory()
        {
            return _baseProductImagePath;
        }

        public string SaveProductImage(string sourcePath)
        {
            if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath))
                throw new FileNotFoundException("Source image file not found", sourcePath);

            // Generate unique filename using timestamp and original filename
            var originalFileName = Path.GetFileName(sourcePath);
            var timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
            var uniqueFileName = $"{timestamp}_{originalFileName}";
            var destinationPath = Path.Combine(_baseProductImagePath, uniqueFileName);

            // Copy the file
            File.Copy(sourcePath, destinationPath, true);
            
            // Return the relative path to store in the database
            return uniqueFileName;
        }

        public bool DeleteProductImage(string imagePath)
        {
            if (string.IsNullOrEmpty(imagePath))
                return false;
                
            try
            {
                var fullPath = GetFullImagePath(imagePath);
                if (File.Exists(fullPath))
                {
                    File.Delete(fullPath);
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error deleting image: {ex.Message}");
                return false;
            }
        }
        
        public string GetFullImagePath(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath))
                return string.Empty;
                
            if (Path.IsPathRooted(relativePath))
                return relativePath;
                
            return Path.Combine(_baseProductImagePath, relativePath);
        }
    }
}