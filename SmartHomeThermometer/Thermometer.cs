using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SmartHomeThermometer
{
    class Thermometer
    {
        private static readonly double DEFAULT_TEMPERATURE = 0.0;
        private static readonly double DELTA_TEMPERATURE = 1.0;

        private static readonly int DEFAULT_UPDATE_INTERVAL = 1000;

        private static readonly Random sRandom = new Random();

        private Mutex _Mutex;
        private Thread _WorkerThread;

        private double _Temperature;
        public double Temperature
        {
            get
            {
                return _Temperature;
            }
        }

        private int _UpdateInterval;
        public int UpdateInterval
        {
            get
            {
                _Mutex.WaitOne();
                int interval = _UpdateInterval;
                _Mutex.ReleaseMutex();

                return interval;
            }

            set
            {
                if (value < 1)
                    throw new ArgumentOutOfRangeException("UpdateInterval can not be negative");

                _Mutex.WaitOne();
                _UpdateInterval = value;
                _Mutex.ReleaseMutex();
            }
        }

        private DateTime _LastUpdateTime;
        public DateTime LastUpdateTime
        {
            get
            {
                return _LastUpdateTime;
            }
        }

        public Thermometer()
        {
            _Temperature = DEFAULT_TEMPERATURE;
            _UpdateInterval = DEFAULT_UPDATE_INTERVAL;
            _LastUpdateTime = DateTime.Now;

            _Mutex = new Mutex();
            _WorkerThread = new Thread(new ThreadStart(Run));
            _WorkerThread.Start();
        }

        ~Thermometer()
        {
            _WorkerThread.Abort();
        }

        private void Run()
        {
            while (true)
            {
                UpdateTemperature();

                _Mutex.WaitOne();
                int interval = _UpdateInterval;
                _Mutex.ReleaseMutex();

                Thread.Sleep(interval);
            }
        }

        private void UpdateTemperature()
        {
            _Mutex.WaitOne();

            _Temperature += (sRandom.NextDouble() < 0.5 ? -1.0 : 1.0) * sRandom.NextDouble() * DELTA_TEMPERATURE;
            _LastUpdateTime = DateTime.Now;

            _Mutex.ReleaseMutex();
        }

    }
}
