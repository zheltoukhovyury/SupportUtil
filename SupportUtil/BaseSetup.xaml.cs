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

namespace SupportUtil
{
    /// <summary>
    /// Interaction logic for BaseSetup.xaml
    /// </summary>
    /// 

    public class BaseSetupConext
    {
        public String ipAddress { get; set; }
        public UInt16 port { get; set; }
        public bool serverEnabled { get; set; }
    }


    public partial class BaseSetup : UserControl
    {
        static public BaseSetupConext baseSettings = new BaseSetupConext();
        public BaseSetup()
        {
            InitializeComponent();

            baseSettings.ipAddress = "auto.stadis.pro";
            baseSettings.port = 2021;
            baseSettings.serverEnabled = true;

            this.DataContext = baseSettings;

        }
    }
}

