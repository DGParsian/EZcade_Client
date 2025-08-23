
namespace EZcade_Client.EzcadeDtos;

public class SerialNumberRequest
{
    public string BatchName { get; set; } = "";
    public string ProductModel { get; set; } = "";
    public string? PcbModel { get; set; }
    public string Type { get; set; } = "";


}


