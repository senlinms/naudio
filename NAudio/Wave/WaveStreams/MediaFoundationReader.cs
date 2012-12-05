﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.MediaFoundation;

namespace NAudio.Wave
{
    /// <summary>
    /// Class for reading any file that Media Foundation can play
    /// Will only work in Windows Vista and above
    /// Automatically converts to PCM
    /// If it is a video file with multiple audio streams, it will pick out the first audio stream
    /// </summary>
    public class MediaFoundationReader : WaveStream
    {
        private readonly WaveFormat waveFormat;
        private readonly long length;
        private readonly MediaFoundationReaderSettings settings;
        private readonly string file;
        private IMFSourceReader pReader;

        private long position;

        /// <summary>
        /// Allows customisation of this reader class
        /// </summary>
        public class MediaFoundationReaderSettings
        {
            /// <summary>
            /// Sets up the default settings for MediaFoundationReader
            /// </summary>
            public MediaFoundationReaderSettings()
            {
                RepositionInRead = true;
            }

            /// <summary>
            /// Allows us to request IEEE float output (n.b. no guarantee this will be accepted)
            /// </summary>
            public bool RequestFloatOutput { get; set; }
            /// <summary>
            /// If true, the reader object created in the constructor is used in Read
            /// Should only be set to true if you are working entirely on an STA thread, or 
            /// entirely with MTA threads.
            /// </summary>
            public bool SingleReaderObject { get; set; }
            /// <summary>
            /// If true, the reposition does not happen immediately, but waits until the
            /// next call to read to be processed.
            /// </summary>
            public bool RepositionInRead { get; set; }
        }


        /// <summary>
        /// Creates a new MediaFoundationReader based on the supplied file
        /// </summary>
        /// <param name="file">Filename</param>
        public MediaFoundationReader(string file)
            : this(file, new MediaFoundationReaderSettings())
        {
        }


        /// <summary>
        /// Creates a new MediaFoundationReader based on the supplied file
        /// </summary>
        /// <param name="file">Filename</param>
        /// <param name="settings">Advanced settings</param>
        public MediaFoundationReader(string file, MediaFoundationReaderSettings settings)
        {
            MediaFoundationApi.Startup();
            this.settings = settings;
            this.file = file;
            var reader = CreateReader(settings);

            /* IMFMediaType currentMediaType;
            reader.GetCurrentMediaType(MediaFoundationInterop.MF_SOURCE_READER_FIRST_AUDIO_STREAM, out currentMediaType);
            var current = new MediaType(currentMediaType);
            IMFMediaType nativeMediaType;
            reader.GetNativeMediaType(MediaFoundationInterop.MF_SOURCE_READER_FIRST_AUDIO_STREAM, 0, out nativeMediaType);
            var native = new MediaType(nativeMediaType);*/

            // now let's find out what we actually got
            IMFMediaType uncompressedMediaType;
            reader.GetCurrentMediaType(MediaFoundationInterop.MF_SOURCE_READER_FIRST_AUDIO_STREAM, out uncompressedMediaType);

            // Two ways to query it, first is to ask for properties (second is to convert into WaveFormatEx using MFCreateWaveFormatExFromMFMediaType)
            var outputMediaType = new MediaType(uncompressedMediaType);
            Guid actualMajorType = outputMediaType.MajorType;
            Debug.Assert(actualMajorType == MediaTypes.MFMediaType_Audio);
            Guid audioSubType = outputMediaType.SubType;
            int channels = outputMediaType.ChannelCount;
            int bits = outputMediaType.BitsPerSample;
            int sampleRate = outputMediaType.SampleRate;

            waveFormat = audioSubType == AudioSubtypes.MFAudioFormat_PCM
                             ? new WaveFormat(sampleRate, bits, channels)
                             : WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);

            reader.SetStreamSelection(MediaFoundationInterop.MF_SOURCE_READER_FIRST_AUDIO_STREAM, true);
            length = GetLength(reader);

            if (settings.SingleReaderObject)
            {
                pReader = reader;
            }
        }

        /// <summary>
        /// Creates the reader (overridable by )
        /// </summary>
        protected virtual IMFSourceReader CreateReader(MediaFoundationReaderSettings settings)
        {
            var uri = new Uri(file);
            IMFSourceReader reader;
            MediaFoundationInterop.MFCreateSourceReaderFromURL(uri.AbsoluteUri, null, out reader);
            reader.SetStreamSelection(MediaFoundationInterop.MF_SOURCE_READER_ALL_STREAMS, false);
            reader.SetStreamSelection(MediaFoundationInterop.MF_SOURCE_READER_FIRST_AUDIO_STREAM, true);

            // Create a partial media type indicating that we want uncompressed PCM audio

            var partialMediaType = new MediaType();
            partialMediaType.MajorType = MediaTypes.MFMediaType_Audio;
            partialMediaType.SubType = settings.RequestFloatOutput ? AudioSubtypes.MFAudioFormat_Float : AudioSubtypes.MFAudioFormat_PCM;

            // set the media type
            // can return MF_E_INVALIDMEDIATYPE if not supported
            reader.SetCurrentMediaType(MediaFoundationInterop.MF_SOURCE_READER_FIRST_AUDIO_STREAM, IntPtr.Zero, partialMediaType.MediaFoundationObject);
            return reader;
        }

