using System;
using System.Diagnostics;
using System.IO;
using System.Media;
using System.Net.Http;
using System.Threading;
using CIBM.Server.Properties;
using NAudio.Wave;

namespace CIBM
{
    public class Player
    {
        public enum StreamingPlaybackState
        {
            Stopped,
            Play,
            Buffering,
            Pause
        }

        private BufferedWaveProvider bufferedWaveProvider;
        private IWavePlayer waveOut;
        private bool fullyDownloaded;
        private HttpClient httpClient;
        private bool Alarma = false;
        private VolumeWaveProvider16 VolumeProvider;
        public bool IsVolumeSetReady
        {
            get
            {
                if (VolumeProvider != null)
                {
                    return true;
                }
                return false;
            }
        }
        public float Volume
        {
            get => (VolumeProvider.Volume * 50);
            set => VolumeProvider.Volume = (value / 50);
        }

        public StreamingPlaybackState Status
        {
            get; private set;
        }


        public Player()
        {
            Status = StreamingPlaybackState.Buffering;
            ThreadPool.QueueUserWorkItem(StreamMp3, "https://stream.statsradio.com:8050/stream");
        }


        public void PlayPause()
        {
            if (!Alarma)
            {

                if (Status == StreamingPlaybackState.Play)
                {
                    waveOut.Pause();
                    Status = StreamingPlaybackState.Pause;
                }
                else
                {
                    waveOut.Play();
                    Status = StreamingPlaybackState.Play;
                }
            }
            else
            {
                Alarma = false;
            }
        }
        public void Stop()
        {
            Status = StreamingPlaybackState.Stopped;
            waveOut?.Stop();
            waveOut?.Dispose();
            waveOut = null;
            bufferedWaveProvider = null;
            httpClient = null;
        }
        public void StartAlarma()
        {
            Alarma = true;
            if (Status == StreamingPlaybackState.Play)
            {
                waveOut.Pause();
            }
            Resources.alert.Position = 0; // Assurez-vous que la position du flux est au début

            using (WaveFileReader reader = new WaveFileReader(Resources.alert))
            using (WaveOutEvent outputDevice = new WaveOutEvent())
            {
                outputDevice.PlaybackStopped += (s, e) =>
                {
                    if (Alarma && bufferedWaveProvider != null && Status != StreamingPlaybackState.Stopped)
                    {
                        WaveFileReader Nreader = new WaveFileReader(Resources.alert);
                        reader.Position = 0; // Réinitialisez la position pour rejouer
                        outputDevice.Init(Nreader);
                        outputDevice.Play(); // Rejoue l'audio
                    }
                };

                outputDevice.Init(reader);
                outputDevice.Play();
            }
        }

        private void StreamMp3(object state)
        {
            fullyDownloaded = false;
            var url = (string)state;
            if (httpClient == null) httpClient = new HttpClient();
            Stream stream;
            try
            {
                stream = httpClient.GetStreamAsync(url).Result;
            }
            catch (Exception e)
            {
                Console.WriteLine("Error: " + e.Message);
                return;
            }

            var buffer = new byte[16384 * 4];
            IMp3FrameDecompressor decompressor = null;
            try
            {
                using (stream)
                {
                    var readFullyStream = new ReadFullyStream(stream);
                    do
                    {
                        if (IsBufferNearlyFull)
                        {
                            Thread.Sleep(500);
                        }
                        else
                        {
                            Mp3Frame frame;
                            try
                            {
                                frame = Mp3Frame.LoadFromStream(readFullyStream);
                            }
                            catch (EndOfStreamException)
                            {
                                fullyDownloaded = true;
                                break;
                            }
                            if (frame == null) break;

                            if (decompressor == null)
                            {
                                decompressor = CreateFrameDecompressor(frame);
                                bufferedWaveProvider = new BufferedWaveProvider(decompressor.OutputFormat);
                                bufferedWaveProvider.BufferDuration = TimeSpan.FromSeconds(20);
                            }

                            if (bufferedWaveProvider != null)
                            {
                                var decompressed = decompressor.DecompressFrame(frame, buffer, 0);
                                bufferedWaveProvider.AddSamples(buffer, 0, decompressed);

                                if (bufferedWaveProvider != null && waveOut == null)
                                {
                                    CreateWaveOut();
                                }
                            }
                        }

                    } while (Status != StreamingPlaybackState.Stopped);

                    decompressor.Dispose();
                }
            }
            finally
            {
                decompressor?.Dispose();
            }
        }
        private static IMp3FrameDecompressor CreateFrameDecompressor(Mp3Frame frame)
        {
            WaveFormat waveFormat = new Mp3WaveFormat(frame.SampleRate, frame.ChannelMode == ChannelMode.Mono ? 1 : 2,
                frame.FrameLength, frame.BitRate);
            return new AcmMp3FrameDecompressor(waveFormat);
        }

        private bool IsBufferNearlyFull => bufferedWaveProvider != null && bufferedWaveProvider.BufferLength - bufferedWaveProvider.BufferedBytes < bufferedWaveProvider.WaveFormat.AverageBytesPerSecond / 4;

        private void CreateWaveOut()
        {
            if (waveOut == null && bufferedWaveProvider != null)
            {
                waveOut = new WaveOut(); // You can also try WaveOutEvent if you have issues
                VolumeProvider = new VolumeWaveProvider16(bufferedWaveProvider);
                VolumeProvider.Volume = 0.5f; // Ensure volume is at an audible level
                waveOut.Init(VolumeProvider);
                waveOut.Play();
                Status = StreamingPlaybackState.Play;
            }
        }

    }
}
