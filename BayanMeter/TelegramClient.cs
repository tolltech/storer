using System.IO;
using Telegram.Bot;

namespace Tolltech.BayanMeter
{
    public class TelegramClient(TelegramBotClient client) : ITelegramClient
    {
        public byte[] GetFile(string fileId)
        {
            var file = client.GetFileAsync(fileId).GetAwaiter().GetResult();

            //for using without local server
            // using var stream = new MemoryStream();
            // client.DownloadFileAsync(file.FilePath, stream).GetAwaiter().GetResult();
            // stream.Seek(0, SeekOrigin.Begin);
            //
            // return stream.ReadToByteArray();

            return File.ReadAllBytes(file.FilePath!);
        }
    }
}