using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CIBM.Server.Properties;

namespace CIBM.Server
{
    class Program
    {
        private static Player CibmPlayer = new Player();
        private static HttpListenerResponse LastResonse;
        private static string LocalUrl = $"http://{GetLocalIPAddress()}:80/";
        static void Main(string[] args)
        {
            // Create an HTTP listener
            HttpListener listener = new HttpListener();
            listener.Prefixes.Add(LocalUrl);
            listener.Start();
            Console.WriteLine($"Listening for requests at {LocalUrl}");

            // Handle requests asynchronously
            Task.Run(() => HandleRequests(listener));

            Console.ReadLine();
            listener.Stop();
        }

        private static async Task HandleRequests(HttpListener listener)
        {
            try
            {
                Thread.Sleep(3000);
                CibmPlayer.Volume = Settings.Default.LastVolume;
                while (listener.IsListening)
                {
                    if (CibmPlayer != null && CibmPlayer.IsVolumeSetReady)
                    {

                        var context = await listener.GetContextAsync();
                        var request = context.Request;
                        LastResonse = context.Response;

                        Log($"{request.Url.AbsolutePath}{request.Url.Query}", "INFO");

                        // Basic routing
                        switch (request.Url.AbsolutePath.ToLower())
                        {
                            case "/service-worker.js":
                                Log("Server starting...", "INFO");
                                break;
                            case "/sync":
                            case "/s":
                                CibmPlayer.Stop();
                                CibmPlayer = null;
                                CibmPlayer = new Player();
                                Thread.Sleep(3000);
                                CibmPlayer.Volume = Settings.Default.LastVolume;
                                Return(CibmPlayer.Status.ToString());
                                break;
                            case "/playpause":
                            case "/pp":
                                CibmPlayer.PlayPause();
                                Return(CibmPlayer.Status.ToString());
                                break;
                            case "/getstate":
                            case "/gs":
                                Return(CibmPlayer.Status.ToString());
                                break;
                            case "/setvolume":
                            case "/sv":
                                if (CibmPlayer.IsVolumeSetReady)
                                {
                                    var Volume = request.Url.Query.Split(new string[] { "?percent=" }, StringSplitOptions.None).Last();
                                    if (float.TryParse(Volume, out var ParseVolume))
                                    {
                                        Settings.Default.LastVolume = ParseVolume;
                                        Settings.Default.Save();
                                        CibmPlayer.Volume = ParseVolume;
                                        Return(CibmPlayer.Volume.ToString());
                                    }
                                }
                                break;
                            case "/getvolume":
                            case "/gv":
                                Return(CibmPlayer.Volume.ToString());
                                break;
                            case "/alarma":
                            case "/a":
                                CibmPlayer.StartAlarma();
                                Return("Alarma is ON");
                                break;
                            default:
                                Log($"User request page that do not exist", "WARNING");
                                Return("404 - Not Found");
                                break;
                        }
                    }
                }
            }
            catch (Exception e) { Log(e, "ERROR"); }

        }
        private static void Log(object Message, string Level)
        {
            var Msg = $"{Level}: \n{Message} | {DateTime.Now}\n";
            Console.WriteLine(Msg);
            File.AppendAllText($@"{AppDomain.CurrentDomain.BaseDirectory}\CIBM.log", Msg);
        }
        private static async void Return(string Message)
        {
            Log(Message, "INFO");
            LastResonse.StatusCode = 200;
            byte[] buffer = Encoding.UTF8.GetBytes(Message);
            LastResonse.ContentLength64 = buffer.Length;
            await LastResonse.OutputStream.WriteAsync(buffer, 0, buffer.Length);
        }
        static string GetLocalIPAddress()
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                {
                    return ip.ToString();
                }
            }
            throw new Exception("No network adapters with an IPv4 address in the system!");
        }
    }

}
