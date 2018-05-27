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
        // Размер буфера для принятия данных.
        private static readonly int BUFFER_SIZE = 8192;
        // Разделитель между элементами данных.
        private static readonly char DELIMITER = ';';
        // Надпись элемента интерфейса.
        private static readonly string IPADDRESS_LOG_LABEL = "IP Address: ";
        // Надпись элемента интерфейса.
        private static readonly string PORT_LOG_LABEL = "Port: ";
        // Минимальное и максимальное значения используемого порта.
        private static readonly int MINIMAL_PORT_VALUE = 1024;
        private static readonly int MAXIMAL_PORT_VALUE = 49151;
        // Метка подключения для журанала.
        private static readonly string CONNECTION_LOG_LABEL = "Connection: ";
        // Состояния подключения.
        private static readonly string CONNECTION_UP = "up";
        private static readonly string CONNECTION_WAIT = "wait";
        private static readonly string CONNECTION_DOWN = "down";
        private static readonly string CONNECTION_ERR = "err";
        // Метка устройства для журнала.
        private static readonly string MOTION_DETECTOR_LOG_LABEL = "Motion Detector: ";
        // Метка сети для журанала.
        private static readonly string NETWORK_LOG_LABEL = "Network: ";
        // Аргумент устройства.
        private static readonly string NETWORK_DEVICE_ARG = "Device: ";
        // Аргумент времени.
        private static readonly string NETWORK_TIME_ARG = "Time: ";
        // Аргумент метода для исполнения.
        private static readonly string NETWORK_METHOD_TO_INVOKE_ARG = "Method: ";
        // Аргумент состояния устройства.
        private static readonly string NETWORK_STATUS_ARG = "Status: ";
        // Метод для отключения устройства.
        private static readonly string NETWORK_METHOD_TO_DISCONNECT = "DISCONNECT";
        // Метод для запроса состояния работы устройства.
        private static readonly string NETWORK_METHOD_TO_REQUEST_STATUS = "REQUEST_STATUS";
        // Корректное состояние устройства.
        private static readonly int DEVICE_STATUS_UP = 42;
        // Генератор случайных чисел.
        private static readonly Random sRandom = new Random();
        // Epoch-time.
        private static readonly DateTime sEpochTime = new DateTime(1970, 1, 1);

        // Расширенный уровень логгирования.
        private bool _VerboseLogging;
        // Автоматическая прокрутка журнала.
        private bool _ShouldScrollToEnd;
        // Сокет.
        private TcpClient _Socket;
        // Поток, принимающий и обрабатывающий данные от сервера.
        private Thread _ListenerThread;
        private Thread _WorkerThread;
        // Мьютекс для синхронизации обращения к данным.
        private Mutex _DataMutex;
        // Кэш данных, полученных от сервера.
        private List<string> _Cache;
        // IP-адрес и порт сервера.
        private IPAddress _IPAddress;
        private int _Port;
        // Работает ли детектор.
        private bool _ShouldWork;
        // Время в секундах относительно 00:00:00 1.1.1970.
        private long _UnixTime;
        // Время обнаружения движния.
        private DateTime _MotionDetectorTime;

        public MainWindow()
        {
            InitializeComponent();
            // Инициализация и настройка приложения.
            Init();
            Configure();
        }
        // Инициализация объектов.
        private void Init()
        {
            _DataMutex = new Mutex();
            _Cache = new List<string>();

            _MotionDetectorTime = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        }
        // Настройка объектов.
        private void Configure()
        {
            // Обнаружение движений работает.
            _ShouldWork = true;
            // Расширенное логгирование отключено.
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
            // Автоматическая прокрутка журнала включена.
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
            // При закрытии приложения.
            Closed += (sender, e) =>
            {
                Disconnect();
                _WorkerThread.Abort();
                _Socket = null;
            };

            /// Controls
            // Кнопка подключения.
            ConnectButton.IsEnabled = true;
            ConnectButton.Click += (sender, e) =>
            {
                Connect();
            };
            // Кнопка отключения.
            DisconnectButton.IsEnabled = false;
            DisconnectButton.Click += (sender, e) =>
            {
                Disconnect();

                /// Bad idea due to bad design.
                _Socket = new TcpClient();
            };
            // Кнопка имитации обнаружения движения.
            FakeButton.Click += (sender, e) =>
            {
                UpdateMotionTime();
                if (_Socket != null && _Socket.Connected)
                {
                    SendMotionTime();
                }
            };
            // Поток, случайным образом имитирующий обнаружение движения.
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

        // Настройка потока, принимающего и обрабатывающего данные от сервера.
        private Thread ConfigureListenerThread()
        {
            return new Thread(new ThreadStart(delegate ()
            {
                try
                {
                    while (_Socket != null && _Socket.Connected)
                    {
                        // Приём данных.
                        byte[] bytes = new byte[BUFFER_SIZE];
                        Receive(ref _Socket, ref bytes);
                        // Кэширование и обработка данных.
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
        // Настройка потока, осуществляющего подключение.
        private Thread ConfigureConnectThread()
        {
            return new Thread(new ThreadStart(delegate ()
            {
                // Обнаружение состояния подключения на ожидание.
                Dispatcher.Invoke(delegate ()
                {
                    ConnectionStateLabel.Content = CONNECTION_WAIT;
                    SwitchButtonsOnConnectionStatusChanged(true);
                });
                Log((CONNECTION_LOG_LABEL +
                    string.Format("Connecting to {0}:{1}\n", _IPAddress.ToString(), _Port)));

                try
                {
                    // Создание сокета.
                    _Socket = new TcpClient();
                    // Подключение к серверу.
                    _Socket.Connect(_IPAddress, _Port);
                    // Обновление состояния подключения на успешное.
                    Dispatcher.Invoke(delegate ()
                    {
                        ConnectionStateLabel.Content = CONNECTION_UP;
                    });
                    Log(CONNECTION_LOG_LABEL +
                        string.Format("Connected to {0}:{1}\n", _IPAddress.ToString(), _Port));
                    // Отправка информации об устройстве.
                    SendInfo();
                    // Обновить и отправить время обнаружения движения.
                    UpdateMotionTime();
                    SendMotionTime();
                    // Запустить поток, принимающий и обрабатывающий данные от сервера.
                    _ListenerThread = ConfigureListenerThread();
                    _ListenerThread.Start();
                }

                catch (SocketException exc)
                {
                    // Ошибка подключения.
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
        // Подключение.
        private void Connect()
        {
            // Чтение IP-адреса.
            try
            {
                _IPAddress = IPAddress.Parse(AddressTextBox.Text);
            }
            catch (Exception exc)
            {
                Log(IPADDRESS_LOG_LABEL + exc.Message + '\n');
                return;
            }
            // Чтение порта.
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
            // Настройка и запуск потока, осуществляющего подключение к серверу.
            Thread connectThread = ConfigureConnectThread();
            connectThread.Start();
        }

        // Отключение.
        private void Disconnect()
        {
            // Отправить метод для отключения.
            SendMethodToInvoke(NETWORK_METHOD_TO_DISCONNECT);
            // Завершение потока, принимающего и обрабатывающего данные от сервера.
            if (_ListenerThread.IsAlive)
            {
                _ListenerThread.Abort();
            }
            // Закрытие сокета.
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
            // Обновить интерфейс.
            SwitchButtonsOnConnectionStatusChanged(false);
            if (_VerboseLogging)
            {
                Log(CONNECTION_LOG_LABEL + "Connection was manually closed" + '\n');
            }
        }
        // Обновить состояние кнопок в зависимости от состояние подключения.
        private void SwitchButtonsOnConnectionStatusChanged(bool isConnected)
        {
            Dispatcher.Invoke(delegate ()
            {
                PortTextBox.IsEnabled = !isConnected;

                ConnectButton.IsEnabled = !isConnected;
                DisconnectButton.IsEnabled = isConnected;
            });
        }
        // Отправка данных.
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
        // Приём данных.
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
        // Отправить информацию об устройстве.
        private void SendInfo()
        {
            byte[] bytes = Encoding.Unicode.GetBytes(NETWORK_DEVICE_ARG + "MotionDetector" + DELIMITER);
            Send(bytes);

            Log(NETWORK_LOG_LABEL + "Sent info" + '\n');
        }
        // Обновить время обнаружения движения на текущее.
        private void UpdateMotionTime()
        {
            _UnixTime = (long)DateTime.UtcNow.Subtract(sEpochTime).TotalSeconds;

            _MotionDetectorTime = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            _MotionDetectorTime = _MotionDetectorTime.AddSeconds(_UnixTime);

            try
            {
                Dispatcher.Invoke(delegate ()
                {
                    MotionTimeValueLabel.Content = _MotionDetectorTime.ToString();
                });
            }

            catch (TaskCanceledException)
            {
                _ShouldWork = false;
            }
        }
        // Отправить время обнаружения движения.
        private void SendMotionTime()
        {
            byte[] bytes = Encoding.Unicode.GetBytes(string.Format(NETWORK_TIME_ARG + "{0}" + DELIMITER, _UnixTime));
            Send(bytes);

            if (_VerboseLogging)
            {
                Log(NETWORK_LOG_LABEL + string.Format("Sent time: {0}", _UnixTime) + '\n');
            }
        }
        // Отправить состояние работы устройства.
        private void SendStatus()
        {
            byte[] bytes = Encoding.Unicode.GetBytes(string.Format(NETWORK_STATUS_ARG + "{0}" + DELIMITER, DEVICE_STATUS_UP));
            Send(bytes);

            if (_VerboseLogging)
            {
                Log(NETWORK_LOG_LABEL + string.Format("Sent status: {0}", DEVICE_STATUS_UP) + '\n');
            }
        }
        // Отправить метод для исполнения.
        private void SendMethodToInvoke(string method)
        {
            byte[] bytes = Encoding.Unicode.GetBytes(NETWORK_METHOD_TO_INVOKE_ARG + method + DELIMITER);
            Send(bytes);

            if (_VerboseLogging)
            {
                Log(NETWORK_LOG_LABEL + "Sent method: " + method + '\n');
            }
        }
        // Кэширование данных.
        string CacheData(string data, ref List<string> cache)
        {
            // Подробно описано в сервере.
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

        // Обработка элемента данных.
        private void ProcessData(string data)
        {
            if (string.IsNullOrEmpty(data) || data.Equals(""))
            {
                return;
            }

            int idx;
            // Метод для исполнения.
            if ((idx = data.IndexOf(NETWORK_METHOD_TO_INVOKE_ARG)) >= 0)
            {
                int startIdx = idx + NETWORK_METHOD_TO_INVOKE_ARG.Length, endIdx = data.IndexOf(DELIMITER);
                string method = data.Substring(startIdx, endIdx - startIdx);
                // Запрос состояния работы устройства.
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
        // Обработка списка элементов данных.
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
        // Добавить запись в журнал.
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
