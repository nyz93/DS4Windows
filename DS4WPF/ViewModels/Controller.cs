using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DS4WPF.ViewModels
{
    enum ControllerStatus {
        Bluetooth, Wired
    }
    class BatteryLevel:ViewModel
    {
        private BatteryLevel() {
        }
        private int _level;
        public int Level {
            get { return _level; }
            private set { _level = value; OnPropertyChanged(); }
        }
        public BatteryLevel(int level)
        {
            _level = level;
        }
        public bool IsFull()
        {
            return Level == 100;
        }
    }
    class Controller:ViewModel
    {
        private string _mac;
        public string MACAddress {
            get { return _mac; }
            set { _mac = value; OnPropertyChanged(); }
        }
        private ControllerStatus _status;
        public ControllerStatus Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged(); }
        }
        public BatteryLevel Battery { get; private set; }
        private Profile _profile;
        public Profile CurrentProfile {
            get => _profile;
            private set { _profile = value; OnPropertyChanged(); }
        }
    }
}
