
using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Windows;

namespace EZcade_Client
{
    public class EZcade_Connection_Handler
    {
        #region Fields
        string EzcadeIp = "127.0.0.1";
        int EzcadePort = 1000;
        TcpListener Ezcadeserver;

        TcpClient Ezcadeclient;
        NetworkStream Ezcadestream;
        #endregion

        #region Constructor
        public EZcade_Connection_Handler()
        {
            try
            {
                Ezcadeserver = new TcpListener(IPAddress.Parse(EzcadeIp), EzcadePort);
                Ezcadeserver.Start();
                Ezcadeserver.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

            }
            catch (Exception ex)
            {
                MessageBox.Show($"Client error: {ex.Message}");
            }
        }
        #endregion

        #region Methods
        public void init()
        {
            Ezcadeclient = Ezcadeserver.AcceptTcpClient();
            Ezcadestream = Ezcadeclient.GetStream();
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

        public void SendAndPrint(String response, bool showMessage = false)
        {
            try
            {
                byte[] responseBytes = Encoding.ASCII.GetBytes(response);
                Ezcadestream.Write(responseBytes, 0, responseBytes.Length);
                if (showMessage)
                {
                    MessageBox.Show("printed " + response);
                }
                
            }
            catch (Exception e)
            {
                MessageBox.Show(e.Message);
            }
        }

        public void Cleanup()
        {
            try
            {
                if (Ezcadeclient != null)
                {
                    Ezcadeclient.Close();
                    Ezcadeclient.Dispose();
                }
                if (Ezcadeserver != null)
                {
                    Ezcadeserver.Stop();
                    Ezcadeserver.Dispose();
                }
            }
            catch { }
        }
        #endregion
    }
}


