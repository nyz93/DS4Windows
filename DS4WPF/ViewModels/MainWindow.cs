using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DS4WPF.ViewModels
{
    class MainWindow: ViewModel
    {
        public ObservableCollection<Controller> Controllers = new ObservableCollection<Controller>();

        public MainWindow()
        {

        }
    }
}
