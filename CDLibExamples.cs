using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;

/* written w/love in vim */

public class CDLibExamples() {

	protected struct Track {
    		public string Name { get; set; }
    		public byte[] Data { get; set; }
    		public int DataSize { get; set; }
	}

    /// <summary>
    ///  Method for consuming Byte Data from the 'ReadTrack()' in the CDDrive.cs API.
    ///  'ReadTrack()' is called with dummy vars to get the track size. This helps build the buffer needed to store data in an efficient way.
    ///  A proper buffer and an offset is used to store data once totalSize is determined.
    ///  The CDDatatReadEventHandler is a callback event used to store Data from CD and update the buffer as Sectors are read.
    ///  CDReadProgressHandler is another callback event for interacting with Bytes2Read & BytesRead. You can use this to update a progress bar.
    ///  Data is returned as struct Track. How you return/handle the data is up to you. I chose to create a struct over a class.
    ///  Ripping this way is defensive & very safe, but memory will scale with tracks read (1x usage per track read).
    ///  Refer to the 'RipTrack' method below for an async implementation that uses contants memory.
    /// </summary>
    /// <param name="trackNum"></param>
    /// <returns>Track</returns>
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
                    {// this is the CDDataReadEventHandler
                        int chunkSize = (int)e.DataSize;

                        if (offset + chunkSize > buffer.Length) {
                            throw new ArgumentException("buffer is too small.");
                        }

                        Buffer.BlockCopy(e.Data, 0, buffer, offset, chunkSize);
                        offset += chunkSize;
                    },
                    (sender, e) =>
                    { //This is the CDReadProgressHandler
			            // do with this what you like 
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
                // Handle however.
                throw;
            }
    }

    /// <summary>
    /// Same as above, but data is written straight to file once it has been collected.
    /// In theory memory use would be constant as opposed to increasing in usage with every track.
    /// While memory is constant, if something goes wrong the process of writing a file can get interrupted.
    /// The method above is more defensive.
    /// This is how I would write an async method that writes bytes to file as it reads sectors.
    /// Feel free to do this anyway you like if you think there is a better way. I am not a C# dev.
    /// </summary>
    /// <param name="dest"></param>
    /// <param name="trackNum"></param>
    /// <param name="format"></param>
    protected async Task<bool> RipTrack(string dest, int trackNum)
    {
        try
        {
            string track = $"{trackNum}_Track.wav";

            using var fs = new FileStream(Path.Combine(dest, track), FileMode.Create,
                FileAccess.Write, FileShare.None, 1 << 16, FileOptions.SequentialScan);

            long bytesWritten = 0;

	    return await Task.Run(async () =>{
		long bytesWritten = 0;

		int res = ReadTrack(
			trackNum,
			(sender, e) =>
			{
				writer.Write(e.Data, 0, (int)e.DataSize); //write as program reads. needs to be in sync.
				bytesWritten += e.DataSize;
			},
			(sender, e) =>
			{
				/*optional: update progress */
			}
			if(res < 0 ){
				throw new IOException("..."); //or whatever exception
			}

			return res > 0;
		)
	    }
        }
	catch (ArgumentException e){
		throw; //handle however
	}
        catch (Exception e)
        {
            // Handle however.
            throw;
        }
    }
}

