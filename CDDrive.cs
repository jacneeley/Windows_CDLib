using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using static OdnoWindowsApp.CDLib.CDDriveEvents;
using CDLib;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using OdnoWindowsApp.Core;
using NAudio.Wave;

namespace OdnoWindowsApp.CDLib
{
    public class CDBufferFiller
    {
        byte[] BufferArray;
        int WritePosition = 0;

        public CDBufferFiller(byte[] aBuffer) {
            BufferArray = aBuffer;
        }

        public void OnCDDataRead(object sender, DataReadEventArgs ea) {
            Buffer.BlockCopy(ea.Data, 0, BufferArray, WritePosition, (int)ea.DataSize);
            WritePosition += (int)ea.DataSize;
        }
    }
    public class CDDrive : IDisposable
    {
        private IntPtr cdHandle;
        private bool TocValid = false;
        private Win32Functions.CDROM_TOC Toc = null;
        private char m_Drive = '\0';
        private DeviceChangeNotificationWindow NotWnd = null;

        public event EventHandler CDInserted;
        public event EventHandler CDRemoved;

        public CDDrive() { 
            Toc = new Win32Functions.CDROM_TOC();
            cdHandle = IntPtr.Zero;
        }

        public bool ReadDisc(char Drive)
        {
            if (Win32Functions.GetDriveType(Drive + ":\\") == Win32Functions.DriveTypes.DRIVE_CDROM)
            {
                cdHandle = Win32Functions.CreateFile("\\\\.\\" + Drive + ":",
                    Win32Functions.GENERIC_READ,
                    Win32Functions.FILE_SHARE_READ, IntPtr.Zero,
                    Win32Functions.OPEN_EXISTING, 0, IntPtr.Zero);

                if (Toggler)
                {
                    m_Drive = Drive;
                    NotWnd = new DeviceChangeNotificationWindow();
                    NotWnd.DeviceChange += new DeviceChangeEventHandler(NotWnd_DeviceChange);
                    return true;
                }

                return false;
            }

            return false;
        }

        public bool Open(char Drive) {
            Close();
            if (Win32Functions.GetDriveType(Drive + ":\\") == Win32Functions.DriveTypes.DRIVE_CDROM) { 
                cdHandle = Win32Functions.CreateFile("\\\\.\\" + Drive + ":", 
                    Win32Functions.GENERIC_READ,
                    Win32Functions.FILE_SHARE_READ, IntPtr.Zero,
                    Win32Functions.OPEN_EXISTING, 0, IntPtr.Zero);

                if(Toggler){
                    m_Drive = Drive;
                    NotWnd = new DeviceChangeNotificationWindow();
                    NotWnd.DeviceChange += new DeviceChangeEventHandler(NotWnd_DeviceChange);
                    EjectCD();
                    return true;
                }

                return false;
            }

            return false;
        }

        public void Close() {
            UnlockCD();

            if (NotWnd != null) {
                NotWnd.DestroyHandle();
                NotWnd = null;
            }
            if (Toggler){
                Win32Functions.CloseHandle(cdHandle);
            }

            cdHandle = IntPtr.Zero;
            m_Drive = '\0';
            TocValid = false;
        }

        public bool Toggler
        {
            get {
                return ((int)cdHandle != -1) && ((int)cdHandle != 0);
            }
        }

        protected bool ReadToc() {
            if (IsCDReady()) {
                if (Toggler)
                {
                    uint BytesRead = 0;
                    TocValid = Win32Functions.DeviceIoControl(
                        cdHandle, Win32Functions.IOCTL_CDROM_READ_TOC,
                        IntPtr.Zero,
                        0,
                        Toc,
                        (uint)Marshal.SizeOf(Toc),
                        ref BytesRead, IntPtr.Zero) != 0;
                }
            }
            
            else { 
                TocValid= false;
            }

            return TocValid;
        }

        protected int GetStartSector(int track)
        {
            //start reading cdda bytes by sector
            if (TocValid && (track >= Toc.FirstTrack) && (track <= Toc.LastTrack))
            {
                Win32Functions.TRACK_DATA td = Toc.TrackData[track - 1];
                return (td.Address_1 * 60 * 75 + td.Address_2 * 75 + td.Address_3) - 150;
            }
            else
            {
                return -1;
            }
        }

        protected int GetEndSector(int track) {
            if (TocValid && (track >= Toc.FirstTrack) && (track <= Toc.LastTrack)) {
                Win32Functions.TRACK_DATA td = Toc.TrackData[track];
                return (td.Address_1 * 60 * 75 + td.Address_2 * 75 + td.Address_3) - 151;
            } 
            else { return -1; }
        }

