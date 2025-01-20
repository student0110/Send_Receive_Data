using Microsoft.Win32;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace SendReceiveData
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>   
    public partial class MainWindow : Window
    {
        private TcpListener? _server;
        private TcpClient? _client;
        private NetworkStream? _networkStream;
        private const int Port = 12345;
        public string LocalIp { get; } = string.Empty;
        private bool _isShuttingDown = false;

        public MainWindow()
        {
            InitializeComponent();
            LocalIp = ipNameTextBlock.Text = GetIpAdress();
            StartServer();
        }

        private static string GetIpAdress()
        {
            var stringIp = string.Empty;
            var networkInterfaces = NetworkInterface.GetAllNetworkInterfaces();

            Parallel.ForEach(networkInterfaces, (networkInterface, state) =>
            {
                if (networkInterface.OperationalStatus != OperationalStatus.Up || networkInterface.GetIPProperties().GatewayAddresses.Count == 0)
                {
                    return;
                }

                var ipProperties = networkInterface.GetIPProperties();

                foreach (var ipInfo in ipProperties.UnicastAddresses)
                {
                    if (ipInfo.PrefixOrigin == PrefixOrigin.Dhcp || ipInfo.Address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        stringIp = ipInfo.Address.ToString();
                        state.Stop();
                        return;
                    }
                }
            });

            if (string.IsNullOrEmpty(stringIp))
            {
                stringIp = "127.0.0.1";
            }

            return stringIp;
        }

       
        private void StartServer()
        {
            try
            {
                _server = new TcpListener(IPAddress.Any, Port);
                _server.Start();
                _server.BeginAcceptTcpClient(ClientConnectedCallback, null);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error starting server: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ClientConnectedCallback(IAsyncResult result)
        {
            try
            {
                if (_isShuttingDown || !_server!.Server.IsBound) return;

                TcpClient client = _server.EndAcceptTcpClient(result);
                _networkStream = client.GetStream();

                Task.Run(() =>
                {
                    try
                    {
                        ReceiveFile(_networkStream);  
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Error receiving file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                });

                _server.BeginAcceptTcpClient(new AsyncCallback(ClientConnectedCallback), null);
            }
            catch (ObjectDisposedException)
            {
                // ingnore this error 
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error accepting client: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ReceiveFile(NetworkStream stream)  
        {
            try
            {
                using var binaryReader = new BinaryReader(stream);
                string fileName = binaryReader.ReadString();
                long fileSize = binaryReader.ReadInt64();

                Dispatcher.Invoke(() => { fileNameTextBlock.Text = fileName; });

                string tempFilePath = Path.Combine(Path.GetTempPath(), fileName);

                using (var fileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write))
                { 
                    byte[] buffer = new byte[8192];
                    long totalBytesRead = 0;
                    int bytesRead;

                    while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        fileStream.Write(buffer, 0, bytesRead);
                        totalBytesRead += bytesRead;

                        Dispatcher.Invoke(() => { progressBar.Value = (double)totalBytesRead / fileSize * 100; });
                    }
                }

                Dispatcher.Invoke(() =>
                {
                    MessageBoxResult result = MessageBox.Show(
                        $"File '{fileName}' received. Do you want to save it?",
                        "File Received",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question
                    );

                    if (result == MessageBoxResult.Yes)
                    {
                        SaveFileDialog saveFileDialog = new()
                        {
                            FileName = fileName,
                            Title = "Save File",
                            Filter = "All Files (*.*)|*.*"
                        };

                        if (saveFileDialog.ShowDialog() == true)
                        {
                            File.Copy(tempFilePath, saveFileDialog.FileName, true);
                            MessageBox.Show("File saved successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                            
                        }
                    }

                    fileNameTextBlock.Text = string.Empty;
                    progressBar.Value = 0;
                   
                    try
                    {
                        if (File.Exists(tempFilePath))
                        {
                            File.Delete(tempFilePath);
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Error deleting temporary file: {ex.Message}", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                });
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                    MessageBox.Show($"Error receiving file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error));
            }
        }

        private void SelectFileButton_Click(object sender, RoutedEventArgs e)  //SendFileButton_Click
        {
            string recipientIp = ipRecipient.Text;
            if (string.IsNullOrEmpty(recipientIp))
            {
                MessageBox.Show($"Error: Enter the recipient IP address.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
                

            OpenFileDialog openFileDialog = new()
            {
                Title = "Select the file to send",
                Filter = "All Files (*.*)|*.*"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                string filePath = openFileDialog.FileName;
                MessageBoxResult result = MessageBox.Show(
                           $"Do you want to send the file '{Path.GetFileName(filePath)}'?",
                           "Send File",
                           MessageBoxButton.YesNo,
                           MessageBoxImage.Question
                       );
                try
                {
                    if(result == MessageBoxResult.Yes)
                    {
                        _client = new TcpClient(recipientIp, Port);
                        _networkStream = _client.GetStream();

                        Task.Run(() =>
                        {
                            try
                            {
                                SendFile(filePath, _networkStream);
                            }
                            catch (Exception ex)
                            {
                                Dispatcher.Invoke(() =>
                                    MessageBox.Show($"Error sending file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error));
                            }
                        });
                    }                  
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void SendFile(string filePath, NetworkStream stream)
        {
            try
            {
                using var binaryWriter = new BinaryWriter(stream);
                binaryWriter.Write(Path.GetFileName(filePath));
                binaryWriter.Write(new FileInfo(filePath).Length);

                using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
                byte[] buffer = new byte[8192];
                int bytesRead;

                while ((bytesRead = fileStream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    stream.Write(buffer, 0, bytesRead);
                }

                Dispatcher.Invoke(() =>
                    MessageBox.Show("File sent successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information));
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                    MessageBox.Show($"Error sending file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error));
            }
        }

        private void TextBlock_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            Clipboard.SetText(LocalIp);
            MessageBox.Show("The text has been copied to the clipboard.", "Copy successful", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                _isShuttingDown = true;
                if (_client != null)
                {
                    _client.Close();
                    _client.Dispose();
                }

                if (_server != null)
                {
                    _server.Stop();
                    _server.Dispose();
                }

                if (_networkStream != null)
                {
                    _networkStream.Close();
                    _networkStream.Dispose();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error closing app: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Window_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            try
            {
                string recipientIp = ipRecipient.Text;
                if (string.IsNullOrEmpty(recipientIp))
                {
                    MessageBox.Show($"Error: Enter the recipient IP address.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                if (e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                    if (files.Length > 0)
                    {
                        string filePath = files[0];
                        MessageBoxResult result = MessageBox.Show(
                            $"Do you want to send the file '{Path.GetFileName(filePath)}'?",
                            "Send File",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Question
                        );

                        if (result == MessageBoxResult.Yes)
                        {
                            _client = new TcpClient(recipientIp, Port);
                            _networkStream = _client.GetStream();

                            Task.Run(() =>
                            {
                                try
                                {
                                    SendFile(filePath, _networkStream);
                                }
                                catch (Exception ex)
                                {
                                    Dispatcher.Invoke(() =>
                                        MessageBox.Show($"Error sending file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error));
                                }
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

}