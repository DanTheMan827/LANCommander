﻿using ICSharpCode.SharpZipLib.Core;
using ICSharpCode.SharpZipLib.Zip;
using LANCommander.PlaynitePlugin.Extensions;
using LANCommander.PlaynitePlugin.Services;
using LANCommander.SDK;
using Playnite.SDK;
using Playnite.SDK.Events;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using PN = Playnite;

namespace LANCommander.PlaynitePlugin
{
    public class LANCommanderLibraryPlugin : LibraryPlugin
    {
        public static readonly ILogger Logger = LogManager.GetLogger();
        internal LANCommanderSettingsViewModel Settings { get; set; }
        internal LANCommanderClient LANCommander { get; set; }
        internal PowerShellRuntime PowerShellRuntime { get; set; }
        internal GameSaveService GameSaveService { get; set; }

        public override Guid Id { get; } = Guid.Parse("48e1bac7-e0a0-45d7-ba83-36f5e9e959fc");
        public override string Name => "LANCommander";
        public override LibraryClient Client { get; } = new LANCommanderLibraryClient();

        internal Dictionary<Guid, string> DownloadCache = new Dictionary<Guid, string>();

        public LANCommanderLibraryPlugin(IPlayniteAPI api) : base(api)
        {
            Properties = new LibraryPluginProperties
            {
                HasSettings = true,
            };

            Settings = new LANCommanderSettingsViewModel(this);

            LANCommander = new LANCommanderClient(Settings.ServerAddress);
            LANCommander.Token = new SDK.Models.AuthToken()
            {
                AccessToken = Settings.AccessToken,
                RefreshToken = Settings.RefreshToken,
            };

            PowerShellRuntime = new PowerShellRuntime();

            GameSaveService = new GameSaveService(LANCommander, PlayniteApi, PowerShellRuntime);
        }

        public override void OnApplicationStarted(OnApplicationStartedEventArgs args)
        {
            if (LANCommander.Token == null || LANCommander.Client == null || !LANCommander.ValidateToken(LANCommander.Token))
            {
                ShowAuthenticationWindow();
            }
        }

        public bool ValidateConnection()
        {
            return LANCommander.ValidateToken(LANCommander.Token);
        }

        public override IEnumerable<GameMetadata> GetGames(LibraryGetGamesArgs args)
        {
            var gameMetadata = new List<GameMetadata>();

            while (!ValidateConnection())
            {
                ShowAuthenticationWindow();
            }


            var games = LANCommander
                .GetGames()
                .Where(g => g.Archives != null && g.Archives.Count() > 0);

            foreach (var game in games)
            {
                try
                {
                    var manifest = LANCommander.GetGameManifest(game.Id);
                    var existingGame = PlayniteApi.Database.Games.FirstOrDefault(g => g.GameId == game.Id.ToString() && g.PluginId == Id && g.IsInstalled);

                    if (existingGame != null)
                    {
                        UpdateGame(manifest, game.Id);

                        continue;
                    }

                    var metadata = new GameMetadata()
                    {
                        IsInstalled = false,
                        Name = manifest.Title,
                        SortingName = manifest.SortTitle,
                        Description = manifest.Description,
                        GameId = game.Id.ToString(),
                        ReleaseDate = new ReleaseDate(manifest.ReleasedOn),
                        //Version = game.Archives.OrderByDescending(a => a.CreatedOn).FirstOrDefault().Version,
                        Icon = new MetadataFile($"{Settings.ServerAddress}{manifest.Icon}"),
                        GameActions = game.Actions.OrderBy(a => a.SortOrder).Select(a => new PN.SDK.Models.GameAction()
                        {
                            Name = a.Name,
                            Arguments = a.Arguments,
                            Path = a.Path,
                            WorkingDir = a.WorkingDirectory,
                            IsPlayAction = a.PrimaryAction
                        }).ToList()
                    };

                    if (manifest.Genre != null && manifest.Genre.Count() > 0)
                        metadata.Genres = new HashSet<MetadataProperty>(manifest.Genre.Select(g => new MetadataNameProperty(g)));

                    if (manifest.Developers != null && manifest.Developers.Count() > 0)
                        metadata.Developers = new HashSet<MetadataProperty>(manifest.Developers.Select(d => new MetadataNameProperty(d)));

                    if (manifest.Publishers != null && manifest.Publishers.Count() > 0)
                        metadata.Publishers = new HashSet<MetadataProperty>(manifest.Publishers.Select(p => new MetadataNameProperty(p)));

                    if (manifest.Tags != null && manifest.Tags.Count() > 0)
                        metadata.Tags = new HashSet<MetadataProperty>(manifest.Tags.Select(t => new MetadataNameProperty(t)));

                    metadata.Features = new HashSet<MetadataProperty>();

                    if (manifest.Singleplayer)
                        metadata.Features.Add(new MetadataNameProperty("Singleplayer"));

                    if (manifest.LocalMultiplayer != null)
                        metadata.Features.Add(new MetadataNameProperty($"Local Multiplayer {manifest.LocalMultiplayer.GetPlayerCount()}".Trim()));

                    if (manifest.LanMultiplayer != null)
                        metadata.Features.Add(new MetadataNameProperty($"LAN Multiplayer {manifest.LanMultiplayer.GetPlayerCount()}".Trim()));

                    if (manifest.OnlineMultiplayer != null)
                        metadata.Features.Add(new MetadataNameProperty($"Online Multiplayer {manifest.OnlineMultiplayer.GetPlayerCount()}".Trim()));

                    gameMetadata.Add(metadata);
                }
                catch (Exception ex)
                {

                }
            };

            return gameMetadata;
        }