        private long GetLength(IMFSourceReader reader)
        {
            PropVariant variant;
            // http://msdn.microsoft.com/en-gb/library/windows/desktop/dd389281%28v=vs.85%29.aspx#getting_file_duration
            reader.GetPresentationAttribute(MediaFoundationInterop.MF_SOURCE_READER_MEDIASOURCE,
                MediaFoundationAttributes.MF_PD_DURATION, out variant);
            var lengthInBytes = (((long)variant.Value) * waveFormat.AverageBytesPerSecond) / 10000000L;
            variant.Clear();
            return lengthInBytes;
        }

        private byte[] decoderOutputBuffer;
        private int decoderOutputOffset;
        private int decoderOutputCount;

        private void EnsureBuffer(int bytesRequired)
        {
            if (decoderOutputBuffer == null || decoderOutputBuffer.Length < bytesRequired)
            {
                decoderOutputBuffer = new byte[bytesRequired];
            }
        }

        /// <summary>
        /// Reads from this wave stream
        /// </summary>
        /// <param name="buffer">Buffer to read into</param>
        /// <param name="offset">Offset in buffer</param>
        /// <param name="count">Bytes required</param>
        /// <returns>Number of bytes read; 0 indicates end of stream</returns>
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (pReader == null)
            {
                pReader = CreateReader(settings);
            }
            if (repositionTo != -1)
            {
                Reposition(repositionTo);
            }

            int bytesWritten = 0;
            // read in any leftovers from last time
            if (decoderOutputCount > 0)
            {
                bytesWritten += ReadFromDecoderBuffer(buffer, offset, count - bytesWritten);
            }

            while (bytesWritten < count)
            {
                IMFSample pSample;
                int dwFlags;
                ulong timestamp;
                int actualStreamIndex;
                pReader.ReadSample(MediaFoundationInterop.MF_SOURCE_READER_FIRST_AUDIO_STREAM, 0, out actualStreamIndex, out dwFlags, out timestamp, out pSample);
                if (dwFlags != 0)
                {
                    // reached the end of the stream or media type changed
                    break;
                }/*
                if (dwFlags & MF_SOURCE_READERF_CURRENTMEDIATYPECHANGED)
                {
                    printf("Type change - not supported by WAVE file format.\n");
                    break;
                }
                if (dwFlags & MF_SOURCE_READERF_ENDOFSTREAM)
                {
                    printf("End of input file.\n");
                    break;
                }*/

                IMFMediaBuffer pBuffer;
                pSample.ConvertToContiguousBuffer(out pBuffer);
                IntPtr pAudioData = IntPtr.Zero;
                int cbBuffer;
                int pcbMaxLength;
                pBuffer.Lock(out pAudioData, out pcbMaxLength, out cbBuffer);
                EnsureBuffer(cbBuffer);
                Marshal.Copy(pAudioData, decoderOutputBuffer, 0, cbBuffer);
                decoderOutputOffset = 0;
                decoderOutputCount = cbBuffer;

                bytesWritten += ReadFromDecoderBuffer(buffer, offset + bytesWritten, count - bytesWritten);


                pBuffer.Unlock();
                Marshal.ReleaseComObject(pBuffer);
                Marshal.ReleaseComObject(pSample);
            }
            position += bytesWritten;
            return bytesWritten;
        }

        private int ReadFromDecoderBuffer(byte[] buffer, int offset, int needed)
        {
            int bytesFromDecoderOutput = Math.Min(needed, decoderOutputCount);
            Array.Copy(decoderOutputBuffer, decoderOutputOffset, buffer, offset, bytesFromDecoderOutput);
            decoderOutputOffset += bytesFromDecoderOutput;
            decoderOutputCount -= bytesFromDecoderOutput;
            if (decoderOutputCount == 0)
            {
                decoderOutputOffset = 0;
            }
            return bytesFromDecoderOutput;
        }

        /// <summary>
        /// WaveFormat of this stream (n.b. this is after converting to PCM)
        /// </summary>
        public override WaveFormat WaveFormat
        {
            get { return waveFormat; }
        }

        /// <summary>
        /// The bytesRequired of this stream in bytes (n.b may not be accurate)
        /// </summary>
        public override long Length
        {
            get
            {
                return length;
            }
        }

        /// <summary>
        /// Current position within this stream
        /// </summary>
        public override long Position
        {
            get { return position; }
            set
            {
                if (value < 0)
                    throw new ArgumentOutOfRangeException("Position cannot be less than 0");
                if (settings.RepositionInRead)
                {
                    repositionTo = value;
                    position = value; // for gui apps, make it look like we have alread processed the reposition
                }
                else
                {
                    Reposition(value);
                }
            }
        }

        private long repositionTo = -1;

        private void Reposition(long desiredPosition)
        {
            long nsPosition = (10000000L * repositionTo) / waveFormat.AverageBytesPerSecond;
            var pv = PropVariant.FromLong(nsPosition);
            // should pass in a variant of type VT_I8 which is a long containing time in 100nanosecond units
            pReader.SetCurrentPosition(Guid.Empty, ref pv);
            decoderOutputCount = 0;
            decoderOutputOffset = 0;
            position = desiredPosition;
            repositionTo = -1;// clear the flag
        }

        /// <summary>
        /// Cleans up after finishing with this reader
        /// </summary>
        /// <param name="disposing">true if called from Dispose</param>
        protected override void Dispose(bool disposing)
        {
            if (pReader != null)
            {
                Marshal.ReleaseComObject(pReader);
                pReader = null;
            }
            base.Dispose(disposing);
        }
    }
}