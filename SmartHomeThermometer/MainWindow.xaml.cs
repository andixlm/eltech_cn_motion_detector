using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private Thermometer _Thermometer;

        private int _UpdateInterval;

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
        }

        private void Configure()
        {
            Closed += (sender, e) =>
            {
                _Thermometer.Dispose();
            };

            UpdateIntervalSetButton.Click += (sender, e) =>
            {
                try
                {
                    _Thermometer.UpdateInterval = int.Parse(UpdateIntervalTextBlock.Text);
                }
                catch (FormatException exc)
                {
                    Console.WriteLine(exc);
                }
            };

        }
    }
}
