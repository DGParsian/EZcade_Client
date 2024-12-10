using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace EZcade_Client
{
    public class Connection_Handler
    {
        //public string serverIp = "127.0.0.1";
        public string serverIp = "213.207.200.115";

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

        //private void startEzcadeSucket()
        //{
        //    string EzcadeIp = "127.0.0.1"; 
        //    int EzcadePort = 1000;


        //    var EZcade_Listen_Thread = new Thread(() =>
        //    {

        //        TcpListener Ezcadeserver = new TcpListener(IPAddress.Parse(EzcadeIp), EzcadePort);
        //        Ezcadeserver.Start();
        //        Ezcadeserver.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        //        TcpClient Ezcadeclient = Ezcadeserver.AcceptTcpClient();
        //        NetworkStream Ezcadestream = Ezcadeclient.GetStream();

        //        while (true)
        //        {
                    
        //            byte[] buffer = new byte[1024];
        //            int bytesRead = Ezcadestream.Read(buffer, 0, buffer.Length);
        //            string receivedData = Encoding.ASCII.GetString(buffer, 0, bytesRead);


            //        MessageBox.Show($"Reqesting: {receivedData}");

            //        if (receivedData == "TCP:Give me string")
            //        {
            //            string response = Request(receivedData);

            //            byte[] responseBytes = Encoding.ASCII.GetBytes(response);
            //            Ezcadestream.Write(responseBytes, 0, responseBytes.Length);

            //            MessageBox.Show(response);
            //        }
            //        else
            //        {
            //            MessageBox.Show("Unexpected command received!");
            //        }
            //    }
                
            //});
        //    EZcade_Listen_Thread.Start();

        //}

        public string Request(string messageToSend)
        {
            try
            {
                if (server_connection_status)
                {
                    byte[] dataToSend = Encoding.UTF8.GetBytes(messageToSend);
                    stream.Write(dataToSend, 0, dataToSend.Length);


                    byte[] buffer = new byte[1024];
                    int bytesRead = stream.Read(buffer, 0, buffer.Length);
                    string response_string = Encoding.UTF8.GetString(buffer, 0, bytesRead);

                    return response_string;
                }
                else
                {
                    MessageBox.Show("Unable to send message, server connection is not established.");
                }
            }
            catch (Exception ex)
            {
                
            }
            return "ERROR";
        }

        
        public bool Status_Check()
        {
            try
            {
                
                byte[] dataToSend = Encoding.UTF8.GetBytes("status");
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
                
                var timeoutThread = new Thread(() =>
                {

                    if (stream != null)
                    {
                        bytesRead = stream.Read(buffer, 0, buffer.Length);
                    }
   
                });

                timeoutThread.Start();  
                Thread.Sleep(1000);
                

                if (bytesRead == 0)
                {
                    server_connection_status = false;
                    return false;
                }

                string response = Encoding.UTF8.GetString(buffer, 0, bytesRead);


                if (response.Equals("is_connected"))
                {
                    server_connection_status = true;
                    return true;
                }
                else
                {
                    server_connection_status = false;
                    return false;
                }
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
