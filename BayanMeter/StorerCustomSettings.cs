namespace Tolltech.BayanMeter
{
    public class StorerCustomSettings
    {
        public required string RootDir { get; set; }
        public string[] AllowedUsers { get; set; } = [];
        public string? PassKey { get; set; }
    }
}