﻿using LANCommander.SDK.Models;
using Playnite.SDK;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace LANCommander.PlaynitePlugin
{
    public partial class LANCommanderSettingsView : UserControl
    {
        private LANCommanderLibraryPlugin Plugin;
        private LANCommanderSettingsViewModel Settings;

        public LANCommanderSettingsView(LANCommanderLibraryPlugin plugin)
        {
            this.Plugin = plugin;
            this.Settings = plugin.Settings;

            InitializeComponent();

            DataContext = this;

            UpdateAuthenticationButtonVisibility();
        }

        private void UpdateAuthenticationButtonVisibility()
        {
            PART_AuthenticateLabel.Content = "Checking authentication status...";
            PART_AuthenticationButton.IsEnabled = false;
            PART_InstallDirectory.Text = Settings.InstallDirectory;

            var token = new AuthToken()
            {
                AccessToken = Settings.AccessToken,
                RefreshToken = Settings.RefreshToken,
            };

            var task = Task.Run(() => Plugin.LANCommander.ValidateToken(token))
                .ContinueWith(antecedent =>
                {
                    try
                    {
                        Dispatcher.Invoke(new System.Action(() =>
                        {
                            if (antecedent.Result == false)
                            {
                                PART_AuthenticateLabel.Content = "Authentication failed!";
                                PART_AuthenticationButton.IsEnabled = true;
                            }
                            else
                            {
                                PART_AuthenticateLabel.Content = "Connection established!";
                                PART_AuthenticationButton.IsEnabled = false;
                            }
                        }));
                    }
                    catch (Exception ex)
                    {

                    }
                });
        }

        private void AuthenticateButton_Click(object sender, RoutedEventArgs e)
        {
            var authWindow = Plugin.ShowAuthenticationWindow();

            authWindow.Closed += AuthWindow_Closed;
        }

        private void AuthWindow_Closed(object sender, EventArgs e)
        {
            UpdateAuthenticationButtonVisibility();
        }

        private void SelectInstallDirectory_Click(object sender, RoutedEventArgs e)
        {
            var selectedDirectory = Plugin.PlayniteApi.Dialogs.SelectFolder();

            if (!String.IsNullOrWhiteSpace(selectedDirectory))
            {
                PART_InstallDirectory.Text = selectedDirectory;
                Plugin.Settings.InstallDirectory = selectedDirectory;
            }
        }
    }
}
