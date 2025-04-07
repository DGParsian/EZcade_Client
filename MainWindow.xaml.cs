using System.Windows;
using System.Text.Json.Nodes;

namespace EZcade_Client
{
    public partial class MainWindow : Window
    {
        #region Fields
        public Connection_Handler connection_handler { get; internal set; }
        public EZcade_Connection_Handler ezcade_Connection_Handler { get; internal set; }

        Thread EZcade_Listen_Thread;
        Thread EZcade_Start_Thread;
        CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();


        private bool IsAutomated = false;
        private bool IsDataMatrixActive = false;

        #endregion

        #region Constructor
        public MainWindow()
        {
            InitializeComponent();
            connection_handler = new Connection_Handler();

            LoginWindow loginWindow = new LoginWindow(connection_handler);
            loginWindow.ShowDialog();

            if (connection_handler.Status_Check())
            {
                Show_ip();
            }
            else
            {
                MessageBox.Show("not connected");
            }
            Init_Form();
        }
        #endregion

        #region Initialization
        private void Init_Form()
        {
            InitializeThreads();
            InitializeUI();
        }

        private void InitializeThreads()
        {
            EZcade_Listen_Thread = new Thread(() =>
            {
                try
                {
               
                    string request = ezcade_Connection_Handler.Listen();
                    Request_recived(request);
                
                }
                catch { }
            });

            EZcade_Start_Thread = new Thread(() =>
            {
                try
                {
                    if (!_cancellationTokenSource.Token.IsCancellationRequested)
                    {
                        ezcade_Connection_Handler = new EZcade_Connection_Handler();
                        ezcade_Connection_Handler.init();
                        EZcade_Listen_Thread.Start();
                    }
                }
                catch { }
            });
        }

        private void InitializeUI()
        {
            Edit_Button.Content = "Connect";
            serial_number_TextBox.Text = "press connect and then start requesting in EZcade";
            print_Buttun.Visibility = Visibility.Hidden;
            Cancel_Button.Visibility = Visibility.Hidden;
        }
        #endregion

        #region Request Handling
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

                    var Response = connection_handler.Request_Json(Request);



                    
                    if (IsAutomated)
                    {
                        
                        print_Automated(Response["serialNumber"].ToString());
                    }
                    else
                    {
                        Dispatcher.Invoke(() =>
                        {
                            serial_number_TextBox.Text = Response["serialNumber"].ToString();
                            Enable_serialNumber_inteaction();
                        });
                    }

                    

                }
            }
        }

        private void print_Automated(string serialNumber)
        {
            activate_product(serialNumber);
            ezcade_Connection_Handler.SendAndPrint(serialNumber);

            if (IsDataMatrixActive)
            {
                ezcade_Connection_Handler.Listen();
                ezcade_Connection_Handler.SendAndPrint(serialNumber);
            }


            
            EZcade_Listen_Thread = new Thread(() =>
            {
                try
                {
                    string request = ezcade_Connection_Handler.Listen();
                    Request_recived(request);
                }
                catch { }
            });

            EZcade_Listen_Thread.Start();
        }
        #endregion

        #region UI Updates
        private void Show_ip()
        {
            string ip = connection_handler.serverIp;
            string port = connection_handler.serverPort.ToString();
            ip_Lable.Content = "connected through " + ip + ":" + port;
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
            print_Buttun.IsEnabled = true;
            Edit_Button.IsEnabled = true;
            Cancel_Button.IsEnabled = true;
        }
        #endregion

        #region Event Handlers
        private void print_Buttun_Click(object sender, RoutedEventArgs e)
        {
            Disable_serialNumber_inteaction();
            activate_product(serial_number_TextBox.Text);
            ezcade_Connection_Handler.SendAndPrint(serial_number_TextBox.Text,showMessage:true);

            if (IsDataMatrixActive)
            {
                ezcade_Connection_Handler.Listen();
                ezcade_Connection_Handler.SendAndPrint(serial_number_TextBox.Text);
            }

            
            serial_number_TextBox.Text = "start requesting in EZcade";
            EZcade_Listen_Thread = new Thread(() =>
            {
                try
                {
                    string request = ezcade_Connection_Handler.Listen();
                    Request_recived(request);
                }
                catch { }
            });

            EZcade_Listen_Thread.Start();
        }

        private void activate_product(string serialNumber)
        {
            var Request = new JsonObject();
            Request["requestType"] = "activation";

            Request["serialNumber"] = serialNumber;
            var Response = connection_handler.Request_Json(Request);
            
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
            Disable_serialNumber_inteaction();
            serial_number_TextBox.Text = "start requesting in EZcade";
            EZcade_Listen_Thread = new Thread(() =>
            {
                try
                {
                    string request = ezcade_Connection_Handler.Listen();
                    Request_recived(request);
                }
                catch { }
            });

            EZcade_Listen_Thread.Start();

        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                connection_handler.Cleanup();
                if (ezcade_Connection_Handler != null)
                {
                    _cancellationTokenSource.Cancel();
                    ezcade_Connection_Handler.Cleanup();
                }
            }
            catch (Exception exeption)
            {
                MessageBox.Show(exeption.Message);
            }
        }

        private void Automation_CheckBox_Checked(object sender, RoutedEventArgs e)
        {
            IsAutomated = true;
        }
        private void Automation_CheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            IsAutomated = false;
        }
        private void DataMatrix_CheckBox_Checked(object sender, RoutedEventArgs e)
        {
            IsDataMatrixActive = true;
        }
        private void DataMatrix_CheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            IsDataMatrixActive = false;
        }

        #endregion
    }
}