        public override IEnumerable<InstallController> GetInstallActions(GetInstallActionsArgs args)
        {
            if (args.Game.PluginId != Id)
                yield break;

            yield return new LANCommanderInstallController(this, args.Game);
        }

        public override IEnumerable<UninstallController> GetUninstallActions(GetUninstallActionsArgs args)
        {
            if (args.Game.PluginId != Id)
                yield break;

            yield return new LANCommanderUninstallController(this, args.Game);
        }

        public override IEnumerable<GameMenuItem> GetGameMenuItems(GetGameMenuItemsArgs args)
        {
            if (args.Games.Count == 1 && args.Games.First().IsInstalled && !String.IsNullOrWhiteSpace(args.Games.First().InstallDirectory))
            {
                var nameChangeScriptPath = PowerShellRuntime.GetScriptFilePath(args.Games.First(), SDK.Enums.ScriptType.NameChange);
                var keyChangeScriptPath = PowerShellRuntime.GetScriptFilePath(args.Games.First(), SDK.Enums.ScriptType.KeyChange);
                var installScriptPath = PowerShellRuntime.GetScriptFilePath(args.Games.First(), SDK.Enums.ScriptType.Install);

                if (File.Exists(nameChangeScriptPath))
                    yield return new GameMenuItem
                    {
                        Description = "Change Player Name",
                        Action = (nameChangeArgs) =>
                        {
                            var oldName = Settings.PlayerName;

                            var result = PlayniteApi.Dialogs.SelectString("Enter your player name", "Change Player Name", Settings.PlayerName);

                            if (result.Result == true)
                                PowerShellRuntime.RunScript(nameChangeArgs.Games.First(), SDK.Enums.ScriptType.NameChange, $@"""{result.SelectedString}"" ""{oldName}""");
                        }
                    };

                if (File.Exists(keyChangeScriptPath))
                    yield return new GameMenuItem
                    {
                        Description = "Change Game Key",
                        Action = (keyChangeArgs) =>
                        {
                            Guid gameId;

                            if (Guid.TryParse(keyChangeArgs.Games.First().GameId, out gameId))
                            {
                                // NUKIEEEE
                                var newKey = LANCommander.GetNewKey(gameId);

                                if (String.IsNullOrEmpty(newKey))
                                    PlayniteApi.Dialogs.ShowErrorMessage("There are no more keys available on the server.", "No Keys Available");
                                else
                                    PowerShellRuntime.RunScript(keyChangeArgs.Games.First(), SDK.Enums.ScriptType.KeyChange, $@"""{newKey}""");
                            }
                            else
                            {
                                PlayniteApi.Dialogs.ShowErrorMessage("This game could not be found on the server. Your game may be corrupted.");
                            }
                        }
                    };

                if (File.Exists(installScriptPath))
                    yield return new GameMenuItem
                    {
                        Description = "Run Install Script",
                        Action = (installArgs) =>
                        {
                            Guid gameId;

                            if (Guid.TryParse(installArgs.Games.First().GameId, out gameId))
                            {
                                PowerShellRuntime.RunScript(installArgs.Games.First(), SDK.Enums.ScriptType.Install);
                            }
                            else
                            {
                                PlayniteApi.Dialogs.ShowErrorMessage("This game could not be found on the server. Your game may be corrupted.");
                            }
                        }
                    };
            }
        }

