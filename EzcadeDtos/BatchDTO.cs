
namespace EZcade_Client.EzcadeDtos;

public class BatchDto
{
    public string BatchName { get; set; } = null!;
    public string SerialNumber { get; set; }

    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public string DispatchDate { get; set; }
    public string? Location { get; set; }
    public List<BatchPartDto> BatchParts { get; set; }


    public BatchDto()
    {

    }
    
}