        /* CD Standards */
        protected const int NSECTORS = 13;
        protected const int UNDERSAMPLING = 1;
        protected const int CB_CDDASECTOR = 2368;
        protected const int CB_QSUBCHANNEL = 16;
        protected const int CB_CDROMSECTOR = 2048;
        protected const int CB_AUDIO = (CB_CDDASECTOR - CB_QSUBCHANNEL);

        /// <summary>
        /// 
        /// </summary>
        /// <param name="sector"></param>
        /// <param name="Buffer"></param>
        /// <param name="NumSectors"></param>
        /// <returns></returns>
        protected bool ReadSector(int sector, byte[] Buffer, int NumSectors)
        {
            if (TocValid && ((sector + NumSectors) <= GetEndSector(Toc.LastTrack)) && (Buffer.Length >= CB_AUDIO * NumSectors))
            {
                Win32Functions.RAW_READ_INFO rri = new Win32Functions.RAW_READ_INFO();
                rri.TrackMode = Win32Functions.TRACK_MODE_TYPE.CDDA;
                rri.SectorCount = (uint)NumSectors;
                rri.DiskOffset = sector * CB_CDROMSECTOR;

                uint BytesRead = 0;
                bool valid = Win32Functions.DeviceIoControl(
                    cdHandle,
                    Win32Functions.IOCTL_CDROM_RAW_READ,
                    rri,
                    (uint)Marshal.SizeOf(rri),
                    Buffer,
                    (uint)NumSectors * CB_AUDIO,
                    ref BytesRead,
                    IntPtr.Zero) != 0;
                
                if (valid)
                {
                    return true;
                }

                return false;
            }
            
            return false;
        }

        public bool LockCD() {
            if (Toggler) {
                uint Dummy = 0;
                Win32Functions.PREVENT_MEDIA_REMOVAL pmr = new Win32Functions.PREVENT_MEDIA_REMOVAL();
                pmr.PreventMediaRemoval = 1;
                return Win32Functions.DeviceIoControl(
                    cdHandle,
                    Win32Functions.IOCTL_STORAGE_MEDIA_REMOVAL,
                    pmr,
                    (uint)Marshal.SizeOf(pmr),
                    IntPtr.Zero,
                    0,
                    ref Dummy,
                    IntPtr.Zero) != 0;
            }

            return false;
        }

        public bool UnlockCD() {
            if (Toggler) {
                uint Dummy = 0;
                Win32Functions.PREVENT_MEDIA_REMOVAL pmr = new Win32Functions.PREVENT_MEDIA_REMOVAL();
                pmr.PreventMediaRemoval = 0;
                return Win32Functions.DeviceIoControl(
                    cdHandle,
                    Win32Functions.IOCTL_STORAGE_MEDIA_REMOVAL,
                    pmr,
                    (uint)Marshal.SizeOf(pmr),
                    IntPtr.Zero,
                    0,
                    ref Dummy,
                    IntPtr.Zero) != 0;
            }

            return false;
        }

        public bool LoadCD() { 
            TocValid = false;
            if (Toggler) {
                uint Dummy = 0;
                return Win32Functions.DeviceIoControl(
                    cdHandle,
                    Win32Functions.IOCTL_STORAGE_LOAD_MEDIA,
                    IntPtr.Zero,
                    0,
                    IntPtr.Zero,
                    0,
                    ref Dummy,
                    IntPtr.Zero) != 0;
            }

            return false;
        }

        public bool EjectCD() {
            TocValid = false;
            if (Toggler) {
                uint Dummy = 0;
                return Win32Functions.DeviceIoControl(
                        cdHandle,
                        Win32Functions.IOCTL_STORAGE_EJECT_MEDIA,
                        IntPtr.Zero,
                        0,
                        IntPtr.Zero,
                        0,
                        ref Dummy,
                        IntPtr.Zero) != 0;
            }

            return false;
        }

        public bool IsCDReady() {
            if (Toggler) {
                uint Dummy = 0;
                bool ready = Win32Functions.DeviceIoControl(
                    cdHandle,
                    Win32Functions.IOCTL_STORAGE_CHECK_VERIFY,
                    IntPtr.Zero,
                    0,
                    IntPtr.Zero,
                    0,
                    ref Dummy, 
                    IntPtr.Zero) != 0;

                if (!ready)
                {
                    TocValid = false;
                    return false;
                }

                return ready;
            }

            TocValid = false;
            return false;
            
        }

        public bool Refresh() {
            if (IsCDReady()) {
                return ReadToc();
            }

            return false;
        }

        public int GetNumOfTracks() {
            if (TocValid) {
                return Toc.LastTrack - Toc.FirstTrack + 1;
            }
            
            return -1;
        }

