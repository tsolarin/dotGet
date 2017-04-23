using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

using DotGet.Core.Configuration;
using DotGet.Core.Logging;

namespace DotGet.Core.Commands
{
    public class ListCommand
    {
        private CommandOptions _options;
        private ILogger _logger;

        public ListCommand(CommandOptions options, ILogger logger)
        {
            _options = options;
            _logger = logger;
        }

        public Dictionary<string, string> GetEtc(string path)
        {
            string[] lines = File.ReadAllLines(path);
            Dictionary<string, string> etc = new Dictionary<string, string>();
            foreach (string line in lines)
            {
                string[] parts = line.Split("=:=".ToCharArray());
                etc.Add(parts[0], parts[3]);
            }

            return etc;
        }

        public void Execute()
        {
            string globalNugetDirectory = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? Environment.GetEnvironmentVariable("USERPROFILE") : Environment.GetEnvironmentVariable("HOME");
            globalNugetDirectory = Path.Combine(globalNugetDirectory, ".nuget");

            string etcDirectory = Path.Combine(globalNugetDirectory, "etc");
            _logger.LogInformation(etcDirectory);

            string[] etcFiles = Directory.GetFiles(etcDirectory);
            foreach (string filePath in etcFiles)
            {
                string bin = Path.GetFileNameWithoutExtension(filePath);
                Dictionary<string, string> etc = GetEtc(filePath);
                _logger.LogInformation(etc["tool"] + " => " + bin);
            }
        }
    }
}