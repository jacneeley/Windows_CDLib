using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using static OdnoWindowsApp.CDLib.CDDriveEvents;

namespace OdnoWindowsApp.CDLib
{
    public class CDDriveEvents
    {
        public class DataReadEventArgs : EventArgs {
            private readonly byte[] _data;
            private readonly uint _dataSize;
            internal DataReadEventArgs(byte[] data)
            {
                _data = data;
                _dataSize = (uint)data.Length;
            }

            internal DataReadEventArgs(byte[] data, uint dataSize) {
                _data = data;
                _dataSize = dataSize;
            }

            public byte[] Data
            {
                get { return _data; }
            }

            public uint DataSize
            {
                get { return _dataSize; }
            }
        }

        public class ReadProgressEventArgs : EventArgs { 
            public uint Bytes2Read { get; }
            public uint BytesRead { get; }
            public bool CancelRead { get; set; } = false;
            public ReadProgressEventArgs(uint bytes2Read, uint bytesRead) { 
                Bytes2Read = bytes2Read;
                BytesRead = bytesRead;
            }
        }

        internal enum DeviceChangeEventType { DeviceInserted, DeviceRemoved };
        internal class DeviceChangeEventArgs : EventArgs { 
            public DeviceChangeEventType ChangeType { get; }
            public char Drive { get; }
            public DeviceChangeEventArgs(char drive, DeviceChangeEventType type) {
                Drive = drive;
                ChangeType = type;
            }
        }

        public delegate void CdDataReadEventHandler(object sender, DataReadEventArgs ea);
        public delegate void CdReadProgressEventHandler(object sender, ReadProgressEventArgs ea);
        internal delegate void DeviceChangeEventHandler(object sender, DeviceChangeEventArgs ea);

        internal enum DeviceType : uint {
            DBT_DEVTYP_OEM = 0x00000000,      // oem-defined device type
            DBT_DEVTYP_DEVNODE = 0x00000001,  // devnode number
            DBT_DEVTYP_VOLUME = 0x00000002,   // logical volume
            DBT_DEVTYP_PORT = 0x00000003,     // serial, parallel
            DBT_DEVTYP_NET = 0x00000004       // network resource
        }

