using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Threading;
using Main_Server.DTOs.Ezcade;

namespace EZcade_Client
{
    public class Connection_Handler
    {
        #region Fields
        public string serverIp = "127.0.0.1";
        //public string serverIp = "213.207.200.115";
        //public string serverIp = "192.168.1.122";
        public int serverPort = 1001;
        public bool server_connection_status { get; set; }
        public TcpClient client;
        private NetworkStream stream;
        private DispatcherTimer timer;
        #endregion

        #region Constructor
        public Connection_Handler()
        {
            try
            {
                client = new TcpClient();
                client.Connect(serverIp, serverPort);
                stream = client.GetStream();
                server_connection_status = false;

            }
            catch (Exception ex)
            {
                MessageBox.Show($"Client error: {ex.Message}");
            }
        }
        #endregion

        #region Methods

        public BatchListDto_Ezcade GetBatchList()
        {
            var Request = new JsonObject();
            Request["requestType"] = "batchList";
            var response = Request_Json(Request);

            string json = response["data"]!.GetValue<string>();
            return JsonSerializer.Deserialize<BatchListDto_Ezcade>(json);
        }

        #region Encryption Methods
        public static string EncryptData(string data)
        {
            using (Aes aesAlg = Aes.Create())
            {
                aesAlg.Key = Convert.FromBase64String("5EjbJ1cMefXwTG8vqn5WPkpQbV5LZp89JaXbkGgNctM=");
                aesAlg.IV = Convert.FromBase64String("n1QSgyiWu1Efo7N7EbggMA==");
                aesAlg.Mode = CipherMode.CBC;
                aesAlg.Padding = PaddingMode.PKCS7;

                using (ICryptoTransform encryptor = aesAlg.CreateEncryptor(aesAlg.Key, aesAlg.IV))
                {
                    byte[] dataBytes = Encoding.UTF8.GetBytes(data);
                    byte[] encryptedData = encryptor.TransformFinalBlock(dataBytes, 0, dataBytes.Length);
                    return Convert.ToBase64String(encryptedData);
                }
            }
        }

        public static string DecryptData(string encryptedData)
        {
            using (Aes aesAlg = Aes.Create())
            {
                aesAlg.Key = Convert.FromBase64String("5EjbJ1cMefXwTG8vqn5WPkpQbV5LZp89JaXbkGgNctM=");
                aesAlg.IV = Convert.FromBase64String("n1QSgyiWu1Efo7N7EbggMA==");
                aesAlg.Mode = CipherMode.CBC;
                aesAlg.Padding = PaddingMode.PKCS7;

                using (ICryptoTransform decryptor = aesAlg.CreateDecryptor(aesAlg.Key, aesAlg.IV))
                {
                    byte[] encryptedBytes = Convert.FromBase64String(encryptedData);
                    byte[] decryptedData = decryptor.TransformFinalBlock(encryptedBytes, 0, encryptedBytes.Length);
                    return Encoding.UTF8.GetString(decryptedData);
                }
            }
        }

        #endregion

        #region Connection Methods
        public JsonObject Request_Json(JsonObject request)
        {
            string request_jsonString = JsonSerializer.Serialize(request);
            request_jsonString = EncryptData(request_jsonString);
            byte[] dataToSend = Encoding.UTF8.GetBytes(request_jsonString);
            stream.Write(dataToSend, 0, dataToSend.Length);

            byte[] buffer = new byte[1024];
            int bytesRead = stream.Read(buffer, 0, buffer.Length);

            string response_jsonString = Encoding.ASCII.GetString(buffer, 0, bytesRead);
            response_jsonString = DecryptData(response_jsonString);
            JsonObject response = JsonSerializer.Deserialize<JsonObject>(response_jsonString);
            return response;
        }
        public bool Status_Check()
        {
            try
            {
                var Request = new JsonObject();
                Request["requestType"] = "status";

                string jsonString = JsonSerializer.Serialize(Request);
                jsonString = EncryptData(jsonString);

                byte[] dataToSend = Encoding.UTF8.GetBytes(jsonString);
                if (stream != null)
                {
                    stream.Write(dataToSend, 0, dataToSend.Length);
                }
                else
                {
                    return false;
                }

                byte[] buffer = new byte[1024];
                int bytesRead = 0;

                bytesRead = stream.Read(buffer, 0, buffer.Length);
                if (bytesRead == 0)
                {
                    server_connection_status = false;
                    return false;
                }

                string response_jsonString = Encoding.ASCII.GetString(buffer, 0, bytesRead);
                response_jsonString = DecryptData(response_jsonString);
                var Response = JsonSerializer.Deserialize<JsonObject>(response_jsonString);

                if (Response["status"].ToString() == "is_connected")
                {
                    server_connection_status = true;
                    return true;
                }

                server_connection_status = false;
                return false;

            }
            catch (NullReferenceException)
            {
                server_connection_status = false;
                return false;
            }
            catch (System.IO.IOException)
            {
                server_connection_status = false;
                return false;
            }
        }
        public void CloseConnection()
        {
            try
            {
                if (client != null && client.Connected)
                {
                    client.Close();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error closing connection: {ex.Message}");
            }
        }

        public void Cleanup()
        {
            CloseConnection();
        }
        #endregion
        
        #endregion
    }
}
