using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;

public class Example() {

	protected struct Track {
    		public string Name { get; set; }
    		public byte[] Data { get; set; }
    		public int DataSize { get; set; }
	}

	protected Track RipTrack(int trackNum)
        {
            try
            {
                string track = $"{trackNum}_Track.wav";

                byte[] dummy = null;
                uint totalSize = 0;

                ReadTrack(trackNum, dummy, ref totalSize, 0, 0, null);

                if (totalSize == 0) {
                    throw new IOException($"Failed @ Track {trackNum} could not be sized (empty or invalid).");
                }

                byte[] buffer = new byte[totalSize];
                int offset = 0;

                int result = ReadTrack(
                    trackNum,
                    (sender, e) =>
                    {
                        int chunkSize = (int)e.DataSize;

                        if (offset + chunkSize > buffer.Length) {
                            throw new ArgumentException("buffer is too small.");
                        }

                        Buffer.BlockCopy(e.Data, 0, buffer, offset, chunkSize);
                        offset += chunkSize;
                    },
                    (sender, e) =>
                    {
                        // e has Bytes2Read and BytesRead (and CancelRead) on it.
                        // e.g. report progress, allow cancellation:
                        // e.CancelRead = userCancelled;

                    });

                if (result < 0)
                {
                    throw new IOException("Track: " + trackNum + " could not be read...");
                }

                if (offset < buffer.Length) {
                    Array.Resize(ref buffer, offset);
                }

                return new Track { Name = track, Data = buffer, DataSize = offset };

            }
            catch (Exception e)
            {
                // TODO: log e
                throw;
            }
        }

}

