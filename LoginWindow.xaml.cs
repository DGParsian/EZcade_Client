using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace EZcade_Client
{
    public partial class LoginWindow : Window
    {
        private Connection_Handler _connection_Handler;
        public string Username { get; private set; }
        public string Password { get; private set; }

        public LoginWindow(Connection_Handler connection)
        {
            InitializeComponent();
            _connection_Handler = connection;
        }

        private void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            Username = UsernameTextBox.Text;
            Password = PasswordBox.Password;

            if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
            {
                MessageBox.Show("Please enter both username and password.", "Login Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            else 
            {
                JsonObject login_response = new JsonObject();
                JsonObject login_request = new JsonObject();

                login_request["requestType"] = "login";

                login_request["userRole"] = "Ezcade_operator";
                login_request["username"] = Username;
                login_request["password"] = Password;

                login_response = _connection_Handler.Request_Json(login_request);


                if (login_response == null || login_response["result"].ToString() != "true") {
                    MessageBox.Show("Login Failed.", "Login Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                DialogResult = true;
                Close();
            }

            
        }
    }
}