        /// <summary>
        /// Return the number of audio tracks on the CD
        /// </summary>
        /// <returns>-1 on error</returns>
        public int GetNumAudioTracks()
        {
            if (TocValid)
            {
                int tracks = 0;
                for (int i = Toc.FirstTrack - 1; i < Toc.LastTrack; i++)
                {
                    if (Toc.TrackData[i].Control == 0)
                        tracks++;
                }
                return tracks;
            }
            else
            {
                return -1;
            }

        }
        /// <summary>
        /// Read the digital data of the track
        /// </summary>
        /// <param name="track">Track to read</param>
        /// <param name="Data">Buffer that will receive the data</param>
        /// <param name="DataSize">On return the size needed to read the track</param>
        /// <param name="StartSecond">First second of the track to read, 0 means to start at beginning of the track</param>
        /// <param name="Seconds2Read">Number of seconds to read, 0 means to read until the end of the track</param>
        /// <param name="OnProgress">Delegate to indicate the reading progress</param>
        /// <returns>Negative value means an error. On success returns the number of bytes read</returns>
        public int ReadTrack(int track, byte[] Data, ref uint DataSize, uint StartSecond, uint Seconds2Read, CdReadProgressEventHandler ProgressEvent)
        {
            if (TocValid && (track >= Toc.FirstTrack) && (track <= Toc.LastTrack))
            {
                int StartSect = GetStartSector(track);
                int EndSect = GetEndSector(track);
                if ((StartSect += (int)StartSecond * 75) >= EndSect)
                {
                    StartSect -= (int)StartSecond * 75;
                }
                if ((Seconds2Read > 0) && ((int)(StartSect + Seconds2Read * 75) < EndSect))
                {
                    EndSect = StartSect + (int)Seconds2Read * 75;
                }
                DataSize = (uint)(EndSect - StartSect) * CB_AUDIO;
                if (Data != null)
                {
                    if (Data.Length >= DataSize)
                    {
                        CDBufferFiller BufferFiller = new CDBufferFiller(Data);
                        return ReadTrack(track, new CdDataReadEventHandler(BufferFiller.OnCDDataRead), StartSecond, Seconds2Read, ProgressEvent);
                    }
                    else
                    {
                        return 0;
                    }
                }
                else
                {
                    return 0;
                }
            }
            else
            {
                return -1;
            }
        }
        /// <summary>
        /// Read the digital data of the track
        /// </summary>
        /// <param name="track">Track to read</param>
        /// <param name="Data">Buffer that will receive the data</param>
        /// <param name="DataSize">On return the size needed to read the track</param>
        /// <param name="OnProgress">Delegate to indicate the reading progress</param>
        /// <returns>Negative value means an error. On success returns the number of bytes read</returns>
        public int ReadTrack(int track, byte[] Data, ref uint DataSize, CdReadProgressEventHandler ProgressEvent)
        {
            return ReadTrack(track, Data, ref DataSize, 0, 0, ProgressEvent);
        }
        /// <summary>
        /// Read the digital data of the track
        /// </summary>
        /// <param name="track">Track to read</param>
        /// <param name="OnDataRead">Call each time data is read</param>
        /// <param name="StartSecond">First second of the track to read, 0 means to start at beginning of the track</param>
        /// <param name="Seconds2Read">Number of seconds to read, 0 means to read until the end of the track</param>
        /// <param name="OnProgress">Delegate to indicate the reading progress</param>
        /// <returns>Negative value means an error. On success returns the number of bytes read</returns>
        public int ReadTrack(int track, CdDataReadEventHandler DataReadEvent, uint StartSecond, uint Seconds2Read, CdReadProgressEventHandler ProgressEvent)
        {
            if (TocValid && (track >= Toc.FirstTrack) && (track <= Toc.LastTrack) && (DataReadEvent != null))
            {
                int StartSect = GetStartSector(track);
                int EndSect = GetEndSector(track);
                if ((StartSect += (int)StartSecond * 75) >= EndSect)
                {
                    StartSect -= (int)StartSecond * 75;
                }
                if ((Seconds2Read > 0) && ((int)(StartSect + Seconds2Read * 75) < EndSect))
                {
                    EndSect = StartSect + (int)Seconds2Read * 75;
                }
                uint Bytes2Read = (uint)(EndSect - StartSect) * CB_AUDIO;
                uint BytesRead = 0;
                byte[] Data = new byte[CB_AUDIO * NSECTORS];
                bool Cont = true;
                bool ReadOk = true;
                if (ProgressEvent != null)
                {
                    ReadProgressEventArgs rpa = new ReadProgressEventArgs(Bytes2Read, 0);
                    ProgressEvent(this, rpa);
                    Cont = !rpa.CancelRead;
                }
                for (int sector = StartSect; (sector < EndSect) && (Cont) && (ReadOk); sector += NSECTORS)
                {
                    int Sectors2Read = ((sector + NSECTORS) < EndSect) ? NSECTORS : (EndSect - sector);
                    ReadOk = ReadSector(sector, Data, Sectors2Read);
                    if (ReadOk)
                    {
                        DataReadEventArgs dra = new DataReadEventArgs(Data, (uint)(CB_AUDIO * Sectors2Read));
                        DataReadEvent(this, dra);
                        BytesRead += (uint)(CB_AUDIO * Sectors2Read);
                        if (ProgressEvent != null)
                        {
                            ReadProgressEventArgs rpa = new ReadProgressEventArgs(Bytes2Read, BytesRead);
                            ProgressEvent(this, rpa);
                            Cont = !rpa.CancelRead;
                        }
                    }
                }
                if (ReadOk)
                {
                    return (int)BytesRead;
                }
                else
                {
                    return -1;
                }

            }
            else
            {
                return -1;
            }
        }
        /// <summary>
        /// Read the digital data of the track
        /// </summary>
        /// <param name="track">Track to read</param>
        /// <param name="OnDataRead">Call each time data is read</param>
        /// <param name="OnProgress">Delegate to indicate the reading progress</param>
        /// <returns>Negative value means an error. On success returns the number of bytes read</returns>
        public int ReadTrack(int track, CdDataReadEventHandler DataReadEvent, CdReadProgressEventHandler ProgressEvent)
        {
            return ReadTrack(track, DataReadEvent, 0, 0, ProgressEvent);
        }