        internal enum VolumeChangeFlags : ushort
        {
            DBTF_MEDIA = 0x0001,          // media comings and goings
            DBTF_NET = 0x0002           // network volume
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct DEV_BROADCAST_HDR
        {
            public uint dbch_size;
            public DeviceType dbch_devicetype;
            uint dbch_reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct DEV_BROADCAST_VOLUME {
            public uint dbcv_size;
            public DeviceType dbcv_devicetype;
            uint dbcv_reserved;
            uint dbcv_volume;
            uint dbcv_unitmask;
            public char[] Drives
            {
                get
                {
                    string drvs = "";
                    for (char c = 'A'; c <= 'Z'; c++)
                    {
                        if ((dbcv_unitmask & (1 << (c - 'A'))) != 0)
                        {
                            drvs += c;
                        }
                    }
                    return drvs.ToCharArray();
                }
            }
            public VolumeChangeFlags dbcv_flags;
        }

        /// <summary>
        /// Use Windows GUI API to display info about CDROM Operations 
        /// </summary>
        internal class DeviceChangeNotificationWindow : NativeWindow {
            public event DeviceChangeEventHandler DeviceChange;

            const int WS_EX_TOOLWINDOW = 0x80;
            const int WS_POPUP = unchecked((int)0x80000000);

            const int WM_DEVICECHANGE = 0x0219;

            const int DBT_APPYBEGIN = 0x0000;
            const int DBT_APPYEND = 0x0001;
            const int DBT_DEVNODES_CHANGED = 0x0007;
            const int DBT_QUERYCHANGECONFIG = 0x0017;
            const int DBT_CONFIGCHANGED = 0x0018;
            const int DBT_CONFIGCHANGECANCELED = 0x0019;
            const int DBT_MONITORCHANGE = 0x001B;
            const int DBT_SHELLLOGGEDON = 0x0020;
            const int DBT_CONFIGMGAPI32 = 0x0022;
            const int DBT_VXDINITCOMPLETE = 0x0023;
            const int DBT_VOLLOCKQUERYLOCK = 0x8041;
            const int DBT_VOLLOCKLOCKTAKEN = 0x8042;
            const int DBT_VOLLOCKLOCKFAILED = 0x8043;
            const int DBT_VOLLOCKQUERYUNLOCK = 0x8044;
            const int DBT_VOLLOCKLOCKRELEASED = 0x8045;
            const int DBT_VOLLOCKUNLOCKFAILED = 0x8046;
            const int DBT_DEVICEARRIVAL = 0x8000;
            const int DBT_DEVICEQUERYREMOVE = 0x8001;
            const int DBT_DEVICEQUERYREMOVEFAILED = 0x8002;
            const int DBT_DEVICEREMOVEPENDING = 0x8003;
            const int DBT_DEVICEREMOVECOMPLETE = 0x8004;
            const int DBT_DEVICETYPESPECIFIC = 0x8005;

            public DeviceChangeNotificationWindow() { 
                CreateParams Params = new CreateParams();
                Params.ExStyle = WS_EX_TOOLWINDOW;
                Params.Style = WS_POPUP;
                CreateHandle(Params);
            }

            private void OnCDChange(DeviceChangeEventArgs ea) {
                DeviceChange?.Invoke(this, ea);
            }

            private void OnDeviceChange(DEV_BROADCAST_VOLUME devDesc, DeviceChangeEventType et) {
                if (DeviceChange != null) {
                    foreach (char ch in devDesc.Drives) {
                        DeviceChangeEventArgs args = new DeviceChangeEventArgs(ch, et);
                        DeviceChange(this, args);
                    }
                }
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == WM_DEVICECHANGE) {
                    Nullable<DEV_BROADCAST_HDR> head;
                    switch (m.WParam.ToInt32()) {
                        case DBT_DEVICEARRIVAL:
                            head = (DEV_BROADCAST_HDR)Marshal.PtrToStructure(m.LParam, typeof(DEV_BROADCAST_HDR));
                            if (!head.HasValue) {
                                throw new Exception("No header.");
                            }
                            if (head.Value.dbch_devicetype == DeviceType.DBT_DEVTYP_VOLUME) {
                                Nullable<DEV_BROADCAST_VOLUME> devDesc = (DEV_BROADCAST_VOLUME)Marshal.PtrToStructure(m.LParam, typeof(DEV_BROADCAST_VOLUME));
                                if (!devDesc.HasValue) {
                                    throw new Exception("Volume could not be unboxed: likely uninstantiated.");
                                }
                                
                                if (devDesc.Value.dbcv_flags == VolumeChangeFlags.DBTF_MEDIA)
                                {
                                    OnDeviceChange(devDesc.Value, DeviceChangeEventType.DeviceInserted);
                                }
                            }
                            break;
                        case DBT_DEVICEREMOVECOMPLETE:
                            head = (DEV_BROADCAST_HDR)Marshal.PtrToStructure(m.LParam, typeof(DEV_BROADCAST_HDR));
                            if (!head.HasValue)
                            {
                                throw new Exception("No header.");
                            }

                            if (head.Value.dbch_devicetype == DeviceType.DBT_DEVTYP_DEVNODE) { 
                                Nullable<DEV_BROADCAST_VOLUME> devDesc = (DEV_BROADCAST_VOLUME)Marshal.PtrToStructure(m.LParam, typeof(DEV_BROADCAST_VOLUME));
                                if (!devDesc.HasValue) {
                                    throw new Exception("Volume could not be unboxed: likely uninstantiated.");
                                }

                                if (devDesc.Value.dbcv_flags == VolumeChangeFlags.DBTF_MEDIA) { 
                                    OnDeviceChange(devDesc.Value, DeviceChangeEventType.DeviceRemoved);
                                }
                            }
                            break;
                    }
                }

                base.WndProc(ref m);
            }
        }
    }
}
