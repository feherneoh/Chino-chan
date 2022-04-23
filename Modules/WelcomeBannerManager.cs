using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Chino_chan.Modules
{
    public class WelcomeBannerManager
    {
        public Dictionary<ulong, WelcomeBanner> Banners { get; private set; }

        private Bitmap DefaultFrame { get; set; }
        private Bitmap DefaultBackground { get; set; }
        private FontFamily DefaultFontFamily { get; set; }
        private float DefaultFontSize { get; set; }

        private Point DefaultAvatarPosition { get; set; }
        private Size DefaultAvatarSize { get; set; }
        private Point DefaultTextPosition { get; set; }
        private bool DefaultCircularAvatar { get; set; }

        private static readonly string ServerBannerPath = Path.Combine("Data", "Resources", "ServerBanners");
        private static readonly string DefaultFramePath = Path.Combine(ServerBannerPath, "default-frame.png");
        private static readonly string DefaultBackgroundPath = Path.Combine(ServerBannerPath, "default-background.png");
        private static readonly string DefaultFontPath = Path.Combine(ServerBannerPath, "default-font.ttf");

        public WelcomeBannerManager()
        {
            Directory.CreateDirectory(ServerBannerPath);
            if (!File.Exists(DefaultFramePath))
                File.WriteAllBytes(DefaultFramePath, WelcomeBannerResources.default_frame);
            if (!File.Exists(DefaultBackgroundPath))
                File.WriteAllBytes(DefaultBackgroundPath, WelcomeBannerResources.default_background);
            if (!File.Exists(DefaultFontPath))
                File.WriteAllBytes(DefaultFontPath, WelcomeBannerResources.default_font);

            Banners = new Dictionary<ulong, WelcomeBanner>();

            Logger.Log(LogType.WelcomeBanner, ConsoleColor.Magenta, "Load", "Loading default resources...");
            DefaultFrame = new Bitmap(System.Drawing.Image.FromFile(DefaultFramePath));
            DefaultBackground = new Bitmap(System.Drawing.Image.FromFile(DefaultBackgroundPath));

            PrivateFontCollection collection = new PrivateFontCollection();
            collection.AddFontFile(DefaultFontPath); // HAN_NOM_B
            DefaultFontFamily = collection.Families[0];
            DefaultFontSize = 21;

            DefaultAvatarPosition = new Point(23, 72);
            DefaultAvatarSize = new Size(187, 187);
            DefaultTextPosition = new Point(200, DefaultFrame.Height - 31);
            DefaultCircularAvatar = true;
            Logger.Log(LogType.WelcomeBanner, ConsoleColor.Magenta, "Load", "Default resources loaded!");

            Logger.Log(LogType.WelcomeBanner, ConsoleColor.Magenta, "Load", "Loading Server configurations...");
            IEnumerable<string> serverFolders = Directory.EnumerateDirectories(ServerBannerPath);
            foreach (string folder in serverFolders)
            {
                IEnumerable<string> jsonFiles = Directory.EnumerateFiles(folder, "*.json");
                foreach (string file in jsonFiles)
                {
                    WelcomeBanner banner = JsonConvert.DeserializeObject<WelcomeBanner>(File.ReadAllText(file));
                    bool successful = banner.Load(DefaultFrame, DefaultBackground, DefaultFontFamily, DefaultFontSize, DefaultAvatarPosition, DefaultAvatarSize, DefaultTextPosition, DefaultCircularAvatar, collection);

                    if (!successful)
                    {
                        Logger.Log(LogType.WelcomeBanner, ConsoleColor.Red, "Error", $"Failed to load { file }, using default configuration!");
                    }

                    Banners.Add(banner.GuildId, banner);
                }
            }
            Logger.Log(LogType.WelcomeBanner, ConsoleColor.Magenta, "Load", $"{ Banners.Count } banners loaded!");
        }
    }
}
