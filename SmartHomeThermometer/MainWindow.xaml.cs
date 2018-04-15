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

        private Thermometer _Thermometer;

        private int _UpdateInterval;

        private TcpClient _Socket;
        private NetworkStream _SocketStream;

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
            _SocketStream = default(NetworkStream);
        }

        private void Configure()
        {
            /// App
            Closed += (sender, e) =>
            {
                _Thermometer.Dispose();
            };

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
                }
                catch (Exception exc)
                {
                    LogTextBlock.AppendText(UPDATE_INTERVAL_LOG_LABEL + exc.Message + "\n");
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
                ConnectButton.IsEnabled = !ConnectButton.IsEnabled;
                return;
            }

            Thread connectThread = new Thread(new ThreadStart(delegate ()
            {
                Dispatcher.Invoke(delegate ()
                {
                    LogTextBlock.AppendText(CONNECTION_LOG_LABEL +
                        string.Format("Connecting to {0}:{1}\n", _IPAddress.ToString(), _Port));
                    ConnectionStateLabel.Content = CONNECTION_WAIT;
                });

                try
                {
                    _Socket.Connect(_IPAddress, _Port);
                    SendInfo();

                    Dispatcher.Invoke(delegate ()
                    {
                        LogTextBlock.AppendText(CONNECTION_LOG_LABEL +
                            string.Format("Connected to {0}:{1}\n", _IPAddress.ToString(), _Port));
                        ConnectionStateLabel.Content = CONNECTION_UP;
                    });
                }
                catch (SocketException exc)
                {
                    Dispatcher.Invoke(delegate ()
                    {
                        LogTextBlock.AppendText(CONNECTION_LOG_LABEL + exc.Message + "\n");
                        ConnectionStateLabel.Content = CONNECTION_ERR;
                        ConnectButton.IsEnabled = !ConnectButton.IsEnabled;
                    });
                }
                catch (ObjectDisposedException exc)
                {
                    Dispatcher.Invoke(delegate ()
                    {
                        LogTextBlock.AppendText(CONNECTION_LOG_LABEL + exc.Message + "\n");
                        ConnectionStateLabel.Content = CONNECTION_DOWN;
                        ConnectButton.IsEnabled = !ConnectButton.IsEnabled;
                    });
                }
            }));
            connectThread.Start();
        }

        private void Send(byte[] data)
        {
            _SocketStream = _Socket.GetStream();
            _SocketStream.Write(data, 0, data.Length);
            _SocketStream.Flush();
        }

        private void SendInfo()
        {
            byte[] data = Encoding.Unicode.GetBytes("Device: Thermometer");

            Send(data);
        }

        private void SendTemperature(double temperature)
        {
            byte[] data = Encoding.Unicode.GetBytes(string.Format("Temparatute: {0}", temperature));

            Send(data);
        }
    }
}
