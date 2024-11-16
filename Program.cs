using System;
using System.Diagnostics;
using System.IO;
using System.Media;
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
            Play,
            Buffering,
            Pause
        }

        private static BufferedWaveProvider bufferedWaveProvider;
        private static IWavePlayer waveOut;
        private static volatile StreamingPlaybackState playbackState;
        private static volatile bool fullyDownloaded;
        private static HttpClient httpClient;
        private static VolumeWaveProvider16 volumeProvider;
        private static bool Alarma = false;

        public static void Main(string[] args)
        {
            try
            {
                playbackState = StreamingPlaybackState.Buffering;
                ThreadPool.QueueUserWorkItem(StreamMp3, "https://stream.statsradio.com:8050/stream");

                //Press 'P' to pause, 'R' to resume, 'S' to stop.";
                while (true)
                {
                    //Console.WriteLine("Enter command\n\np: pause\nr: resume\ns: stop\v: Set volume in %\nstate:Get stream play status):");
                    var key = Console.ReadLine()?.Trim().ToLower();
                    if (waveOut != null)
                    {
                        switch (key)
                        {
                            case "a":
                                Alarma = true;
                                if (playbackState == StreamingPlaybackState.Play)
                                {
                                    waveOut.Pause();
                                }
                                Properties.Resources.alert.Position = 0; // Assurez-vous que la position du flux est au début

                                using (WaveFileReader reader = new WaveFileReader(Properties.Resources.alert))
                                using (WaveOutEvent outputDevice = new WaveOutEvent())
                                {
                                    outputDevice.PlaybackStopped += (s, e) =>
                                    {
                                        if (Alarma)
                                        {
                                            reader.Position = 0; // Réinitialisez la position pour rejouer
                                            outputDevice.Play(); // Rejoue l'audio
                                        }
                                    };

                                    outputDevice.Init(reader);
                                    outputDevice.Play();

                                    var v = Console.ReadLine();
                                    while (v.Contains("gv") || v.Contains("state"))
                                    {
                                        v = Console.ReadLine();
                                    }
                                    
                                     Alarma = false;
                                }
                                break;
                            case "p":
                                if (!Alarma)
                                {

                                    if (playbackState == StreamingPlaybackState.Play)
                                    {
                                        waveOut.Pause();
                                        playbackState = StreamingPlaybackState.Pause;
                                    }
                                    else
                                    {
                                        waveOut.Play();
                                        playbackState = StreamingPlaybackState.Play;
                                    }
                                }
                                Console.WriteLine(playbackState);
                                break;
                            case "v":
                                var volpercent = Console.ReadLine()?.Trim().ToLower();
                                if (float.TryParse(volpercent, out var Volume))
                                {
                                    volumeProvider.Volume = (Volume / 50);
                                    Console.WriteLine(volumeProvider.Volume * 50);
                                }
                                else
                                {
                                    Console.WriteLine("50");
                                }
                                break;
                            case "gv":
                                Console.WriteLine(volumeProvider.Volume * 50);
                                break;
                            case "state":
                                Console.WriteLine(playbackState);
                                break;
                        }

                        if (key == "s")
                        {
                            if (!Alarma)
                            {
                                playbackState = StreamingPlaybackState.Stopped;
                                waveOut?.Stop();
                                waveOut?.Dispose();
                                waveOut = null;
                            }
                            break;
                        }
                    }
                }
            }
            catch(Exception e) { Console.WriteLine(e); File.AppendAllText($"{AppDomain.CurrentDomain.BaseDirectory}\\CIBM.log", $"{DateTime.Now}\n{e}"); }
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
                        { Thread.Sleep(500); }
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

        private static bool IsBufferNearlyFull => bufferedWaveProvider != null && bufferedWaveProvider.BufferLength - bufferedWaveProvider.BufferedBytes < bufferedWaveProvider.WaveFormat.AverageBytesPerSecond / 4;

        private static void CreateWaveOut()
        {
            if (waveOut == null && bufferedWaveProvider != null)
            {
                waveOut = new WaveOut(); // You can also try WaveOutEvent if you have issues
                volumeProvider = new VolumeWaveProvider16(bufferedWaveProvider);
                volumeProvider.Volume = 0.5f; // Ensure volume is at an audible level
                waveOut.Init(volumeProvider);
                waveOut.Play();
                playbackState = StreamingPlaybackState.Play;
            }
        }

    }
}
