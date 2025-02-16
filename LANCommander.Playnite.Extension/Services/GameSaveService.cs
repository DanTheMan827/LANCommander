﻿using ICSharpCode.SharpZipLib.Zip;
using LANCommander.SDK;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using YamlDotNet.Serialization.NamingConventions;
using YamlDotNet.Serialization;
using ICSharpCode.SharpZipLib.Core;
using Playnite.SDK;

namespace LANCommander.PlaynitePlugin.Services
{
    internal class GameSaveService
    {
        private readonly LANCommanderClient LANCommander;
        private readonly IPlayniteAPI PlayniteApi;
        private readonly PowerShellRuntime PowerShellRuntime;

        internal GameSaveService(LANCommanderClient lanCommander, IPlayniteAPI playniteApi, PowerShellRuntime powerShellRuntime) {
            LANCommander = lanCommander;
            PlayniteApi = playniteApi;
            PowerShellRuntime = powerShellRuntime;
        }

        internal void DownloadSave(Game game)
        {
            string tempFile = String.Empty;

            if (game != null)
            {
                PlayniteApi.Dialogs.ActivateGlobalProgress(progress =>
                {
                    progress.ProgressMaxValue = 100;
                    progress.CurrentProgressValue = 0;

                    var destination = LANCommander.DownloadLatestSave(Guid.Parse(game.GameId), (changed) =>
                    {
                        progress.CurrentProgressValue = changed.ProgressPercentage;
                    }, (complete) =>
                    {
                        progress.CurrentProgressValue = 100;
                    });

                    // Lock the thread until download is done
                    while (progress.CurrentProgressValue != 100)
                    {

                    }

                    tempFile = destination;
                },
                new GlobalProgressOptions("Downloading latest save...")
                {
                    IsIndeterminate = false,
                    Cancelable = false
                });

                // Go into the archive and extract the files to the correct locations
                try
                {
                    var tempLocation = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

                    Directory.CreateDirectory(tempLocation);

                    ExtractFilesFromZip(tempFile, tempLocation);

                    var deserializer = new DeserializerBuilder()
                        .WithNamingConvention(new PascalCaseNamingConvention())
                        .Build();

                    var manifestContents = File.ReadAllText(Path.Combine(tempLocation, "_manifest.yml"));

                    var manifest = deserializer.Deserialize<GameManifest>(manifestContents);

                    #region Move files
                    foreach (var savePath in manifest.SavePaths.Where(sp => sp.Type == "File"))
                    {
                        bool inInstallDir = savePath.Path.StartsWith("{InstallDir}");
                        string tempSavePath = Path.Combine(tempLocation, savePath.Id.ToString());

                        var tempSavePathFile = Path.Combine(tempSavePath, savePath.Path.Replace('/', '\\').Replace("{InstallDir}\\", ""));

                        var destination = Environment.ExpandEnvironmentVariables(savePath.Path.Replace('/', '\\').Replace("{InstallDir}", game.InstallDirectory));

                        if (File.Exists(tempSavePathFile))
                        {
                            // Is file, move file
                            if (File.Exists(destination))
                                File.Delete(destination);

                            File.Move(tempSavePathFile, destination);
                        }
                        else if (Directory.Exists(tempSavePath))
                        {
                            var files = Directory.GetFiles(tempSavePath, "*", SearchOption.AllDirectories);

                            if (inInstallDir)
                            {
                                foreach (var file in files)
                                {
                                    if (inInstallDir)
                                    {
                                        // Files are in the game's install directory. Move them there from the save path.
                                        destination = file.Replace(tempSavePath, savePath.Path.Replace('/', '\\').TrimEnd('\\').Replace("{InstallDir}", game.InstallDirectory));

                                        if (File.Exists(destination))
                                            File.Delete(destination);

                                        File.Move(file, destination);
                                    }
                                    else
                                    {
                                        // Specified path is probably an absolute path, maybe with environment variables.
                                        destination = Environment.ExpandEnvironmentVariables(file.Replace(tempSavePathFile, savePath.Path.Replace('/', '\\')));

                                        if (File.Exists(destination))
                                            File.Delete(destination);

                                        File.Move(file, destination);
                                    }
                                }
                            }
                            else
                            {

                            }
                        }
                    }
                    #endregion

                    #region Handle registry importing
                    var registryImportFilePath = Path.Combine(tempLocation, "_registry.reg");

                    if (File.Exists(registryImportFilePath))
                    {
                        var registryImportFileContents = File.ReadAllText(registryImportFilePath);

                        PowerShellRuntime.RunCommand($"regedit.exe /s \"{registryImportFilePath}\"", registryImportFileContents.Contains("HKEY_LOCAL_MACHINE"));
                    }
                    #endregion

                    // Clean up temp files
                    Directory.Delete(tempLocation, true);
                }
                catch (Exception ex)
                {

                }
            }
        }

