// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Microsoft.DotNet.Build.Tasks
{
    public sealed class Crossgen : ToolTask
    {
        public Crossgen()
        {
            // Disable partial NGEN to avoid excess JIT-compilation.
            // The intention is to pre-compile as much as possible.
            EnvironmentVariables = new string[] { "COMPlus_PartialNGen=0" };
        }

        [Required]
        public string SourceAssembly { get;set; }

        [Required]
        public string DestinationPath { get; set; }

        [Required]
        public string Architecture { get; set; }

        public string CrossgenPath { get; set; }

        public bool CreateSymbols { get; set; }

        public bool ReadyToRun { get; set; }

        public ITaskItem[] PlatformAssemblyPaths { get; set; }

        private string TempOutputPath { get; set; }

        protected override bool ValidateParameters()
        {
            base.ValidateParameters();

            if (!File.Exists(SourceAssembly))
            {
                Log.LogError($"SourceAssembly '{SourceAssembly}' does not exist.");

                return false;
            }

            return true;
        }

        public override bool Execute()
        {
            string tempDirPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(tempDirPath);
            TempOutputPath = Path.Combine(tempDirPath, Path.GetFileName(DestinationPath));

            var toolResult = base.Execute();

            if (toolResult)
            {
                var files = System.IO.Directory.GetFiles(Path.GetDirectoryName(TempOutputPath));
                var dest = Path.GetDirectoryName(DestinationPath);
                // Copy both dll and pdb files to the destination folder
                foreach(var file in files)
                {
                    File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), overwrite: true);
                    // Delete file in temp
                    File.Delete(file);
                }
            }

            if (File.Exists(TempOutputPath))
            {
                File.Delete(TempOutputPath);
            }
            Directory.Delete(tempDirPath);

            return toolResult;
        }

        protected override string ToolName => "crossgen2";

        protected override MessageImportance StandardOutputLoggingImportance
            // Default is low, but we want to see output at normal verbosity.
            => MessageImportance.Normal;

        protected override MessageImportance StandardErrorLoggingImportance
            // This turns stderr messages into msbuild errors below.
            => MessageImportance.High;

        protected override void LogEventsFromTextOutput(string singleLine, MessageImportance messageImportance)
        {
            // Crossgen's error/warning formatting is inconsistent and so we do
            // not use the "canonical error format" handling of base.
            //
            // Furthermore, we don't want to log crossgen warnings as msbuild
            // warnings because we cannot prevent them and they are only
            // occasionally formatted as something that base would recognize as
            // a canonically formatted warning anyway.
            //
            // One thing that is consistent is that crossgen errors go to stderr
            // and everything else goes to stdout. Above, we set stderr to high
            // importance above, and stdout to normal. So we can use that here
            // to distinguish between errors and messages.
            if (messageImportance == MessageImportance.High)
            {
                Log.LogError(singleLine);
            }
            else
            {
                Log.LogMessage(messageImportance, singleLine);
            }
        }

        protected override string GenerateFullPathToTool()
        {
            if (CrossgenPath != null)
            {
                return CrossgenPath;
            }

            return "crossgen2";
        }

        protected override string GenerateCommandLineCommands() => $"{GetInPath()} {GetOutPath()} {GetArchitecture()} {GetPlatformAssemblyPaths()} {GetCreateSymbols()}";

        private string GetArchitecture() => $"--targetarch {Architecture}";

        private string GetCreateSymbols()
        {
            var option = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "--pdb" : "--perfmap";
            return $"{option}";
        }

        private string GetReadyToRun()
        {
            if (ReadyToRun)
            {
                return "-readytorun";
            }

            return null;
        }

        private string GetInPath() => $"\"{SourceAssembly}\"";

        private string GetOutPath() => $"-o \"{TempOutputPath}\"";

        private string GetPlatformAssemblyPaths()
        {
            var platformAssemblyPaths = String.Empty;

            if (PlatformAssemblyPaths != null)
            {
                foreach (var excludeTaskItem in PlatformAssemblyPaths)
                {
                    platformAssemblyPaths += $"-r {excludeTaskItem.ItemSpec}{Path.DirectorySeparatorChar}*.dll ";
                }
            }
            
            return platformAssemblyPaths;
        }

        private string GetMissingDependenciesOk() => "-MissingDependenciesOK";

        protected override void LogToolCommand(string message) => base.LogToolCommand($"{base.GetWorkingDirectory()}> {message}");
    }
}
