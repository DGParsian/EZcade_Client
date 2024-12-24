using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.IO;
using System;
using System.Text.Json.Nodes;

namespace EZcade_Client
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    /// 
   

    //private Connection
    public partial class MainWindow : Window
    {
        public Connection_Handler connection_handler { get; internal set; }
        public EZcade_Connection_Handler ezcade_Connection_Handler { get; internal set; }


        Thread EZcade_Listen_Thread;
        Thread EZcade_Start_Thread;

        public MainWindow()
        {
            InitializeComponent();
            connection_handler = new Connection_Handler();

            LoginWindow loginWindow = new LoginWindow(connection_handler);
            loginWindow.ShowDialog();


            if (connection_handler.Status_Check())
            {
                //MessageBox.Show("connected");
                Show_ip();
            }
            else {
                MessageBox.Show("not connected");
            }
            Init_Form();
            //Handle_EZ_Request();
            
        }
        //private string login()
        //{
           
            
        //    if ( == true)
        //    {
        //        try
        //        {
                    
        //            // Send login credentials to the server
        //            writer.WriteLine(username);
        //            writer.WriteLine(password);

        //            // Handle server response
        //            string response = reader.ReadLine();
        //            if (response == "Admin Login Successful")
        //            {
        //                MessageBox.Show("Welcome, Admin!");
        //                // Proceed to admin functionality
        //            }
        //            else if (response == "User Login Successful")
        //            {
        //                MessageBox.Show("Welcome, Regular User!");
        //                // Proceed to user functionality
        //            }
        //            else
        //            {
        //                MessageBox.Show("Invalid credentials. Connection will close.", "Login Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        
        //            }
        //        }
        //        catch (Exception ex)
        //        {
        //            MessageBox.Show($"An error occurred: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    
        //        }
        //    }
           

        //}



        CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();

        private void Init_Form()
        {
            EZcade_Listen_Thread = new Thread(() =>
            {
                try
                {
                    while (!_cancellationTokenSource.Token.IsCancellationRequested)
                    {
                        string request = ezcade_Connection_Handler.Listen();
                        
                        Request_recived(request);

                       
                    }

                        
                }
                catch{}
            });
            EZcade_Start_Thread = new Thread(() =>
            {
                try
                {

                    //bool Is_connected = true;
                    //var timeoutThread = new Thread(() =>
                    //{
                    //    ezcade_Connection_Handler = new EZcade_Connection_Handler();
                    //});


                    //while (!_cancellationTokenSource.Token.IsCancellationRequested)
                    //{

                    //    if (timeoutThread.IsAlive) { 
                    //        timeoutThread.Resume();
                    //    }
                    //    else
                    //    {
                    //        timeoutThread.Start();
                    //    }
                    //    Thread.Sleep(1000);
                    //    timeoutThread.Suspend();
                    //}






                    if (!_cancellationTokenSource.Token.IsCancellationRequested)
                    {
                        ezcade_Connection_Handler = new EZcade_Connection_Handler();
                        ezcade_Connection_Handler.init();
                        EZcade_Listen_Thread.Start();
                    }
                }
                catch { }
            });


            Edit_Button.Content = "Connect";
            serial_number_TextBox.Text = "press connect and then start requesting in EZcade";
            print_Buttun.Visibility = Visibility.Hidden;
            Cancel_Button.Visibility = Visibility.Hidden;
             


        }
        private TaskCompletionSource<bool> _buttonClickedTcs;

        private void Request_recived(string request)
        {
            if (request != "")
            {
                if (request != "ERROR")
                {
                    var Request = new JsonObject();
                    if (request == "TCP:Give me string")
                    {
                        Request["requestType"] = "test";

                    }
                    else
                    {
                        Request["requestType"] = "serialNumber";
                        Request["model"] = request;
                    }




                    //string response = connection_handler.Request(request);
                    var Response = connection_handler.Request_Json(Request);

                    Dispatcher.Invoke(() =>
                    {
                        serial_number_TextBox.Text = Response["serialNumber"].ToString();
                        Enable_serialNumber_inteaction();
                    });
                }
            }

             
            //serial_number_TextBox.Text = response;
            //Enable_serialNumber_inteaction();
            //WaitForButtonPressAsync();
            //Disable_serialNumber_inteaction();
        }
        private async void WaitForButtonPressAsync()
        {
            _buttonClickedTcs = new TaskCompletionSource<bool>();
            await _buttonClickedTcs.Task;
        }
        private void Show_ip()
        {
            string ip = connection_handler.serverIp;
            string port = connection_handler.serverPort.ToString();

            ip_Lable.Content = "connected through " + ip + ":" + port;
        }
        private void print_Buttun_Click(object sender, RoutedEventArgs e)
        {
            ezcade_Connection_Handler.SendAndPrint(serial_number_TextBox.Text);
            serial_number_TextBox.Text = "start requesting in EZcade";
            EZcade_Listen_Thread = new Thread(() =>
            {
                try
                {
                    string request = ezcade_Connection_Handler.Listen();
                    Request_recived(request);
                }
                catch
                {
                    // Handle exceptions if needed
                }
            });

            EZcade_Listen_Thread.Start();


            //_buttonClickedTcs.TrySetResult(true);

        }
        private void Edit_Button_Click(object sender, RoutedEventArgs e)
        {
            if (Edit_Button.Content.Equals("Edit"))
            {
                serial_number_TextBox.IsEnabled = true;
                Cancel_Button.IsEnabled = false;
                print_Buttun.IsEnabled = false;
                Edit_Button.Content = "OK";
            }
            else if (Edit_Button.Content.Equals("OK"))
            {
                serial_number_TextBox.IsEnabled = false;
                Cancel_Button.IsEnabled = true;
                print_Buttun.IsEnabled = true;
                Edit_Button.Content = "Edit";
            }
            if (Edit_Button.Content.Equals("Connect"))
            {
                print_Buttun.Visibility = Visibility.Visible;
                Cancel_Button.Visibility = Visibility.Visible;
                serial_number_TextBox.Visibility = Visibility.Visible;
                this.InvalidateVisual();
                this.UpdateLayout();
                EZcade_Start_Thread.Start();
                Edit_Button.Content = "Edit";
                Disable_serialNumber_inteaction();
            }
        }
        private void Cancel_Button_Click(object sender, RoutedEventArgs e)
        {
            serial_number_TextBox.Text = "start requesting in EZcade";
            //EZcade_Listen_Thread.Start();
            EZcade_Listen_Thread = new Thread(() =>
            {
                try
                {
                    string request = ezcade_Connection_Handler.Listen();
                    Request_recived(request);
                }
                catch
                {
                    // Handle exceptions if needed
                }
            });

            EZcade_Listen_Thread.Start();
            //_buttonClickedTcs.TrySetResult(true);
        }
        private void Disable_serialNumber_inteaction()
        {
            serial_number_TextBox.IsEnabled = false;
            print_Buttun.IsEnabled = false;
            Edit_Button.IsEnabled = false;
            Cancel_Button.IsEnabled = false;
        }
        private void Enable_serialNumber_inteaction()
        {
            //serial_number_TextBox.IsEnabled = true;
            print_Buttun.IsEnabled = true;
            Edit_Button.IsEnabled = true;
            Cancel_Button.IsEnabled = true;
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            try {
                //MessageBox.Show("Window is closing. Running cleanup.");
                connection_handler.Cleanup();
                if(ezcade_Connection_Handler != null)
                {
                    _cancellationTokenSource.Cancel();
                    ezcade_Connection_Handler.Cleanup();
                    
                }
            }
            catch(Exception exeption) {
                MessageBox.Show(exeption.Message);
            }
            
        }
    }
}