using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Windows;

namespace EZcade_Client
{



    public class EZcade_Connection_Handler
    {
        string EzcadeIp = "127.0.0.1";
        int EzcadePort = 1000;
        TcpListener Ezcadeserver;


        TcpClient Ezcadeclient;
        NetworkStream Ezcadestream;
            

        public EZcade_Connection_Handler()
        {
            try
            {
                TcpListener Ezcadeserver = new TcpListener(IPAddress.Parse(EzcadeIp), EzcadePort);
                Ezcadeserver.Start();
                Ezcadeserver.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                Ezcadeclient = Ezcadeserver.AcceptTcpClient();
                Ezcadestream = Ezcadeclient.GetStream();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Client error: {ex.Message}");
            }
        }
        public String Listen()
        {
            try
            {
                byte[] buffer = new byte[1024];
                int bytesRead = Ezcadestream.Read(buffer, 0, buffer.Length);
                string receivedData = Encoding.ASCII.GetString(buffer, 0, bytesRead);
                return receivedData;
            }
            catch
            {
                return "ERROR";
            }
            
        }
        public void SendAndPrint(String response) 
        {
            try
            {

                byte[] responseBytes = Encoding.ASCII.GetBytes(response);
                Ezcadestream.Write(responseBytes, 0, responseBytes.Length);
                //if(response != "start requesting in EZcade")
                //{

                //}
                MessageBox.Show("printed " + response);



            }
            catch (Exception e){
                MessageBox.Show(e.Message);
             }
        }
        public void Cleanup()
        {
            try {
                if (Ezcadeserver != null)
                { 
                    Ezcadeserver.Stop();
                }
            }
            catch{}
        }
    }
}