        // To add new main menu items override GetMainMenuItems
        public override IEnumerable<MainMenuItem> GetMainMenuItems(GetMainMenuItemsArgs args)
        {
            yield return new MainMenuItem
            {
                Description = "Change Player Name (All Games)",
                Action = (args2) =>
                {
                    ShowNameChangeWindow();
                }
            };

            yield return new MainMenuItem
            {
                Description = "Clear Download Cache",
                Action = (args2) =>
                {
                    foreach (var gameId in DownloadCache.Keys)
                    {
                        File.Delete(DownloadCache[gameId]);
                        DownloadCache.Remove(gameId);
                    }

                    PlayniteApi.Dialogs.ShowMessage("The download cache has been cleared and any temporary files have been deleted.", "Cache Cleared!", MessageBoxButton.OK);
                }
            };
        }

        public override void OnGameStarting(OnGameStartingEventArgs args)
        {
            GameSaveService.DownloadSave(args.Game);
        }

        public override void OnGameStopped(OnGameStoppedEventArgs args)
        {
            GameSaveService.UploadSave(args.Game);
        }

        public override IEnumerable<TopPanelItem> GetTopPanelItems()
        {
            yield return new TopPanelItem
            {
                Title = "Click to change your name (All Games)",
                Icon = new TextBlock
                {
                    Text = char.ConvertFromUtf32(0xeded),
                    FontSize = 16,
                    FontFamily = ResourceProvider.GetResource("FontIcoFont") as FontFamily,
                    Padding = new Thickness(10, 0, 10, 0),
                    
                },
                Activated = () => {
                    ShowNameChangeWindow();
                }
            };
        }

        public override ISettings GetSettings(bool firstRunSettings)
        {
            return Settings;
        }

        public override UserControl GetSettingsView(bool firstRunView)
        {
            return new LANCommanderSettingsView(this);
        }

        public void ShowNameChangeWindow()
        {
            var result = PlayniteApi.Dialogs.SelectString("Enter your new player name. This will change your name across all installed games!", "Enter Name", Settings.PlayerName);

            if (result.Result == true)
            {
                // Check to make sure they're staying in ASCII encoding
                if (String.IsNullOrEmpty(result.SelectedString) || result.SelectedString.Any(c => c > sbyte.MaxValue))
                {
                    PlayniteApi.Dialogs.ShowErrorMessage("The name you supplied is invalid. Try again.");

                    ShowNameChangeWindow();
                }
                else
                {
                    Settings.PlayerName = result.SelectedString;

                    SavePluginSettings(Settings);

                    var games = PlayniteApi.Database.Games.Where(g => g.IsInstalled).ToList();

                    PowerShellRuntime.RunScripts(games, SDK.Enums.ScriptType.NameChange, Settings.PlayerName);
                }
            }
        }

