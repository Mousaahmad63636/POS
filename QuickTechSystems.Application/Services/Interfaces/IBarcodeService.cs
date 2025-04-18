// QuickTechSystems.Application/Services/Interfaces/IBarcodeService.cs
namespace QuickTechSystems.Application.Services.Interfaces
{
    public interface IBarcodeService
    {
        byte[] GenerateBarcode(string barcodeData);
        byte[] GenerateBarcode(string barcodeData, int width, int height);
    }
}