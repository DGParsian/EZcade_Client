using System.Windows;
using System.Text.Json.Nodes;
using EZcade_Client.Properties;
using System.Windows.Controls;
using Main_Server.DTOs.Ezcade;
using System.Text.Json;
using EZcade_Client.EzcadeDtos;

namespace EZcade_Client
{
    public partial class MainWindow : Window
    {
        #region Fields
        public HttpConnectionHandler connection_handler { get; internal set; }
        public EZcade_Connection_Handler ezcade_Connection_Handler { get; internal set; }

        Thread EZcade_Listen_Thread;
        Thread EZcade_Start_Thread;
        CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();


        private bool IsAutomated = false;
        private bool IsDataMatrixActive = false;
        public int QueryCount { get; set; }
        public string QueryType { get; set; }

        public List<string> FirstOptions { get; set; } = new() { "Select Batch"};
        //public List<string> SecondOptions { get; set; } = new() { "Select Model"};

        public ListDto<BatchDto> BatchList { get; set; }

        #endregion

        #region Constructor
        public MainWindow()
        {
            InitializeComponent();
            connection_handler = new HttpConnectionHandler(); // Set correct base URL

            if (!AutoLogin(connection_handler))
            {
                LoginWindow loginWindow = new LoginWindow(connection_handler);
                loginWindow.ShowDialog();
            }

            Loaded += MainWindow_Loaded;

            DataContext = this;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if ( connection_handler.StatusCheckAsync())
            {
                Show_ip();
                Init_Form();
            }
            else
            {
                MessageBox.Show("Not connected to server", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var batchList =  connection_handler.GetComponentBatchListAsync();

            foreach (var batch in batchList.Items)
            {
                FirstOptions.Add(batch);
            }

            firstComboBox.SelectedIndex = 0;
            //secondComboBox.SelectedIndex = 0;
        }
        #endregion

        #region Initialization

        //private bool AutoLogin(Connection_Handler connection_Handler)

        //{

        //    //Settings.Default.SavedPassword = "";
        //    //Settings.Default.Save();
        //    var username = Settings.Default.SavedUsername;
        //    var password = Settings.Default.SavedPassword;

        //    if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        //    {
        //        return false;
        //    }

        //    JsonObject login_response = new JsonObject();
        //    JsonObject login_request = new JsonObject();

        //    login_request["requestType"] = "login";

        //    login_request["userRole"] = "Ezcade_operator";
        //    login_request["username"] = username;
        //    login_request["password"] = password;

        //    login_response = connection_Handler.Request_Json(login_request);

        //    if (login_response == null || login_response["result"].ToString() != "true")
        //    {
        //        MessageBox.Show("Login Failed.", "Login Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        //        return false ;
        //    }
        //     return true;

        //}
        private bool AutoLogin(HttpConnectionHandler connection_Handler)
        {
            var username = Settings.Default.SavedUsername;
            var password = Settings.Default.SavedPassword;

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                return false;
            }

            try
            {
                bool success = connection_Handler.LoginAsync(username, password);

                if (!success)
                {
                    MessageBox.Show("Login Failed.", "Login Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Login Error: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }



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


        public SerialNumberDto SerialNumberObj;
        #region Request Handling
        private void Request_recived(string recived_string  )
        {
            recived_string = recived_string.ToUpper();

            //string model = firstComboBox.Dispatcher.Invoke(() => secondComboBox.SelectedItem?.ToString().ToUpper());
            string batch = firstComboBox.Dispatcher.Invoke(() => firstComboBox.SelectedItem?.ToString());

            if (recived_string != "")
            {
                if (recived_string != "ERROR")
                {
                    SerialNumberQuery request = new(recived_string);
                    SerialNumberObj = connection_handler.GetSerialNumber(request, batch);

                    


                    


                    HandleQueryResponse(request, SerialNumberObj.SerialNumber , SerialNumberObj.DmCode, 1);
                }
            }
        }
        private void HandleQueryResponse(SerialNumberQuery query,string serialNumber, string dmCode, int index)
        {
            var serialNumber_to_print = query.GetSerialNumber(serialNumber , dmCode);
            QueryType = query.Type;

            if (IsAutomated)
            {
                if (SerialNumberObj.SerialNumber != "ERROR")
                    ezcade_Connection_Handler.SendAndPrint(serialNumber_to_print, showMessage: true);
                QueryCount = query.Count - 1;
                HandleQueries(serialNumber , dmCode);
            }
            else
            {
                Dispatcher.Invoke(() =>
                {
                    serial_number_TextBox.Text = serialNumber_to_print;
                    Enable_serialNumber_inteaction();
                });
                QueryCount = query.Count - 1;
            }
        }

        private void HandleQueries(string serialNumber , string dmCode)
        {

            
            for (int i = 0; i < QueryCount; i++)
            {
                var newRecived_string = ezcade_Connection_Handler.Listen();
                SerialNumberQuery query = new(newRecived_string);
                var serialNumber_to_print = query.GetSerialNumber(serialNumber , dmCode);
                if(SerialNumberObj.SerialNumber != "ERROR")
                {
                    ezcade_Connection_Handler.SendAndPrint(serialNumber_to_print, showMessage: true);
                }
            }
            if (QueryType == "PR")
            {
                var response = connection_handler.Activation(serialNumber, "product");
            }
            else if(QueryType == "PB")
            {
                var response = connection_handler.Activation(serialNumber, "pcb");
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

        private void firstComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selectedBatch = firstComboBox.SelectedItem as string;
            //SecondOptions.Clear();
            //SecondOptions.Add("Select Model");
            //foreach (var batch in BatchList.Items)
            //{
            //    if (selectedBatch == batch.BatchName)
            //    {
                    
            //        foreach (var part in batch.BatchParts)
            //        {
            //            SecondOptions.Add(part.Model);
            //        }
            //    }                
            //}
            //secondComboBox.ItemsSource = null;
            //secondComboBox.SelectedIndex = 0;
            //secondComboBox.ItemsSource = SecondOptions;
        }


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
            ezcade_Connection_Handler.SendAndPrint(serial_number_TextBox.Text,showMessage:true);

            
            HandleQueries(RemovePrefix(serial_number_TextBox.Text) , SerialNumberObj.DmCode);

            serial_number_TextBox.Text = "start requesting in EZcade";

        }
        public string RemovePrefix(string input)
        {
            if (input.StartsWith("PR"))
                return input.Substring(2);
            if (input.StartsWith("PB"))
                return input.Substring(2);
            return input;
        }
        private void activate_product(string serialNumber)
        {
            var Request = new JsonObject();
            Request["requestType"] = "activation";

            Request["serialNumber"] = serialNumber;

            


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