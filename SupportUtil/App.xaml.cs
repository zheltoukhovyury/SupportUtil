using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Threading;
using System.Timers;
using System.Threading.Tasks;
using System.Windows;
using System.ComponentModel;
using System.IO.Ports;

namespace SupportUtil
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {

    }

    public delegate bool PacketRceived(Packet packet);
    public enum PortState
    {
        NA,
        Closed,
        Opened,
    };


    public interface ISerialCommunication
    {
        PortState State { get; }
        string Name { get; set; }
        bool Open();
        void Close();
        event PacketRceived packetReceived;
        bool SendPacket(Packet packet);
    }


    public enum Opcode
    {
        Start_Opcode = 1,
        ExchangeCheck_Opcode = 2,
        WriteXmlToDevice = 3,
    }

    public enum PacketState
    {
        New_PacketState,
        sended_PacketState,
        ACK_PacketState,
        NACK_PacketState,
    }


    public class Packet
    {
        public Opcode opcode { get; set; }
        public PacketState packetState { get; set; }
        public byte[] data { get; set; }
        public byte[] packet { get; set; }
    }


    public class ComPortCommunication : ISerialCommunication, INotifyPropertyChanged
    {
        System.IO.Ports.SerialPort port;
        Packet currentPAcket;
        System.Timers.Timer sendingTimer;
        string name;
        PortState state = PortState.NA;
        Thread portThread;
        
        public string Name { get { return name; } set { name = value; } }

        public PortState State { get { return state; } }

        public event PropertyChangedEventHandler PropertyChanged;
        public event PacketRceived packetReceived;

        public void Close()
        {
            state = PortState.Closed;
            portThread.Join();
        }

        public bool Open()
        {
            port = new SerialPort();
            port.PortName = name;
            port.BaudRate = 115200;
            port.Parity = Parity.None;
            port.DataBits = 8;
            port.StopBits = StopBits.One;
            port.Handshake = Handshake.None;
            if (port.IsOpen)
                port.Close();
            port.Open();
            state = PortState.Opened;
            port.ReadTimeout = 1000;
            portThread = new Thread(new ThreadStart(PortThread));
            portThread.Start();
            if(port.IsOpen)
                return true;
            else
                return false;
        }

        static public ISerialCommunication[] ScanPorts()
        {
            string[] comPortNameList = SerialPort.GetPortNames();
            ISerialCommunication[] resultList = new ISerialCommunication[comPortNameList.Length];
            for (int i = 0; i < resultList.Length; i++)
            {
                resultList[i] = new ComPortCommunication();
                resultList[i].Name = comPortNameList[i];
            }
            return resultList;
        }



        void PortThread()
        {
            byte[] rcvPacket = new byte[260];
            int rcvP = 0;
            while (state == PortState.Opened)
            {
                Thread.Sleep(1);
                byte[] readByte = new byte[1];
                try
                {
                    //port.BaseStream.Read(readByte, 0, 1);
                    int test = port.Read(readByte, 0, 1);
                    rcvPacket[rcvP++] = readByte[0];

                    if (rcvPacket[0] != 0x55)
                        rcvP = 0;
                    if (rcvP >= 2 && rcvP >= (4 + rcvPacket[1]))
                    {
                        byte[] data = new byte[4 + rcvPacket[1]];
                        Array.Copy(rcvPacket, 0, data, 0, data.Length);
                        Packet rcvedPacket = Communication.Parse(data);

                        if (currentPAcket != null && currentPAcket.packetState == PacketState.sended_PacketState && currentPAcket.opcode == rcvedPacket.opcode)
                        {
                            sendingTimer.Stop();
                            currentPAcket.packetState = PacketState.ACK_PacketState;
                            
                        }
                        else if (packetReceived != null)
                        {
                            bool processed = packetReceived.Invoke(rcvedPacket);
                        }
                        rcvP = 0;
                    }




                }
                catch (Exception)
                {
                }
            }
        }

        bool ISerialCommunication.SendPacket(Packet packet)
        {
            port.Write(packet.packet, 0, packet.packet.Length);
            currentPAcket = packet;
            currentPAcket.packetState = PacketState.sended_PacketState;
            if (sendingTimer != null)
            {
                sendingTimer.Stop();
                sendingTimer.Dispose();
            }
            sendingTimer = new System.Timers.Timer(1000);
            sendingTimer.Elapsed += (object sender, ElapsedEventArgs e) => {
                sendingTimer.Stop();
                currentPAcket.packetState = PacketState.NACK_PacketState;
            };
            sendingTimer.Start();


            return true;
        }

    }
}
