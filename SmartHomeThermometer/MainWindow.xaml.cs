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

        private static readonly string THERMOMETER_LOG_LABEL = "Thermometer: ";

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
        private static readonly string NETWORK_METHOD_TO_DISCONNECT = "DISCONNECT";

        private bool _VerboseLogging = false;
        private bool _ShouldScrollToEnd = true;

        private Thermometer _Thermometer;

        private int _UpdateInterval;

        private TcpClient _Socket;

        private Thread _ListenerThread;

        private Mutex _ReceiveMutex;
        private Mutex _SendMutex;

        private Mutex _DataMutex;

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

            _ReceiveMutex = new Mutex();
            _SendMutex = new Mutex();

            _DataMutex = new Mutex();

            _Cache = new List<string>();
        }

        private void Configure()
        {
            /// App
            Closed += (sender, e) =>
            {
                _Thermometer.Dispose();
                Disconnect();
                _Socket = null;
            };

            /// Controls
            ConnectButton.IsEnabled = true;
            ConnectButton.Click += (sender, e) =>
            {
                Connect();
            };

            DisconnectButton.IsEnabled = false;
            DisconnectButton.Click += (sender, e) =>
            {
                Disconnect();

                /// Bad idea due to bad design.
                _Socket = new TcpClient();
            };

            UpdateIntervalSetButton.Click += (sender, e) =>
            {
                try
                {
                    _UpdateInterval = int.Parse(UpdateIntervalTextBlock.Text);
                    _Thermometer.UpdateInterval = _UpdateInterval;

                    Log(UPDATE_INTERVAL_LOG_LABEL + string.Format("Set to {0}" + '\n', _UpdateInterval));

                    if (_Socket != null && _Socket.Connected)
                    {
                        SendUpdateInterval(_Thermometer.UpdateInterval);
                    }
                }
                catch (Exception exc)
                {
                    if (_VerboseLogging)
                    {
                        Log(UPDATE_INTERVAL_LOG_LABEL + exc.Message + '\n');
                    }
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

                if (_Socket != null && _Socket.Connected)
                {
                    SendTemperature(temperature);
                }
            };
        }

        private Thread ConfigureListenerThread()
        {
            return new Thread(new ThreadStart(delegate ()
            {
                try
                {
                    while (_Socket != null)
                    {
                        if (_Socket.Connected)
                        {
                            byte[] bytes = new byte[BUFFER_SIZE];
                            Receive(ref _Socket, ref bytes);

                            ProcessData(CacheData(Encoding.Unicode.GetString(bytes), ref _Cache));
                            ProcessData(ref _Cache);
                        }
                    }
                }
                catch (ThreadAbortException)
                {
                    try
                    {
                        _ReceiveMutex.ReleaseMutex();

                        Log(NETWORK_LOG_LABEL + "Disconnected." + '\n');
                        if (_VerboseLogging)
                        {
                            Log(NETWORK_LOG_LABEL + "Listener thread was terminated" + '\n');
                        }
                    }
                    catch (ApplicationException)
                    {
                        if (_VerboseLogging)
                        {
                            Log(THERMOMETER_LOG_LABEL + "Mutex's been tried to be released not by the owner thread." + '\n');
                        }
                    }
                }
            }));
        }

        private Thread ConfigureConnectThread()
        {
            return new Thread(new ThreadStart(delegate ()
            {
                Dispatcher.Invoke(delegate ()
                {
                    ConnectionStateLabel.Content = CONNECTION_WAIT;
                    SwitchButtonsOnConnectionStatusChanged(true);
                });
                Log((CONNECTION_LOG_LABEL +
                    string.Format("Connecting to {0}:{1}\n", _IPAddress.ToString(), _Port)));

                try
                {
                    _Socket = new TcpClient();
                    _Socket.Connect(_IPAddress, _Port);

                    Dispatcher.Invoke(delegate ()
                    {
                        ConnectionStateLabel.Content = CONNECTION_UP;
                    });
                    Log(CONNECTION_LOG_LABEL +
                        string.Format("Connected to {0}:{1}\n", _IPAddress.ToString(), _Port));

                    SendInfo();
                    SendUpdateInterval(_UpdateInterval);

                    _ListenerThread = ConfigureListenerThread();
                    _ListenerThread.Start();
                }
                catch (SocketException exc)
                {
                    Dispatcher.Invoke(delegate ()
                    {
                        ConnectionStateLabel.Content = CONNECTION_ERR;
                        SwitchButtonsOnConnectionStatusChanged(false);
                    });
                    if (_VerboseLogging)
                    {
                        Log(CONNECTION_LOG_LABEL + exc.Message + '\n');
                    }
                }
                catch (ObjectDisposedException exc)
                {
                    Dispatcher.Invoke(delegate ()
                    {
                        ConnectionStateLabel.Content = CONNECTION_DOWN;
                        SwitchButtonsOnConnectionStatusChanged(false);
                    });
                    if (_VerboseLogging)
                    {
                        Log(CONNECTION_LOG_LABEL + exc.Message + '\n');
                    }
                }
            }));
        }

        private void Connect()
        {
            try
            {
                _IPAddress = IPAddress.Parse(AddressTextBox.Text);
            }
            catch (Exception exc)
            {
                Log(IPADDRESS_LOG_LABEL + exc.Message + '\n');
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
                Log(PORT_LOG_LABEL + exc.Message + '\n');
                return;
            }

            Thread connectThread = ConfigureConnectThread();
            connectThread.Start();
        }

        private void Disconnect()
        {
            SendMethodToInvoke(NETWORK_METHOD_TO_DISCONNECT);

            if (_ListenerThread.IsAlive)
            {
                _ListenerThread.Abort();
            }

            if (_Socket != null)
            {
                if (_Socket.Connected)
                {
                    _Socket.Close();
                }
                else
                {
                    _Socket.Dispose();
                }
            }

            SwitchButtonsOnConnectionStatusChanged(false);
            if (_VerboseLogging)
            {
                Log(CONNECTION_LOG_LABEL + "Connection was manually closed" + '\n');
            }
        }

        private void SwitchButtonsOnConnectionStatusChanged(bool isConnected)
        {
            Dispatcher.Invoke(delegate ()
            {
                ConnectButton.IsEnabled = !isConnected;
                DisconnectButton.IsEnabled = isConnected;
            });
        }

        private void Send(byte[] bytes)
        {
            if (_Socket == null)
            {
                return;
            }

            _SendMutex.WaitOne();

            try
            {
                NetworkStream stream = _Socket.GetStream();
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush();
            }
            catch (System.IO.IOException exc)
            {
                SwitchButtonsOnConnectionStatusChanged(false);
                if (_VerboseLogging)
                {
                    Log(NETWORK_LOG_LABEL +
                        (exc.InnerException != null ? exc.InnerException.Message : exc.Message) + '\n');
                }
            }

            _SendMutex.ReleaseMutex();
        }

        private void Receive(ref TcpClient socket, ref byte[] bytes)
        {
            if (_Socket == null)
            {
                return;
            }

            _ReceiveMutex.WaitOne();

            try
            {
                NetworkStream stream = socket.GetStream();
                stream.Read(bytes, 0, socket.ReceiveBufferSize);
            }
            catch (System.IO.IOException exc)
            {
                SwitchButtonsOnConnectionStatusChanged(false);
                if (_VerboseLogging)
                {
                    Log(NETWORK_LOG_LABEL +
                        (exc.InnerException != null ? exc.InnerException.Message : exc.Message) + '\n');
                }
            }

            _ReceiveMutex.ReleaseMutex();
        }

        private void SendInfo()
        {
            byte[] bytes = Encoding.Unicode.GetBytes(NETWORK_DEVICE_ARG + "Thermometer" + DELIMITER);
            Send(bytes);

            Log(NETWORK_LOG_LABEL + "Sent info" + '\n');
        }

        private void SendUpdateInterval(double updateInterval)
        {
            byte[] bytes = Encoding.Unicode.GetBytes(string.Format(NETWORK_UPDATE_INTERVAL_ARG + "{0}" + DELIMITER, updateInterval));
            Send(bytes);

            Log(NETWORK_LOG_LABEL + "Sent update interval" + '\n');
        }

        private void SendTemperature(double temperature)
        {
            byte[] bytes = Encoding.Unicode.GetBytes(string.Format(NETWORK_TEMPERATURE_ARG + "{0}" + DELIMITER, temperature));
            Send(bytes);

            Log(NETWORK_LOG_LABEL +
                string.Format("Sent temperature: {0}", temperature.ToString("F2")) + '\n');
        }

        private void SendMethodToInvoke(string method)
        {
            byte[] bytes = Encoding.Unicode.GetBytes(NETWORK_METHOD_TO_INVOKE_ARG + method + DELIMITER);
            Send(bytes);

            Log(NETWORK_LOG_LABEL + "Sent method to close connection" + '\n');
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
            if (string.IsNullOrEmpty(data) || data.Equals(""))
            {
                return;
            }

            int idx;
            if ((idx = data.IndexOf(NETWORK_UPDATE_INTERVAL_ARG)) >= 0)
            {
                try
                {
                    int startIdx = idx + NETWORK_UPDATE_INTERVAL_ARG.Length, endIdx = data.IndexOf(DELIMITER);
                    int updateInterval = int.Parse(data.Substring(startIdx, endIdx - startIdx));

                    Log(NETWORK_LOG_LABEL + string.Format("Received update interval: {0}", updateInterval) + '\n');

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
                        SendUpdateInterval(_Thermometer.UpdateInterval);

                        Log(UPDATE_INTERVAL_LOG_LABEL + exc.Message + '\n');
                    }
                }
                catch (FormatException)
                {
                    Log(NETWORK_LOG_LABEL + "Received incorrect update interval" + '\n');
                }
            }
            else if ((idx = data.IndexOf(NETWORK_METHOD_TO_INVOKE_ARG)) >= 0)
            {
                int startIdx = idx + NETWORK_METHOD_TO_INVOKE_ARG.Length, endIdx = data.IndexOf(DELIMITER);
                string method = data.Substring(startIdx, endIdx - startIdx);

                if (!string.IsNullOrEmpty(method) && method.Equals(NETWORK_METHOD_TO_UPDATE_TEMP))
                {
                    _Thermometer.UpdateTemperature();

                    Log(NETWORK_LOG_LABEL + "Temperature update was requested." + '\n');
                }
            }
            else
            {
                Log(string.Format(NETWORK_LOG_LABEL + "Received unknown data: \"{0}\"" + '\n', data));
            }
        }

        private void ProcessData(ref List<string> dataSet)
        {
            _DataMutex.WaitOne();

            foreach (string data in dataSet)
            {
                ProcessData(data);
            }

            dataSet.Clear();

            _DataMutex.ReleaseMutex();
        }

        private void Log(string info)
        {
            try
            {
                Dispatcher.Invoke(delegate ()
                {
                    LogTextBlock.AppendText(info);
                    if (_ShouldScrollToEnd)
                    {
                        LogTextBlock.ScrollToEnd();
                    }
                });
            }
            catch (TaskCanceledException)
            {
                if (_VerboseLogging)
                {
                    Log(THERMOMETER_LOG_LABEL + "TaskCancelledException while Log's being executed" + '\n');
                }
            }
        }
    }
}
