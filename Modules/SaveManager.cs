using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace Chino_chan.Modules
{
    public static class SaveManager
    {
        public static readonly string DataFolder = "Data";
        public static readonly string BackupFolder = Path.Combine(DataFolder, "Backup");
        static readonly List<string> Saving = new List<string>();

        public static T LoadSettings<T>(string fileName)
        {
            string originalFile = Path.Combine(DataFolder, $"{fileName}.json");
            string backupFile = Path.Combine(BackupFolder, $"{fileName}.backup.json");
            T data = default;

            try
            {
                data = JsonConvert.DeserializeObject<T>(File.ReadAllText(originalFile));
                Logger.Log(LogType.Settings, ConsoleColor.Green, null, $"{ fileName }.json was successfully loaded!");
            }
            catch (Exception e)
            {
                Logger.Log(LogType.Settings, ConsoleColor.Red, "Error", $"There was an error loading { fileName }.json! Loading backup file...");
                Logger.Log(LogType.Settings, ConsoleColor.Red, "Error", JsonConvert.SerializeObject(e, Formatting.Indented));
                if (File.Exists(backupFile))
                {
                    try
                    {
                        data = JsonConvert.DeserializeObject<T>(File.ReadAllText(backupFile));

                        Logger.Log(LogType.Settings, ConsoleColor.Green, null, "Backup file successfully loaded! Replacing settings with backup...");

                        if (File.Exists(originalFile))
                            File.Delete(originalFile);

                        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(originalFile)));
                        File.Copy(backupFile, originalFile);
                    }
                    catch
                    {
                        Logger.Log(LogType.Settings, ConsoleColor.Red, "Error", "Backup file could not be loaded!");
                    }
                }
            
            }

            return data;
        }
        public static void SaveData(string fileName, object data)
        {
            if (Saving.Contains(fileName))
                return;

            Saving.Add(fileName);

            string originalFile = Path.Combine(DataFolder, $"{fileName}.json");
            string backupFile = Path.Combine(BackupFolder, $"{fileName}.backup.json");

            if (File.Exists(originalFile))
            {
                if (File.Exists(backupFile))
                    File.Delete(backupFile);

                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(backupFile)));
                File.Copy(originalFile, backupFile);

                File.Delete(originalFile);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(originalFile)));
            File.WriteAllText(originalFile, JsonConvert.SerializeObject(data, Formatting.Indented));

            Saving.Remove(fileName);
        }
    }
}