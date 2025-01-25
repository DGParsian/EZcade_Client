using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace EZcade_Client
{
    public class Connection_Handler
    {
        public string serverIp = "127.0.0.1";
        //public string serverIp = "213.207.200.115";
        //public string serverIp = "192.168.1.122";

        public int serverPort = 1001;         
        public bool server_connection_status { get; set; }



        public TcpClient client;
        private NetworkStream stream;
        private DispatcherTimer timer;



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

        public JsonObject Request_Json(JsonObject request)
        {
            string request_jsonString = JsonSerializer.Serialize(request);
            byte[] dataToSend = Encoding.UTF8.GetBytes(request_jsonString);
            stream.Write(dataToSend, 0, dataToSend.Length);


            byte[] buffer = new byte[1024];
            int bytesRead = stream.Read(buffer, 0, buffer.Length);
            
            string response_jsonString = Encoding.ASCII.GetString(buffer, 0, bytesRead);
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
                var Response = JsonSerializer.Deserialize<JsonObject>(response_jsonString);

                //testDatabase();
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
    }
}