        internal void UploadSave(Game game)
        {
            var manifestPath = Path.Combine(game.InstallDirectory, "_manifest.yml");

            if (File.Exists(manifestPath))
            {
                var deserializer = new DeserializerBuilder()
                    .WithNamingConvention(new PascalCaseNamingConvention())
                    .Build();

                var manifest = deserializer.Deserialize<GameManifest>(File.ReadAllText(manifestPath));
                var temp = Path.GetTempFileName();

                using (ZipOutputStream zipStream = new ZipOutputStream(File.Create(temp)))
                {
                    zipStream.SetLevel(5);

                    #region Add files from defined paths
                    foreach (var savePath in manifest.SavePaths.Where(sp => sp.Type == "File"))
                    {
                        var localPath = Environment.ExpandEnvironmentVariables(savePath.Path.Replace('/', '\\').Replace("{InstallDir}", game.InstallDirectory));

                        if (Directory.Exists(localPath))
                        {
                            AddDirectoryToZip(zipStream, localPath, localPath, savePath.Id);
                        }
                        else if (File.Exists(localPath))
                        {
                            var entry = new ZipEntry(Path.Combine(savePath.Id.ToString(), savePath.Path.Replace("{InstallDir}/", "")));

                            zipStream.PutNextEntry(entry);

                            byte[] buffer = File.ReadAllBytes(localPath);

                            zipStream.Write(buffer, 0, buffer.Length);
                            zipStream.CloseEntry();
                        }
                    }
                    #endregion

                    #region Export registry keys
                    if (manifest.SavePaths.Any(sp => sp.Type == "Registry"))
                    {
                        List<string> tempRegFiles = new List<string>();

                        var exportCommand = new StringBuilder();

                        foreach (var savePath in manifest.SavePaths.Where(sp => sp.Type == "Registry"))
                        {
                            var tempRegFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".reg");

                            exportCommand.AppendLine($"reg.exe export \"{savePath.Path.Replace(":\\", "\\")}\" \"{tempRegFile}\"");
                            tempRegFiles.Add(tempRegFile);
                        }

                        PowerShellRuntime.RunCommand(exportCommand.ToString());

                        var exportFile = new StringBuilder();

                        foreach (var tempRegFile in tempRegFiles)
                        {
                            exportFile.AppendLine(File.ReadAllText(tempRegFile));
                            File.Delete(tempRegFile);
                        }

                        zipStream.PutNextEntry(new ZipEntry("_registry.reg"));

                        byte[] regBuffer = Encoding.UTF8.GetBytes(exportFile.ToString());

                        zipStream.Write(regBuffer, 0, regBuffer.Length);
                        zipStream.CloseEntry();
                    }
                    #endregion

                    var manifestEntry = new ZipEntry("_manifest.yml");

                    zipStream.PutNextEntry(manifestEntry);

                    byte[] manifestBuffer = File.ReadAllBytes(manifestPath);

                    zipStream.Write(manifestBuffer, 0, manifestBuffer.Length);
                    zipStream.CloseEntry();
                }

                var save = LANCommander.UploadSave(game.GameId, File.ReadAllBytes(temp));

                File.Delete(temp);
            }
        }

        private void AddDirectoryToZip(ZipOutputStream zipStream, string path, string workingDirectory, Guid pathId)
        {
            foreach (var file in Directory.GetFiles(path))
            {
                // Oh man is this a hack. We should be removing only the working directory from the start,
                // but we're making the assumption that the working dir put in actually prefixes the path.
                // Also wtf, that Path.Combine is stripping the pathId out?
                var entry = new ZipEntry(Path.Combine(pathId.ToString(), path.Substring(workingDirectory.Length), Path.GetFileName(file)));

                zipStream.PutNextEntry(entry);

                byte[] buffer = File.ReadAllBytes(file);

                zipStream.Write(buffer, 0, buffer.Length);

                zipStream.CloseEntry();
            }

            foreach (var child in Directory.GetDirectories(path))
            {
                // See above
                //ZipEntry entry = new ZipEntry(Path.Combine(pathId.ToString(), path.Substring(workingDirectory.Length), Path.GetFileName(path)));

                //zipStream.PutNextEntry(entry);
                //zipStream.CloseEntry();

                AddDirectoryToZip(zipStream, child, workingDirectory, pathId);
            }
        }

        private void ExtractFilesFromZip(string zipPath, string destination)
        {
            ZipFile file = null;

            try
            {
                FileStream fs = File.OpenRead(zipPath);

                file = new ZipFile(fs);

                foreach (ZipEntry entry in file)
                {
                    if (!entry.IsFile)
                        continue;

                    byte[] buffer = new byte[4096];
                    var zipStream = file.GetInputStream(entry);

                    var entryDestination = Path.Combine(destination, entry.Name);
                    var entryDirectory = Path.GetDirectoryName(entryDestination);

                    if (!String.IsNullOrWhiteSpace(entryDirectory))
                        Directory.CreateDirectory(entryDirectory);

                    using (FileStream streamWriter = File.Create(entryDestination))
                    {
                        StreamUtils.Copy(zipStream, streamWriter, buffer);
                    }
                }
            }
            finally
            {
                if (file != null)
                {
                    file.IsStreamOwner = true;
                    file.Close();
                }
            }
        }
    }
}
