namespace EZcade_Client;

public class SerialNumberQuery
{
    public string ProductModel { get; set; } = "";
    public string? PcbModel { get; set; } 
    public string Type { get; set; } = ""; // "PR" or "PB"
    public string CodeType { get; set; } = ""; // "SN" or "DM"
    public int Count { get; set; }

    public SerialNumberQuery(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            throw new ArgumentException("Input cannot be null or empty.");

        var parts = input.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Type = parts[0];
        if (Type == "PR")
        {
            if (parts.Length != 4)
                throw new FormatException("Input must contain exactly four parts: Model, Type, CodeType, and Count.");

            ProductModel = parts[1];
            CodeType = parts[2];

            if (!int.TryParse(parts[3], out int count))
                throw new FormatException("The last part must be a valid integer.");

            Count = count;

        }
        else if (Type == "PB")
        {
            if (parts.Length != 5)
                throw new FormatException("Input must contain exactly five parts: Model, PcbductModel, Type, CodeType, and Count.");
            ProductModel = parts[1];
            PcbModel = parts[2];
            CodeType = parts[3];
            if (!int.TryParse(parts[4], out int count))
                throw new FormatException("The last part must be a valid integer.");
            Count = count;
        }
        else
        {
            throw new ArgumentException("Invalid Type. Expected 'PR' or 'PB'.");

        }
    }

    public string GetSerialNumber(string serialNumber)
    {
        if (Type == "PR")
        {
            if (CodeType == "DM")
            {
                serialNumber = "PR" + serialNumber;
            }

        }
        else if (Type == "PB")
        {
            if (CodeType == "DM")
            {
                serialNumber = "PB" + serialNumber;
            }
        }

        return serialNumber;
    }
    

}


