﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudioWpfDemo.ViewModel;

namespace NAudioWpfDemo.WasapiCaptureDemo
{
    internal class WasapiCaptureViewModel : ViewModelBase, IDisposable
    {
        private MMDevice selectedDevice;
        private int sampleRate;
        private int bitDepth;
        private int channelCount;
        private int sampleTypeIndex;
        private WasapiCapture capture;
        private WaveFileWriter writer;
        private string currentFileName;
        private string message;
        public RecordingsViewModel RecordingsViewModel { get; private set; }

        public DelegateCommand RecordCommand { get; private set; }
        public DelegateCommand StopCommand { get; private set; }

        public WasapiCaptureViewModel()
        {
            var enumerator = new MMDeviceEnumerator();
            CaptureDevices = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active).ToArray();
            var defaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);
            SelectedDevice = CaptureDevices.FirstOrDefault(c => c.ID == defaultDevice.ID);
            RecordCommand = new DelegateCommand(Record);
            StopCommand = new DelegateCommand(Stop) { IsEnabled = false };
            RecordingsViewModel = new RecordingsViewModel();
        }

        private void Stop()
        {
            if (capture != null)
            {
                capture.StopRecording();
            }
        }

        private void Record()
        {
            try
            {
                capture = new WasapiCapture(SelectedDevice);
                capture.WaveFormat =
                    SampleTypeIndex == 0 ? WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channelCount) :
                    new WaveFormat(sampleRate, bitDepth, channelCount);
                currentFileName = String.Format("NAudioDemo {0:yyy-MM-dd HH-mm-ss}.wav", DateTime.Now);
                capture.StartRecording();
                capture.RecordingStopped += OnRecordingStopped;
                capture.DataAvailable += CaptureOnDataAvailable;
                RecordCommand.IsEnabled = false;
                StopCommand.IsEnabled = true;
                Message = "Recording...";
            }
            catch (Exception e)
            {
                Message = e.Message;
            }
        }

        private void CaptureOnDataAvailable(object sender, WaveInEventArgs waveInEventArgs)
        {
            if (writer == null) 
                writer = new WaveFileWriter(Path.Combine(RecordingsViewModel.OutputFolder, currentFileName), capture.WaveFormat);

            writer.Write(waveInEventArgs.Buffer, 0, waveInEventArgs.BytesRecorded);
        }

        void OnRecordingStopped(object sender, StoppedEventArgs e)
        {
            writer.Dispose();
            writer = null;
            RecordingsViewModel.Recordings.Add(currentFileName);
            RecordingsViewModel.SelectedRecording = currentFileName;
            if (e.Exception == null)
                Message = "Recording Stopped";
            else
                Message = "Recording Error: " + e.Exception.Message;
            capture.Dispose();
            capture = null;
            RecordCommand.IsEnabled = true;
            StopCommand.IsEnabled = false;
        }

        public IEnumerable<MMDevice> CaptureDevices { get; private set; }

        public MMDevice SelectedDevice
        {
            get { return selectedDevice; }
            set
            {
                if (selectedDevice != value)
                {
                    selectedDevice = value;
                    OnPropertyChanged("SelectedDevice");
                    GetDefaultRecordingFormat(value);
                }
            }
        }

        private void GetDefaultRecordingFormat(MMDevice value)
        {
            using (var c = new WasapiCapture(value))
            {
                SampleRate = c.WaveFormat.SampleRate;
                BitDepth = c.WaveFormat.BitsPerSample;
                ChannelCount = c.WaveFormat.Channels;
                SampleTypeIndex = c.WaveFormat.Encoding == WaveFormatEncoding.IeeeFloat ? 0 : 1;
                Message = "";
            }
        }

        public int SampleRate
        {
            get
            {
                return sampleRate;
            }
            set
            {
                if (sampleRate != value)
                {
                    sampleRate = value;
                    OnPropertyChanged("SampleRate");
                }
            }
        }

        public int BitDepth
        {
            get
            {
                return bitDepth;
            }
            set
            {
                if (bitDepth != value)
                {
                    bitDepth = value;
                    OnPropertyChanged("BitDepth");
                }
            }
        }

        public int ChannelCount
        {
            get
            {
                return channelCount;
            }
            set
            {
                if (channelCount != value)
                {
                    channelCount = value;
                    OnPropertyChanged("ChannelCount");
                }
            }
        }

        public int SampleTypeIndex
        {
            get
            {
                return sampleTypeIndex;
            }
            set
            {
                if (sampleTypeIndex != value)
                {
                    sampleTypeIndex = value;
                    OnPropertyChanged("SampleTypeIndex");
                }
            }
        }

        public string Message
        {
            get
            {
                return message;
            }
            set
            {
                if (message != value)
                {
                    message = value;
                    OnPropertyChanged("Message");
                }
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
