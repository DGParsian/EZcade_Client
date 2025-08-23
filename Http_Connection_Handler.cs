using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using EZcade_Client.EzcadeDtos;

namespace EZcade_Client
{
    public class HttpConnectionHandler
    {
        private readonly HttpClient _httpClient;
        //private readonly string? _base_address = "http://192.168.1.243:8080/";

        private readonly string? _base_address = "http://127.0.0.1:5218/";
        private readonly string? _base_route = "api/ezcade_client/main/";
        public readonly string? serverIp = "localhost";
        public readonly string? serverPort = "5218";
        private string? _token;
        public bool ServerConnectionStatus { get; private set; }

        public HttpConnectionHandler()
        {
            _httpClient = new HttpClient
            {
                BaseAddress = new Uri(_base_address)
            };
        }

        public bool LoginAsync(string username, string password)
        {
            var request = new { Username = username, Password = password };
            var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");

            var response =  _httpClient.PostAsync("login", content).GetAwaiter().GetResult();

            if (!response.IsSuccessStatusCode)
            {
                ServerConnectionStatus = false;
                return false;
            }

            var json =  response.Content.ReadAsStringAsync();
            var root = JsonSerializer.Deserialize<JsonObject>(json.Result);
            _token = root["myToken"]?.GetValue<string>();

            if (!string.IsNullOrEmpty(_token))
            {
                _httpClient.DefaultRequestHeaders.Add("Authorization", _token);
                ServerConnectionStatus = true;
                return true;
            }

            ServerConnectionStatus = false;
            return false;
        }

        public bool StatusCheckAsync()
        {
            var response = _httpClient.GetAsync(_base_route).Result;
            ServerConnectionStatus = response.IsSuccessStatusCode;
            return ServerConnectionStatus;
        }

        public ListDto<BatchDto> GetBatchListAsync()
        {
            var response = _httpClient.GetAsync(_base_route + "Batches").GetAwaiter().GetResult();

            if (!response.IsSuccessStatusCode)
            {
                ServerConnectionStatus = false;
                return null;
            }

            var json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

            var result = JsonSerializer.Deserialize<ListDto<BatchDto>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            return result;
        }





        public async Task<JsonObject?> SendSerialNumberWithBatchAsync(string serialInput, string? batch)
        {
            var payload = new JsonObject
            {
                ["requestType"] = "serialNumber_withBatch",
                ["model"] = serialInput,
                ["batch"] = batch
            };

            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync("api/server/serialNumber_withBatch", content);

            if (!response.IsSuccessStatusCode)
                return null;

            string jsonResponse = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<JsonObject>(jsonResponse);
        }
        public string GetSerialNumber(SerialNumberQuery Query, string batchName)
        {
            var request = new SerialNumberRequest
            {
                BatchName = batchName,
                ProductModel = Query.ProductModel,
                Type = Query.Type,



                
            };

            if (Query.PcbModel != null)
                request.PcbModel = Query.PcbModel;

            var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");
            var response = _httpClient.PostAsync(_base_route + "serialNumber", content).GetAwaiter().GetResult();
            string res = response.ToString();

            if (!response.IsSuccessStatusCode)
                return "";

            var resultJson = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            var dto = JsonSerializer.Deserialize<SerialNumberDto>(resultJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            return dto?.SerialNumber ?? "";
        }

        public bool Activation (string serialNumber, string fullType)
        {

            var url = $"{_base_route}activation/{fullType}/{serialNumber}";
            var response = _httpClient.PostAsync(url, null).GetAwaiter().GetResult();

      
            return response.IsSuccessStatusCode;
        }

        
        public void Cleanup()
        {
            _httpClient.Dispose();
        }

        
    }
}