        public Window ShowAuthenticationWindow()
        {
            Window window = null;
            Application.Current.Dispatcher.Invoke((Action)delegate
            {
                window = PlayniteApi.Dialogs.CreateWindow(new WindowCreationOptions()
                {
                    ShowMinimizeButton = false,
                });

                window.Title = "Authenticate to LANCommander";
                window.Width = 400;
                window.Height = 230;
                window.Content = new Views.Authentication(this);
                window.DataContext = new ViewModels.Authentication()
                {
                    ServerAddress = Settings?.ServerAddress
                };

                window.Owner = PlayniteApi.Dialogs.GetCurrentAppWindow();
                window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                window.ResizeMode = ResizeMode.NoResize;
                window.ShowDialog();
            });

            return window;
        }

        public void UpdateGame(SDK.GameManifest manifest, Guid gameId)
        {
            var game = PlayniteApi.Database.Games.First(g => g.GameId == gameId.ToString());

            if (game == null)
                return;

            if (game.GameActions == null)
                game.GameActions = new ObservableCollection<PN.SDK.Models.GameAction>();
            else
                game.GameActions.Clear();

            game.Icon = $"{Settings.ServerAddress}{manifest.Icon}";

            if (manifest.Actions == null)
                throw new Exception("The game has no actions defined.");

            foreach (var action in manifest.Actions.OrderBy(a => a.SortOrder))
            {
                bool isFirstAction = !manifest.Actions.Any(a => a.IsPrimaryAction) && manifest.Actions.First().Name == action.Name;

                game.GameActions.Add(new PN.SDK.Models.GameAction()
                {
                    Name = action.Name,
                    Arguments = action.Arguments,
                    Path = PlayniteApi.ExpandGameVariables(game, action.Path?.Replace('/', Path.DirectorySeparatorChar)),
                    WorkingDir = PlayniteApi.ExpandGameVariables(game, action.WorkingDirectory?.Replace('/', Path.DirectorySeparatorChar) ?? game.InstallDirectory),
                    IsPlayAction = action.IsPrimaryAction || isFirstAction
                });
            }

            #region Features
            var singlePlayerFeature = PlayniteApi.Database.Features.FirstOrDefault(f => f.Name == "Single Player");

            if (manifest.LanMultiplayer != null)
            {
                var multiplayerInfo = manifest.LanMultiplayer;

                string playerCount = multiplayerInfo.MinPlayers == multiplayerInfo.MaxPlayers ? $"({multiplayerInfo.MinPlayers} players)" : $"({multiplayerInfo.MinPlayers} - {multiplayerInfo.MaxPlayers} players)";
                string featureName = $"LAN Multiplayer {playerCount}";

                if (PlayniteApi.Database.Features.Any(f => f.Name == featureName))
                {
                    game.Features.Add(PlayniteApi.Database.Features.FirstOrDefault(f => f.Name == featureName));
                }
                else
                {
                    PlayniteApi.Database.Features.Add(new GameFeature()
                    {
                        Name = featureName
                    });

                    game.Features.Add(new GameFeature()
                    {
                        Name = $"LAN Multiplayer {playerCount}"
                    });
                }
            }

            if (manifest.LocalMultiplayer != null)
            {
                var multiplayerInfo = manifest.LocalMultiplayer;

                string playerCount = multiplayerInfo.MinPlayers == multiplayerInfo.MaxPlayers ? $"({multiplayerInfo.MinPlayers} players)" : $"({multiplayerInfo.MinPlayers} - {multiplayerInfo.MaxPlayers} players)";

                game.Features.Add(new GameFeature()
                {
                    Name = $"Local Multiplayer {playerCount}"
                });
            }

            if (manifest.OnlineMultiplayer != null)
            {
                var multiplayerInfo = manifest.OnlineMultiplayer;

                string playerCount = multiplayerInfo.MinPlayers == multiplayerInfo.MaxPlayers ? $"({multiplayerInfo.MinPlayers} players)" : $"({multiplayerInfo.MinPlayers} - {multiplayerInfo.MaxPlayers} players)";

                game.Features.Add(new GameFeature()
                {
                    Name = $"Online Multiplayer {playerCount}"
                });
            }
            #endregion

            PlayniteApi.Database.Games.Update(game);
        }
    }
}
