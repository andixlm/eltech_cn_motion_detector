using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace SmartHomeThermometer
{
    public partial class MainWindow : Window
    {
        private static readonly int BUFFER_SIZE = 1024;

        private static readonly string IPADDRESS_LOG_LABEL = "IP Address: ";

        private static readonly string PORT_LOG_LABEL = "Port: ";
        private static readonly int MINIMAL_PORT_VALUE = 1024;
        private static readonly int MAXIMAL_PORT_VALUE = 49151;

        private static readonly string CONNECTION_LOG_LABEL = "Connection: ";
        private static readonly string CONNECTION_UP = "up";
        private static readonly string CONNECTION_WAIT = "wait";
        private static readonly string CONNECTION_DOWN = "down";
        private static readonly string CONNECTION_ERR = "err";

        private static readonly string UPDATE_INTERVAL_LOG_LABEL = "Update interval: ";

        private static readonly string NETWORK_LOG_LABEL = "Network: ";

        private static readonly string NETWORK_UPDATE_INTERVAL_ARG = "Update interval: ";

        private Thermometer _Thermometer;

        private int _UpdateInterval;

        private TcpClient _Socket;

        private Thread _ListenerThread;

        private IPAddress _IPAddress;
        private int _Port;

        public MainWindow()
        {
            InitializeComponent();

            Init();
            Configure();
        }

        private void Init()
        {
            _Thermometer = new Thermometer();

            _UpdateInterval = Thermometer.DEFAULT_UPDATE_INTERVAL;
            UpdateIntervalTextBlock.Text = _UpdateInterval.ToString();

            _Socket = new TcpClient();
        }

        private void Configure()
        {
            /// App
            Closed += (sender, e) =>
            {
                _Thermometer.Dispose();
            };

            _ListenerThread = new Thread(new ThreadStart(delegate ()
            {
                while (_Socket.Connected)
                {
                    byte[] bytes = new byte[BUFFER_SIZE];

                    NetworkStream socketStream = _Socket.GetStream();
                    socketStream.Read(bytes, 0, _Socket.ReceiveBufferSize);

                    string data = Encoding.Unicode.GetString(bytes);
                    data = data.Substring(0, data.IndexOf("$"));

                    if (string.IsNullOrEmpty(data))
                    {
                        continue;
                    }

                    ProcessData(data);
                }
            }));

            /// Controls
            ConnectButton.Click += (sender, e) =>
            {
                Connect();
            };

            UpdateIntervalSetButton.Click += (sender, e) =>
            {
                try
                {
                    _UpdateInterval = int.Parse(UpdateIntervalTextBlock.Text);
                    _Thermometer.UpdateInterval = _UpdateInterval;

                    LogTextBlock.AppendText(UPDATE_INTERVAL_LOG_LABEL +
                        string.Format("Set to {0}\n", _UpdateInterval));
                    LogTextBlock.ScrollToEnd();
                }
                catch (Exception exc)
                {
                    LogTextBlock.AppendText(UPDATE_INTERVAL_LOG_LABEL + exc.Message + "\n");
                    LogTextBlock.ScrollToEnd();
                }
            };

            TemperatureUpdateButton.Click += (sender, e) =>
            {
                _Thermometer.UpdateTemperature();
            };

            /// Objects
            _Thermometer.OnTemperatureUpdate = (temperature) =>
            {
                Dispatcher.Invoke(delegate ()
                {
                    TemperatureValueLabel.Content = temperature.ToString("F2");
                });

                if (_Socket.Connected)
                {
                    SendTemperature(temperature);
                }
            };
        }

        private void Connect()
        {
            ConnectButton.IsEnabled = !ConnectButton.IsEnabled;

            try
            {
                _IPAddress = IPAddress.Parse(AddressTextBox.Text);
            }
            catch (Exception exc)
            {
                LogTextBlock.AppendText(IPADDRESS_LOG_LABEL + exc.Message);
                LogTextBlock.ScrollToEnd();
                ConnectButton.IsEnabled = !ConnectButton.IsEnabled;
                return;
            }

            try
            {
                _Port = int.Parse(PortTextBox.Text);

                if (_Port < MINIMAL_PORT_VALUE || _Port > MAXIMAL_PORT_VALUE)
                {
                    throw new Exception(string.Format("Incorrect port value. [{0}; {1}] ports are allowed.",
                        MINIMAL_PORT_VALUE, MAXIMAL_PORT_VALUE));
                }
            }
            catch (Exception exc)
            {
                LogTextBlock.AppendText(PORT_LOG_LABEL + exc.Message);
                LogTextBlock.ScrollToEnd();
                ConnectButton.IsEnabled = !ConnectButton.IsEnabled;
                return;
            }

            Thread connectThread = new Thread(new ThreadStart(delegate ()
            {
                Dispatcher.Invoke(delegate ()
                {
                    LogTextBlock.AppendText(CONNECTION_LOG_LABEL +
                        string.Format("Connecting to {0}:{1}\n", _IPAddress.ToString(), _Port));
                    LogTextBlock.ScrollToEnd();
                    ConnectionStateLabel.Content = CONNECTION_WAIT;
                });

                try
                {
                    _Socket.Connect(_IPAddress, _Port);
                    SendInfo();

                    _ListenerThread.Start();

                    Dispatcher.Invoke(delegate ()
                    {
                        LogTextBlock.AppendText(CONNECTION_LOG_LABEL +
                            string.Format("Connected to {0}:{1}\n", _IPAddress.ToString(), _Port));
                        LogTextBlock.ScrollToEnd();
                        ConnectionStateLabel.Content = CONNECTION_UP;
                    });
                }
                catch (SocketException exc)
                {
                    Dispatcher.Invoke(delegate ()
                    {
                        LogTextBlock.AppendText(CONNECTION_LOG_LABEL + exc.Message + "\n");
                        LogTextBlock.ScrollToEnd();
                        ConnectionStateLabel.Content = CONNECTION_ERR;
                        ConnectButton.IsEnabled = !ConnectButton.IsEnabled;
                    });
                }
                catch (ObjectDisposedException exc)
                {
                    Dispatcher.Invoke(delegate ()
                    {
                        LogTextBlock.AppendText(CONNECTION_LOG_LABEL + exc.Message + "\n");
                        LogTextBlock.ScrollToEnd();
                        ConnectionStateLabel.Content = CONNECTION_DOWN;
                        ConnectButton.IsEnabled = !ConnectButton.IsEnabled;
                    });
                }
            }));
            connectThread.Start();
        }

        private void Send(byte[] bytes)
        {
            NetworkStream stream = _Socket.GetStream();
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush();
        }

        private void SendInfo()
        {
            byte[] bytes = Encoding.Unicode.GetBytes("Device: Thermometer");

            Send(bytes);
        }

        private void SendTemperature(double temperature)
        {
            byte[] bytes = Encoding.Unicode.GetBytes(string.Format("Temparatute: {0}", temperature));

            Send(bytes);
        }

        private void ProcessData(string data)
        {
            int idx;
            if ((idx = data.IndexOf(NETWORK_UPDATE_INTERVAL_ARG)) > 0)
            {
                try
                {
                    int updateInterval = int.Parse(data.Substring(idx, data.IndexOf("$")));
                    _Thermometer.UpdateInterval = updateInterval;
                }
                catch (FormatException)
                {
                    LogTextBlock.AppendText(NETWORK_LOG_LABEL + "Received incorrect update interval.");
                    LogTextBlock.ScrollToEnd();
                }
            }
            else
            {
                LogTextBlock.AppendText(string.Format(NETWORK_LOG_LABEL + "Received unknown data: \"{0}\"\n", data));
                LogTextBlock.ScrollToEnd();
            }
        }
    }
}