        /// <summary>
        /// Get track size
        /// </summary>
        /// <param name="track">Track</param>
        /// <returns>Size in bytes of track data</returns>
        public uint TrackSize(int track)
        {
            uint Size = 0;
            ReadTrack(track, null, ref Size, null);
            return Size;
        }

        public bool IsAudioTrack(int track)
        {
            if ((TocValid) && (track >= Toc.FirstTrack) && (track <= Toc.LastTrack))
            {
                return (Toc.TrackData[track - 1].Control & 4) == 0;
            }
            else
            {
                return false;
            }
        }

        public static char[] GetCDDriveLetters()
        {
            string res = "";
            for (char c = 'C'; c <= 'Z'; c++)
            {
                if (Win32Functions.GetDriveType(c + ":") == Win32Functions.DriveTypes.DRIVE_CDROM)
                {
                    res += c;
                }
            }
            return res.ToCharArray();
        }

        private void OnCDInserted()
        {
            if (CDInserted != null)
            {
                CDInserted(this, EventArgs.Empty);
            }
        }

        private void OnCDRemoved()
        {
            if (CDRemoved != null)
            {
                CDRemoved(this, EventArgs.Empty);
            }
        }

        private void NotWnd_DeviceChange(object sender, DeviceChangeEventArgs ea)
        {
            if (ea.Drive == m_Drive)
            {
                TocValid = false;
                switch (ea.ChangeType)
                {
                    case DeviceChangeEventType.DeviceInserted:
                        OnCDInserted();
                        break;
                    case DeviceChangeEventType.DeviceRemoved:
                        OnCDRemoved();
                        break;
                }
            }
        }

        //Implentation
        public async void RipContents(string dest)
        {
            try
            {
                if (ReadToc())
                {
                    UnlockCD();
                    LockCD();
                    int firstTrack = Toc.FirstTrack;
                    int lastTrack = Toc.LastTrack;

                    var trackList = new List<TrackData>();

                    for (int trackNum = firstTrack; trackNum <= lastTrack; trackNum++)
                    {
                        //var trackData = RipTrack(trackNum);
                        trackList.Add(RipTrack(trackNum));
                    }

                    var options = new ParallelOptions()
                    {
                        MaxDegreeOfParallelism = trackList.Count() / 2
                    };

                    WaveFormat format = new WaveFormat();

                    await Parallel.ForEachAsync(trackList, options, async (trackInstance, ct) =>
                    {
                        try
                        {
                            //await File.WriteAllBytesAsync(Path.Combine(dest, trackInstance.Track), trackInstance.Data, ct);
                            using (WaveFileWriter writer = new WaveFileWriter(Path.Combine(dest, trackInstance.Track), format))
                            {
                                writer.Write(trackInstance.Data, 0, trackInstance.DataSize);
                            }
                        }
                        catch (Exception ex)
                        {
                            throw new IOException(ex.Message, ex);
                        }
                    });

                    trackList.Clear();
                    UnlockCD();
                }
            }
            catch (Exception ex)
            {
                UnlockCD();
                OdnoException.CriticalException(new OdnoException(ex),
                    "Could not write Disc contents to WAV file. Cause: " + ex.Message,
                    "CDDrive.RipContents");
            }
            
            this.Dispose();
        }

        ~CDDrive() {
            Dispose();
        }

        public void Dispose()
        {
            Close();
            GC.SuppressFinalize(this);
        }
    }
}
