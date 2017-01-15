using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
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
using System.Timers;
using System.Collections.ObjectModel;
using System.Threading;
using System.Xml;
using System.IO;

namespace SupportUtil
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        ISerialCommunication currentPort = null;
        Communication communication = null; 
        List<ISerialCommunication> allPortList = new List<ISerialCommunication>();

        System.Timers.Timer scanTimer;
        List<MenuItem> portMenuList = new List<MenuItem>();
        

        public MainWindow()
        {
            InitializeComponent();
            scanTimer = new System.Timers.Timer(1000);
            scanTimer.Elapsed += ScanTimer_Elapsed;
            scanTimer.Start();

            FillModeSelector();

            saveAsXml.Click += SaveAsXml_Click;
            WriteDevice.Click += WriteDevice_Click;


          
        }

        private void WriteDevice_Click(object sender, RoutedEventArgs e)
        {
            SaveToXml().Save("writtenToDevice.xml");
            communication.BeginSendXml();
        }

        private void SaveAsXml_Click(object sender, RoutedEventArgs e)
        {

            SaveToXml().Save("new.xml");
        }

        XmlDocument SaveToXml()
        {

            XmlDocument document = new XmlDocument();
            document.AppendChild(document.CreateElement("body"));


            XmlNode baseSetup = document.CreateElement("baseSetup");
            baseSetup.AppendChild(CreateOption("ipAddr", "ip", BaseSetup.baseSettings.ipAddress, document));
            baseSetup.AppendChild(CreateOption("port", "uint_16", BaseSetup.baseSettings.port.ToString(), document));
            baseSetup.AppendChild(CreateOption("enabled", "bool", BaseSetup.baseSettings.serverEnabled.ToString(), document));
            document.DocumentElement.AppendChild(baseSetup);

            XmlNode GSMNumberSettings = document.CreateElement("GSMNumberSettings");
            GSMNumberSettings.AppendChild(CreateOption("GSMNumber_1", "GSMNumber", "9601346889", document));
            GSMNumberSettings.AppendChild(CreateOption("GSMNumber_2", "GSMNumber", "9601346889", document));
            document.DocumentElement.AppendChild(GSMNumberSettings);

            return document;
        }


        XmlNode CreateOption(String name, String type, String value, XmlDocument doc)
        {
            XmlNode option = doc.CreateElement(name);
            XmlAttribute typeAttr = doc.CreateAttribute("type");
            typeAttr.Value = type;
            option.InnerText = value;
            option.Attributes.Append(typeAttr);
            return option;
        }



        void FillModeSelector()
        {
            modeSelector.Items.Clear();

            ListBoxItem debugMode = new ListBoxItem() { Content = "Debug" };
            debugMode.Selected += DebugMode_Selected;
            modeSelector.Items.Add(debugMode);

            ListBoxItem baseSetupMode = new ListBoxItem() { Content = "Base Setup" };
            baseSetupMode.Selected += BaseSetupMode_Selected;
            modeSelector.Items.Add(baseSetupMode);

        }

        private void BaseSetupMode_Selected(object sender, RoutedEventArgs e)
        {
            mainControl.Content = new BaseSetup();
        }

        private void DebugMode_Selected(object sender, RoutedEventArgs e)
        {
            mainControl.Content = new DebugControl();
        }

        private void ScanTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            Dispatcher.BeginInvoke(new ThreadStart(delegate
            {
                ISerialCommunication[] pList = ComPortCommunication.ScanPorts();
                List<ISerialCommunication> comportPortList = new List<ISerialCommunication>(pList);
                foreach (ISerialCommunication p in comportPortList)
                {

                    bool match = false;
                    foreach (MenuItem mi in portMenuList)
                    {
                        if ((String)mi.Header == p.Name)
                        {
                            match = true;
                            break;
                        }
                    }
                    if (!match)
                    {
                        MenuItem newPortItem = new MenuItem();
                        newPortItem.Header = p.Name;
                        portMenuList.Add(newPortItem);
                        portList.Items.Add(newPortItem);
                        allPortList.Add(p);
                        newPortItem.Click += PortMenuItem;
                    }
                }

                // так же для других реализаций ISerialCommunication

                MenuItem[] portArray = portMenuList.ToArray();
                for (int i = 0; i < portArray.Length; i++)
                {
                    if (!comportPortList.Exists(p => p.Name == (String)portArray[i].Header))
                    {
                        portMenuList.Remove(portArray[i]);
                        portList.Items.Remove(portArray[i]);
                        allPortList.Remove(allPortList.Find(p1 => p1.Name == (String)portArray[i].Header));
                    }

                }
            }));
        }

        private void PortMenuItem(object sender, RoutedEventArgs e)
        {
            String portName = (String)(sender as MenuItem).Header;
            if (currentPort != null)
                currentPort.Close();
            
            currentPort = allPortList.Find(p1 => p1.Name == portName);
            if (currentPort != null && currentPort.Open())
            {

                communication = new Communication(currentPort);
            }
        }
    }


    class Communication
    {
        Thread thread;
        bool runing = false;
        System.Timers.Timer exchangeCheckTimer;
        bool exchangeCheck = false;
        ISerialCommunication port;
        MemoryStream xmlSettingsStream;
        UInt32 xmlSendAddr;

        enum CommunicationState
        {
            Idle,
            BeginSendXmx,
            SendXmx_sending,
            SendXmx_waitingACK,
        } 

        CommunicationState communicationState;

        public Communication(ISerialCommunication port)
        {
            this.port = port;
            port.packetReceived += Port_packetReceived;
            thread = new Thread(new ThreadStart(Run));
            runing = true;
            thread.Start();

        }

        public void BeginSendXml()
        {

            XmlDocument doc = new XmlDocument();
            doc.Load("writtenToDevice.xml");
            xmlSettingsStream = new MemoryStream();
            doc.Save(xmlSettingsStream);
            xmlSettingsStream.Flush();
            xmlSettingsStream.Position = 0;
            xmlSendAddr = 0;

            if (communicationState == CommunicationState.Idle)
                communicationState = CommunicationState.BeginSendXmx;
        }


        static public Packet Parse(byte[] data)
        {
            Packet packet = new Packet();
            if (data.Length >= 4 && data.Length >= (data[1] + 4))
            {
                switch (data[2])
                {
                    default:
                    case 1:
                        packet.opcode = Opcode.Start_Opcode;
                        break;
                    case 2:
                        packet.opcode = Opcode.ExchangeCheck_Opcode;
                        break;
                    case 3:
                        packet.opcode = Opcode.WriteXmlToDevice;
                        break;
                }
                packet.data = new byte[data[1]];
                packet.packet = data;
                Array.Copy(data, 3, packet.data, 0, data[1]);
                return packet;
            }
            return null;
        }

        static public Packet CreatePacket(Opcode opcode, byte[] data)
        {
            Packet packet = new Packet();
            packet.data = data;
            packet.opcode = opcode;
            packet.packetState = PacketState.New_PacketState;


            packet.packet = new byte[data.Length + 4];

            packet.packet[0] = 0x55;
            packet.packet[1] = Convert.ToByte(data.Length);
            switch (opcode)
            {
                default:
                case Opcode.Start_Opcode:
                    packet.packet[2] = 1;
                    break;
                case Opcode.ExchangeCheck_Opcode:
                    packet.packet[2] = 2;
                    break;
                case Opcode.WriteXmlToDevice:
                    packet.packet[2] = 3;
                    break;
            }
            Array.Copy(data, 0, packet.packet, 3, data.Length);
            packet.packet[3 + data.Length] = 0xCC;
            return packet;
        }


        private void ExchangeCheckTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            exchangeCheck = true;
        }


        private bool Port_packetReceived(Packet packet)
        {
            return true;
        }

        void Run()
        {

            while (runing)
            { 
                byte[] data = new byte[0];
                Packet packet = CreatePacket(Opcode.Start_Opcode, data);
                port.SendPacket(packet);
                while (packet.packetState == PacketState.sended_PacketState || packet.packetState == PacketState.New_PacketState) ;

                if (packet.packetState == PacketState.NACK_PacketState)
                    continue;
                if (packet.packetState == PacketState.ACK_PacketState)
                    break;
            }


            exchangeCheckTimer = new System.Timers.Timer(1000);
            exchangeCheckTimer.Elapsed += ExchangeCheckTimer_Elapsed; ;
            //exchangeCheckTimer.Start();

            communicationState = CommunicationState.Idle;
            Packet sendedPAcket = null;

            while (runing)
            {
                if (exchangeCheck && communicationState == CommunicationState.Idle)
                { 
                    exchangeCheckTimer.Stop();
                    byte[] testData = new byte[0];
                    Packet testPacket = CreatePacket(Opcode.ExchangeCheck_Opcode, testData);
                    port.SendPacket(testPacket);
                    while (testPacket.packetState == PacketState.sended_PacketState || testPacket.packetState == PacketState.New_PacketState) ;
                    exchangeCheckTimer.Start();
                    exchangeCheck = false;
                }



                switch (communicationState)
                {
                    default:
                    case CommunicationState.Idle:
                        break;
                    case CommunicationState.BeginSendXmx:
                        communicationState = CommunicationState.SendXmx_sending;
                        break;
                    case CommunicationState.SendXmx_sending:
                        byte[] data;

                        if (xmlSettingsStream.Length - xmlSettingsStream.Position == 0)
                        {
                            communicationState = CommunicationState.Idle;
                            break;
                        }

                        if (xmlSettingsStream.Length - xmlSettingsStream.Position > 100)
                            data = new byte[4 + 100];
                        else
                            data = new byte[4 + xmlSettingsStream.Length - xmlSettingsStream.Position];

                        BitConverter.GetBytes(xmlSendAddr);

                        xmlSendAddr += (UInt32)data.Length;

                        xmlSettingsStream.Read(data, 4, data.Length - 4);
                        sendedPAcket = CreatePacket(Opcode.WriteXmlToDevice, data);
                        port.SendPacket(sendedPAcket);
                        communicationState = CommunicationState.SendXmx_waitingACK;
                        break;
                    case CommunicationState.SendXmx_waitingACK:
                        if (sendedPAcket.packetState == PacketState.sended_PacketState || sendedPAcket.packetState == PacketState.New_PacketState)
                            break;
                        if(sendedPAcket.packetState == PacketState.ACK_PacketState)
                            communicationState = CommunicationState.SendXmx_sending;
                        if (sendedPAcket.packetState == PacketState.NACK_PacketState)
                            communicationState = CommunicationState.Idle;
                            break;
                }

            }
            exchangeCheckTimer.Stop();
        }   


    } 


}


