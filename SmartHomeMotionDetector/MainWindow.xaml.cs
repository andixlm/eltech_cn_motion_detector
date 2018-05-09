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

namespace SmartHomeMotionDetector
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

        private static readonly string MOTION_DETECTOR_LOG_LABEL = "Motion Detector: ";

        private static readonly string NETWORK_LOG_LABEL = "Network: ";

        private static readonly string NETWORK_DEVICE_ARG = "Device: ";
        private static readonly string NETWORK_TIME_ARG = "Time: ";
        private static readonly string NETWORK_METHOD_TO_INVOKE_ARG = "Method: ";
        private static readonly string NETWORK_STATUS_ARG = "Status: ";

        private static readonly string NETWORK_METHOD_TO_DISCONNECT = "DISCONNECT";
        private static readonly string NETWORK_METHOD_TO_REQUEST_STATUS = "REQUEST_STATUS";

        private static readonly int DEVICE_STATUS_UP = 42;

        private static readonly Random sRandom = new Random();

        private static readonly DateTime sEpochTime = new DateTime(1970, 1, 1);

        private bool _VerboseLogging;
        private bool _ShouldScrollToEnd;

        private TcpClient _Socket;

        private Thread _ListenerThread;
        private Thread _WorkerThread;

        private Mutex _DataMutex;

        private List<string> _Cache;

        private IPAddress _IPAddress;
        private int _Port;

        private bool _ShouldWork;

        private long _UnixTime;
        private DateTime _MotionDetectorTime;

        public MainWindow()
        {
            InitializeComponent();

            Init();
            Configure();
        }

        private void Init()
        {
            _DataMutex = new Mutex();
            _Cache = new List<string>();

            _MotionDetectorTime = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        }

        private void Configure()
        {
            _ShouldWork = true;

            _VerboseLogging = false;
            VerobseLoggingCheckBox.IsChecked = _VerboseLogging;
            VerobseLoggingCheckBox.Checked += (sender, e) =>
            {
                _VerboseLogging = true;
            };
            VerobseLoggingCheckBox.Unchecked += (sender, e) =>
            {
                _VerboseLogging = false;
            };

            _ShouldScrollToEnd = true;
            ScrollToEndCheckBox.IsChecked = _ShouldScrollToEnd;
            ScrollToEndCheckBox.Checked += (sender, e) =>
            {
                _ShouldScrollToEnd = true;
            };
            ScrollToEndCheckBox.Unchecked += (sender, e) =>
            {
                _ShouldScrollToEnd = false;
            };

            /// App
            Closed += (sender, e) =>
            {
                Disconnect();
                _WorkerThread.Abort();
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

            FakeButton.Click += (sender, e) =>
            {
                UpdateMotionTime();
                if (_Socket != null && _Socket.Connected)
                {
                    SendMotionTime();
                }
            };

            _WorkerThread = new Thread(new ThreadStart(delegate ()
            {
                try
                {
                    while (_ShouldWork)
                    {
                        UpdateMotionTime();
                        if (_Socket != null && _Socket.Connected)
                        {
                            SendMotionTime();
                        }

                        Thread.Sleep(sRandom.Next(1000, 10000));
                    }
                }
                catch (ThreadAbortException)
                {
                    _ShouldWork = false;
                    if (_VerboseLogging)
                    {
                        Log(NETWORK_LOG_LABEL + "Worker thread was terminated" + '\n');
                    }
                }
            }));
            _WorkerThread.Start();
        }

        private Thread ConfigureListenerThread()
        {
            return new Thread(new ThreadStart(delegate ()
            {
                try
                {
                    while (_Socket != null && _Socket.Connected)
                    {
                        byte[] bytes = new byte[BUFFER_SIZE];
                        Receive(ref _Socket, ref bytes);

                        ProcessData(CacheData(Encoding.Unicode.GetString(bytes), ref _Cache));
                        ProcessData(ref _Cache);
                    }
                }
                catch (ThreadAbortException)
                {
                    Log(NETWORK_LOG_LABEL + "Disconnected." + '\n');
                    if (_VerboseLogging)
                    {
                        Log(NETWORK_LOG_LABEL + "Listener thread was terminated" + '\n');
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
                    UpdateMotionTime();
                    SendMotionTime();

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
                PortTextBox.IsEnabled = !isConnected;

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
                else
                {
                    Log(CONNECTION_LOG_LABEL + "Connection's unavailable." + '\n');
                }
            }
        }

        private void Receive(ref TcpClient socket, ref byte[] bytes)
        {
            if (_Socket == null)
            {
                return;
            }

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
                else
                {
                    Log(CONNECTION_LOG_LABEL + "Connection's unavailable." + '\n');
                }
            }
        }

        private void SendInfo()
        {
            byte[] bytes = Encoding.Unicode.GetBytes(NETWORK_DEVICE_ARG + "MotionDetector" + DELIMITER);
            Send(bytes);

            Log(NETWORK_LOG_LABEL + "Sent info" + '\n');
        }

        private void UpdateMotionTime()
        {
            _UnixTime = (long) DateTime.UtcNow.Subtract(sEpochTime).TotalSeconds;

            _MotionDetectorTime = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            _MotionDetectorTime = _MotionDetectorTime.AddSeconds(_UnixTime);

            Dispatcher.Invoke(delegate ()
            {
                MotionTimeValueLabel.Content = _MotionDetectorTime.ToString();
            });
        }

        private void SendMotionTime()
        {
            byte[] bytes = Encoding.Unicode.GetBytes(string.Format(NETWORK_TIME_ARG + "{0}" + DELIMITER, _UnixTime));
            Send(bytes);

            if (_VerboseLogging)
            {
                Log(NETWORK_LOG_LABEL + string.Format("Sent time: {0}", _UnixTime) + '\n');
            }
        }

        private void SendStatus()
        {
            byte[] bytes = Encoding.Unicode.GetBytes(string.Format(NETWORK_STATUS_ARG + "{0}" + DELIMITER, DEVICE_STATUS_UP));
            Send(bytes);

            if (_VerboseLogging)
            {
                Log(NETWORK_LOG_LABEL + string.Format("Sent status: {0}", DEVICE_STATUS_UP) + '\n');
            }
        }

        private void SendMethodToInvoke(string method)
        {
            byte[] bytes = Encoding.Unicode.GetBytes(NETWORK_METHOD_TO_INVOKE_ARG + method + DELIMITER);
            Send(bytes);

            if (_VerboseLogging)
            {
                Log(NETWORK_LOG_LABEL + "Sent method: " + method + '\n');
            }
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
            if ((idx = data.IndexOf(NETWORK_METHOD_TO_INVOKE_ARG)) >= 0)
            {
                int startIdx = idx + NETWORK_METHOD_TO_INVOKE_ARG.Length, endIdx = data.IndexOf(DELIMITER);
                string method = data.Substring(startIdx, endIdx - startIdx);

                if (!string.IsNullOrEmpty(method) && method.Equals(NETWORK_METHOD_TO_REQUEST_STATUS))
                {
                    if (_VerboseLogging)
                    {
                        Log(NETWORK_LOG_LABEL + "Status was requested." + '\n');
                    }

                    SendStatus();
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
                return;
            }
        }
    }
}
