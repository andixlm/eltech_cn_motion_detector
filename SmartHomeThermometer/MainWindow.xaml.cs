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
        private static readonly int BUFFER_SIZE = 8192;

        private static readonly char DELIMITER = ';';

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

        private static readonly string NETWORK_DEVICE_ARG = "Device: ";
        private static readonly string NETWORK_TEMPERATURE_ARG = "Temperatute: ";
        private static readonly string NETWORK_UPDATE_INTERVAL_ARG = "Update interval: ";
        private static readonly string NETWORK_METHOD_TO_INVOKE_ARG = "Method: ";

        private static readonly string NETWORK_METHOD_TO_UPDATE_TEMP = "UPDATE_TEMP";

        private bool _ShouldScrollToEnd = true;

        private Thermometer _Thermometer;

        private int _UpdateInterval;

        private TcpClient _Socket;

        private Thread _ListenerThread;

        private Mutex _ReceiveMutex;
        private Mutex _SendMutex;

        private List<string> _Cache;

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

            _ReceiveMutex = new Mutex();
            _SendMutex = new Mutex();

            _Cache = new List<string>();
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
                    Receive(ref _Socket, ref bytes);

                    ProcessData(CacheData(Encoding.Unicode.GetString(bytes), ref _Cache));
                    ProcessData(ref _Cache);
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

                    Log(UPDATE_INTERVAL_LOG_LABEL + string.Format("Set to {0}\n", _UpdateInterval));

                    if (_Socket.Connected) SendUpdateInterval(_Thermometer.UpdateInterval);
                }
                catch (Exception exc)
                {
                    Log(UPDATE_INTERVAL_LOG_LABEL + exc.Message + "\n");
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
                Log(IPADDRESS_LOG_LABEL + exc.Message);
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
                Log(PORT_LOG_LABEL + exc.Message);
                ConnectButton.IsEnabled = !ConnectButton.IsEnabled;
                return;
            }

            Thread connectThread = new Thread(new ThreadStart(delegate ()
            {
                Log((CONNECTION_LOG_LABEL +
                    string.Format("Connecting to {0}:{1}\n", _IPAddress.ToString(), _Port)));
                Dispatcher.Invoke(delegate ()
                {
                    ConnectionStateLabel.Content = CONNECTION_WAIT;
                });

                try
                {
                    _Socket.Connect(_IPAddress, _Port);

                    Log(CONNECTION_LOG_LABEL +
                        string.Format("Connected to {0}:{1}\n", _IPAddress.ToString(), _Port));
                    Dispatcher.Invoke(delegate ()
                    {
                        ConnectionStateLabel.Content = CONNECTION_UP;
                    });

                    SendInfo();
                    SendUpdateInterval(_UpdateInterval);

                    _ListenerThread.Start();
                }
                catch (SocketException exc)
                {
                    Log(CONNECTION_LOG_LABEL + exc.Message + "\n");
                    Dispatcher.Invoke(delegate ()
                    {
                        ConnectionStateLabel.Content = CONNECTION_ERR;
                        ConnectButton.IsEnabled = !ConnectButton.IsEnabled;
                    });
                }
                catch (ObjectDisposedException exc)
                {
                    Log(CONNECTION_LOG_LABEL + exc.Message + "\n");
                    Dispatcher.Invoke(delegate ()
                    {
                        ConnectionStateLabel.Content = CONNECTION_DOWN;
                        ConnectButton.IsEnabled = !ConnectButton.IsEnabled;
                    });
                }
            }));
            connectThread.Start();
        }

        private void Send(byte[] bytes)
        {
            _SendMutex.WaitOne();

            try
            {
                NetworkStream stream = _Socket.GetStream();
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush();
            }
            catch (System.IO.IOException exc)
            {
                Log(NETWORK_LOG_LABEL +
                    (exc.InnerException != null ? exc.InnerException.Message : exc.Message) + "\n");
            }

            _SendMutex.ReleaseMutex();
        }

        private void Receive(ref TcpClient socket, ref byte[] bytes)
        {
            _ReceiveMutex.WaitOne();

            try
            {
                NetworkStream stream = socket.GetStream();
                stream.Read(bytes, 0, socket.ReceiveBufferSize);
            }
            catch (System.IO.IOException exc)
            {
                Log(NETWORK_LOG_LABEL +
                    (exc.InnerException != null ? exc.InnerException.Message : exc.Message) + "\n");
            }

            _ReceiveMutex.ReleaseMutex();
        }

        private void SendInfo()
        {
            byte[] bytes = Encoding.Unicode.GetBytes(NETWORK_DEVICE_ARG + "Thermometer" + DELIMITER);

            Send(bytes);

            Log(NETWORK_LOG_LABEL + "Sent info" + "\n");
        }

        private void SendUpdateInterval(double updateInterval)
        {
            byte[] bytes = Encoding.Unicode.GetBytes(string.Format(NETWORK_UPDATE_INTERVAL_ARG + "{0}" + DELIMITER, updateInterval));

            Send(bytes);

            Log(NETWORK_LOG_LABEL + "Sent update interval" + "\n");
        }

        private void SendTemperature(double temperature)
        {
            byte[] bytes = Encoding.Unicode.GetBytes(string.Format(NETWORK_TEMPERATURE_ARG + "{0}" + DELIMITER, temperature));

            Send(bytes);

            Log(NETWORK_LOG_LABEL +
                string.Format("Sent temperature: {0}", temperature.ToString("F2")) + "\n");
        }

        string CacheData(string data, ref List<string> cache)
        {
            int delimiterIdx = data.IndexOf(DELIMITER);
            string first = data.Substring(0, delimiterIdx + 1);

            data = data.Substring(delimiterIdx + 1, data.Length - delimiterIdx - 1);
            for (delimiterIdx = data.IndexOf(DELIMITER); delimiterIdx >= 0; delimiterIdx = data.IndexOf(DELIMITER))
            {
                cache.Add(data.Substring(0, delimiterIdx + 1));
                data = data.Substring(delimiterIdx + 1, data.Length - delimiterIdx - 1);
            }

            return first;
        }

        private void ProcessData(string data)
        {
            int idx;
            if ((idx = data.IndexOf(NETWORK_UPDATE_INTERVAL_ARG)) >= 0)
            {
                try
                {
                    int startIdx = idx + NETWORK_UPDATE_INTERVAL_ARG.Length, endIdx = data.IndexOf(DELIMITER);
                    int updateInterval = int.Parse(data.Substring(startIdx, endIdx - startIdx));

                    Log(NETWORK_LOG_LABEL + string.Format("Received update interval: {0}", updateInterval) + "\n");

                    try
                    {
                        _Thermometer.UpdateInterval = updateInterval;

                        Dispatcher.Invoke(delegate ()
                        {
                            UpdateIntervalTextBlock.Text = updateInterval.ToString();
                        });
                    }
                    catch (Exception exc)
                    {
                        Log(UPDATE_INTERVAL_LOG_LABEL + exc.Message + "\n");

                        SendUpdateInterval(_Thermometer.UpdateInterval);
                    }
                }
                catch (FormatException)
                {
                    Log(NETWORK_LOG_LABEL + "Received incorrect update interval" + "\n");
                }
            }
            else if ((idx = data.IndexOf(NETWORK_METHOD_TO_INVOKE_ARG)) >= 0)
            {
                int startIdx = idx + NETWORK_METHOD_TO_INVOKE_ARG.Length, endIdx = data.IndexOf(DELIMITER);
                string method = data.Substring(startIdx, endIdx - startIdx);

                if (!string.IsNullOrEmpty(method) && method.Equals(NETWORK_METHOD_TO_UPDATE_TEMP))
                {
                    _Thermometer.UpdateTemperature();

                    Log(NETWORK_LOG_LABEL + "Temperature update was requested." + "\n");
                }
            }
            else
            {
                Log(string.Format(NETWORK_LOG_LABEL + "Received unknown data: \"{0}\"" + "\n", data));
            }
        }

        private void ProcessData(ref List<string> dataSet)
        {
            foreach (string data in dataSet)
            {
                ProcessData(data);
            }

            dataSet.Clear();
        }

        private void Log(string info)
        {
            Dispatcher.Invoke(delegate ()
            {
                LogTextBlock.AppendText(info);
                if (_ShouldScrollToEnd) LogTextBlock.ScrollToEnd();
            });
        }
    }
}
