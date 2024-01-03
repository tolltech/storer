using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Tolltech.TelegramCore;
using Tolltech.CoreLib;
using Tolltech.Storer;

namespace Tolltech.BayanMeter
{
    class Program
    {
        class AppSettings
        {
            public string ConnectionString { get; set; }
            public BotSettings[] BotSettings { get; set; }
        }
        
        class BotSettings
        {
            public string Token { get; set; }
            public string BotName { get; set; }
            public string CustomSettings { get; set; }
        }

        private static TelegramBotClient client;
        
        public static HttpClient CreateWorkaroundClient()
        {
            var handler = new SocketsHttpHandler
            {
                ConnectCallback = IPv4ConnectAsync
            };

            return new HttpClient(handler);

            static async ValueTask<Stream> IPv4ConnectAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
            {
                // By default, we create dual-mode sockets:
                // Socket socket = new Socket(SocketType.Stream, ProtocolType.Tcp);

                Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                socket.NoDelay = true;

                try
                {
                    await socket.ConnectAsync(context.DnsEndPoint, cancellationToken).ConfigureAwait(false);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            }
        }

        static void Main(string[] args)
        {
            Console.WriteLine($"Start Bots {DateTime.Now}");

            var wc = CreateWorkaroundClient();

            //wc.DefaultRequestVersion = HttpVersion.Version10;

            var argsFileName = "args.txt";
            var botSettingsStr = args.Length > 0 ? args[0] :
                File.Exists(argsFileName) ? File.ReadAllText(argsFileName) : string.Empty;

            var appSettings = JsonConvert.DeserializeObject<AppSettings>(botSettingsStr);

            var botSettings = appSettings?.BotSettings ?? Array.Empty<BotSettings>();
            Console.WriteLine($"Read {botSettings.Length} bot settings");

            using var cts = new CancellationTokenSource();

            foreach (var botSetting in botSettings)
            {
                var token = botSetting.Token;
             
                Console.WriteLine($"Start bot {token}");

                client = new TelegramBotClient(new TelegramBotClientOptions(token, "http://localhost:8081"), wc);

                var receiverOptions = new ReceiverOptions
                {
                    AllowedUpdates = { } // receive all update types
                };

                var settings = new CustomSettings
                {
                    Raw = botSetting.CustomSettings ?? string.Empty
                };
                
                var botDaemon = new ServerStorerBotDaemon(new TelegramClient(client), settings);
                client.StartReceiving(
                    botDaemon.HandleUpdateAsync,
                    botDaemon.HandleErrorAsync,
                    receiverOptions,
                    cancellationToken: cts.Token);

                var me = client.GetMeAsync(cts.Token).GetAwaiter().GetResult();

                Console.WriteLine($"Start listening for @{me.Username}");
            }

            Console.ReadLine();

            cts.Cancel();

            Console.WriteLine($"End Bots {DateTime.Now}");
        }
    }
}