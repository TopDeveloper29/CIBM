using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using NAudio.Wave;

namespace CIBM
{
    public class Program
    {
        enum StreamingPlaybackState
        {
            Stopped,
            Playing,
            Buffering,
            Paused
        }

        private static BufferedWaveProvider bufferedWaveProvider;
        private static IWavePlayer waveOut;
        private static volatile StreamingPlaybackState playbackState;
        private static volatile bool fullyDownloaded;
        private static HttpClient httpClient;
        private static VolumeWaveProvider16 volumeProvider;


        public static void Main(string[] args)
        {
            var url = "https://stream.statsradio.com:8050/stream";

            playbackState = StreamingPlaybackState.Stopped;
            StartStreaming(url);

            //Press 'P' to pause, 'R' to resume, 'S' to stop.";
            while (true)
            {
                Console.WriteLine("Enter command\n\np: pause\nr: resume\ns: stop\niv: Increase volume\ndv: Decrease volume):");
                var key = Console.ReadLine()?.Trim().ToLower();
                Console.Clear();
                switch (key.ToLower())
                {
                    case "p":
                        Pause();
                        break;
                    case "r":
                        Resume();
                        break;
                    case "iv":
                        volumeProvider.Volume += 0.15f;
                        break;
                    case "dv":
                        volumeProvider.Volume -= 0.15f;
                        break;
                }
                if (key == "s")
                {
                    break;
                }
            }
        }

        private static void StartStreaming(string url)
        {
            playbackState = StreamingPlaybackState.Buffering;
            ThreadPool.QueueUserWorkItem(StreamMp3, url);
        }

        private static void StreamMp3(object state)
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
                            Console.WriteLine("Buffer is full, waiting...");
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

                            int decompressed = decompressor.DecompressFrame(frame, buffer, 0);
                            bufferedWaveProvider.AddSamples(buffer, 0, decompressed);

                            if (bufferedWaveProvider != null && waveOut == null)
                            {
                                CreateWaveOut();
                            }
                        }

                    } while (playbackState != StreamingPlaybackState.Stopped);

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

        private static bool IsBufferNearlyFull => bufferedWaveProvider != null &&
                                                  bufferedWaveProvider.BufferLength - bufferedWaveProvider.BufferedBytes
                                                  < bufferedWaveProvider.WaveFormat.AverageBytesPerSecond / 4;

        private static void CreateWaveOut()
        {
            if (waveOut == null && bufferedWaveProvider != null)
            {
                waveOut = new WaveOut(); // You can also try WaveOutEvent if you have issues
                waveOut.PlaybackStopped += OnPlaybackStopped;
                volumeProvider = new VolumeWaveProvider16(bufferedWaveProvider);
                volumeProvider.Volume = 0.5f; // Ensure volume is at an audible level
                waveOut.Init(volumeProvider);
                waveOut.Play();
                playbackState = StreamingPlaybackState.Playing;
                Console.WriteLine("Started playing.");
            }
        }

        private static void OnPlaybackStopped(object sender, StoppedEventArgs e)
        {
            if (e.Exception != null)
            {
                Console.WriteLine($"Playback Error: {e.Exception.Message}");
            }
            else
            {
                Console.WriteLine("Playback Stopped.");
            }
        }

        private static void Pause()
        {
            if (waveOut != null)
            {
                waveOut.Pause();
                playbackState = StreamingPlaybackState.Paused;
                Console.WriteLine("Paused.");
            }
        }

        private static void Resume()
        {
            if (waveOut != null)
            {
                waveOut.Play();
                playbackState = StreamingPlaybackState.Playing;
                Console.WriteLine("Resumed.");
            }
        }

        private static void StopPlayback()
        {
            if (playbackState != StreamingPlaybackState.Stopped)
            {
                playbackState = StreamingPlaybackState.Stopped;
                waveOut?.Stop();
                waveOut?.Dispose();
                waveOut = null;
                Console.WriteLine("Stopped.");
            }
        }
    }
}
