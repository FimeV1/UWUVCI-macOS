using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CNUSPACKER.crypto;
using CNUSPACKER.packaging;
using CNUSPACKER.utils;
using UWUVCI_AIO_WPF.Helpers;

namespace UWUVCI.Core.Engine
{
    /// <summary>
    /// Packs a code/content/meta folder into an installable WUP title with the CNUSPACKER library.
    /// This is CNUSPACKER.Program.Main without the parts that only work on Windows: Main looks for app.xml through a
    /// hard-coded backslash path and calls Environment.Exit on bad input, which would take the whole app down.
    /// </summary>
    public static class NusPacker
    {
        private const string DefaultEncryptionKey = "13371337133713371337133713371337";

        public static void Pack(string inputDirectory, string outputDirectory, string commonKey)
        {
            foreach (string part in new[] { "code", "content", "meta" })
                if (!Directory.Exists(Path.Combine(inputDirectory, part)))
                    throw new DirectoryNotFoundException($"The injected title is missing its {part} folder.");

            if (string.IsNullOrEmpty(commonKey) || commonKey.Length != 32)
                throw new InvalidOperationException("The Wii U Common Key is not set. Enter it in Settings before packing.");

            // The library exits the process when it meets an empty folder.
            string empty = Directory.GetDirectories(inputDirectory, "*", SearchOption.AllDirectories)
                .FirstOrDefault(d => !Directory.EnumerateFileSystemEntries(d).Any());
            if (empty != null)
                File.WriteAllText(Path.Combine(empty, "dummy"), "");

            var parser = new XMLParser();
            parser.LoadDocument(Path.Combine(inputDirectory, "code", "app.xml"));
            AppXMLInfo appInfo = parser.GetAppXMLInfo();

            short groupId = appInfo.groupID;
            long parentId = appInfo.titleID & -64424509441L;
            Logger.Log($"Packing title {appInfo.titleID:X16} to {outputDirectory}");

            List<ContentRule> rules = ContentRule.GetCommonRules(groupId, parentId);

            // The library keeps its scratch files in "tmp" under the current directory.
            string previous = Directory.GetCurrentDirectory();
            Directory.CreateDirectory(AppPaths.Temp);
            Directory.SetCurrentDirectory(AppPaths.Temp);
            TextWriter console = Console.Out;
            try
            {
                // It also reports progress by writing tens of thousands of lines to the console.
                Console.SetOut(TextWriter.Null);
                Directory.CreateDirectory(outputDirectory);
                Directory.CreateDirectory("tmp");
                FST.strings = new MemoryStream();
                FST.curEntryOffset = 0;
                var config = new NusPackageConfiguration(inputDirectory, appInfo, new Key(DefaultEncryptionKey), new Key(commonKey), rules);
                NUSpackage package = NUSPackageFactory.CreateNewPackage(config);
                package.PackContents(outputDirectory);
            }
            finally
            {
                Console.SetOut(console);
                Directory.SetCurrentDirectory(previous);
                string tmp = Path.Combine(AppPaths.Temp, "tmp");
                if (Directory.Exists(tmp))
                    Directory.Delete(tmp, true);
            }

            if (!File.Exists(Path.Combine(outputDirectory, "title.tmd")))
                throw new Exception("Packing failed: no title.tmd was produced.");
        }
    }
}
