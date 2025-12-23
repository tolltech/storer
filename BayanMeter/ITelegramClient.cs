namespace Tolltech.BayanMeter
{
    public interface ITelegramClient
    {
        byte[] GetFile(string fileId);
    }
}