﻿using LANCommander.SDK.Enums;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Runtime.InteropServices;
using System.Security.RightsManagement;
using System.Text;
using System.Threading.Tasks;

namespace LANCommander.PlaynitePlugin
{
    internal class PowerShellRuntime
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool Wow64DisableWow64FsRedirection(ref IntPtr ptr);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool Wow64RevertWow64FsRedirection(ref IntPtr ptr);

        public void RunCommand(string command, bool asAdmin = false)
        {
            var tempScript = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".ps1");

            File.WriteAllText(tempScript, command);

            RunScript(tempScript, asAdmin);

            File.Delete(tempScript);
        }

        public void RunScript(string path, bool asAdmin = false, string arguments = null)
        {
            var wow64Value = IntPtr.Zero;

            Wow64DisableWow64FsRedirection(ref wow64Value);

            var process = new Process();

            process.StartInfo.FileName = "powershell.exe";
            process.StartInfo.Arguments = $@"-ExecutionPolicy Unrestricted -File ""{path}""";
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = false;

            if (arguments != null)
                process.StartInfo.Arguments += " " + arguments;

            if (asAdmin)
                process.StartInfo.Verb = "runas";

            process.Start();
            process.WaitForExit();

            Wow64RevertWow64FsRedirection(ref wow64Value);
        }

        public void RunScriptsAsAdmin(IEnumerable<string> paths, string arguments = null)
        {
            // Concatenate scripts
            var sb = new StringBuilder();

            foreach (var path in paths)
            {
                var contents = File.ReadAllText(path);

                sb.AppendLine(contents);
            }

            if (sb.Length > 0)
            {
                var scriptPath = Path.GetTempFileName();

                File.WriteAllText(scriptPath, sb.ToString());

                RunScript(scriptPath, true, arguments);
            }
        }

        public void RunScript(Game game, ScriptType type, string arguments = null)
        {
            var path = GetScriptFilePath(game, type);

            if (File.Exists(path))
            {
                var contents = File.ReadAllText(path);

                if (contents.StartsWith("# Requires Admin"))
                    RunScript(path, true, arguments);
                else
                    RunScript(path, false, arguments);
            }
        }

        public void RunScripts(IEnumerable<Game> games, ScriptType type, string arguments = null)
        {
            List<string> scripts = new List<string>();
            List<string> adminScripts = new List<string>();

            foreach (var game in games)
            {
                var path = GetScriptFilePath(game, type);

                if (!File.Exists(path))
                    continue;

                var contents = File.ReadAllText(path);

                if (contents.StartsWith("# Requires Admin"))
                    adminScripts.Add(path);
                else
                    scripts.Add(path);
            }

            RunScriptsAsAdmin(adminScripts, arguments);

            foreach (var script in scripts)
            {
                RunScript(script, false, arguments);
            }
        }

        public static string GetScriptFilePath(Game game, ScriptType type)
        {
            Dictionary<ScriptType, string> filenames = new Dictionary<ScriptType, string>() {
                { ScriptType.Install, "_install.ps1" },
                { ScriptType.Uninstall, "_uninstall.ps1" },
                { ScriptType.NameChange, "_changename.ps1" },
                { ScriptType.KeyChange, "_changekey.ps1" }
            };

            var filename = filenames[type];

            return Path.Combine(game.InstallDirectory, filename);
        }
    }
}
