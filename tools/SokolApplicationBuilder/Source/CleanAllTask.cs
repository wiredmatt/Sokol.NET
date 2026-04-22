// Copyright (c) 2022 Eli Aloni (a.k.a  elix22)
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Task = Microsoft.Build.Utilities.Task;

namespace SokolApplicationBuilder
{
    public class CleanAllTask : Task
    {
        private static readonly string[] FoldersToDelete = { "bin", "obj", "output", "Android", "ios" };

        Options opts;
        public CleanAllTask(Options opts)
        {
            this.opts = opts;
        }

        public override bool Execute()
        {
            string homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrEmpty(homeDir) || !Directory.Exists(homeDir))
                homeDir = Environment.GetEnvironmentVariable("HOME") ?? "";

            string sokolNetHomeFile = Path.Combine(homeDir, ".sokolnet_config", "sokolnet_home");
            if (!File.Exists(sokolNetHomeFile))
            {
                Log.LogError("ERROR: SokolNetHome configuration not found. Please run 'register' task first.");
                return false;
            }

            string sokolNetHome = File.ReadAllText(sokolNetHomeFile).Trim();
            string examplesPath = Path.Combine(sokolNetHome, "examples");

            if (!Directory.Exists(examplesPath))
            {
                Log.LogError($"Examples folder not found: {examplesPath}");
                return false;
            }

            foreach (string exampleDir in Directory.GetDirectories(examplesPath))
            {
                string exampleName = Path.GetFileName(exampleDir);
                foreach (string folder in FoldersToDelete)
                {
                    string target = Path.Combine(exampleDir, folder);
                    if (Directory.Exists(target))
                    {
                        Log.LogMessage(MessageImportance.High, $"Deleting {exampleName}/{folder}");
                        DeleteDirectory(target);
                    }
                }
            }

            Log.LogMessage(MessageImportance.High, "Clean all examples completed.");
            return true;
        }

        private static void DeleteDirectory(string path)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var psi = new ProcessStartInfo("cmd.exe", $"/c rmdir /s /q \"{path}\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                Process.Start(psi)!.WaitForExit();
            }
            else
            {
                var psi = new ProcessStartInfo("/bin/rm", $"-rf \"{path}\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                Process.Start(psi)!.WaitForExit();
            }
        }

        public override int GetHashCode() => base.GetHashCode();
        public override bool Equals(object? obj) => base.Equals(obj);
        public override string? ToString() => base.ToString();
    }
}
