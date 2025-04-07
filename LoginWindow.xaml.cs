using System.Text.Json.Nodes;
using System.Windows;


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

        #region Event Handlers
        private void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            Username = UsernameTextBox.Text;
            Password = PasswordBox.Password;

            if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
            {
                MessageBox.Show("Please enter both username and password.", "Login Error", MessageBoxButton.OK, MessageBoxImage.Warning);
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

                if (login_response == null || login_response["result"].ToString() != "true")
                {
                    MessageBox.Show("Login Failed.", "Login Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                DialogResult = true;
                Close();
            }
        }
        #endregion
    }
}
