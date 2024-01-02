using System;
using System.IO;
using System.Threading;
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

        static void Main(string[] args)
        {
            Console.WriteLine($"Start Bots {DateTime.Now}");

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

                client = new TelegramBotClient(token);

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