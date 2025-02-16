﻿using LANCommander.SDK;
using LANCommander.SDK.Models;
using RestSharp;
using RestSharp.Extensions;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media.Converters;
using YamlDotNet.Core;

namespace LANCommander.PlaynitePlugin
{
    internal class LANCommanderClient
    {
        public readonly RestClient Client;
        public AuthToken Token;

        public LANCommanderClient(string baseUrl)
        {
            if (!String.IsNullOrWhiteSpace(baseUrl))
                Client = new RestClient(baseUrl);
        }

        private T PostRequest<T>(string route, object body)
        {
            var request = new RestRequest(route)
                .AddJsonBody(body)
                .AddHeader("Authorization", $"Bearer {Token.AccessToken}");

            var response = Client.Post<T>(request);

            return response.Data;
        }

        private T GetRequest<T>(string route)
        {
            var request = new RestRequest(route)
                .AddHeader("Authorization", $"Bearer {Token.AccessToken}");

            var response = Client.Get<T>(request);

            return response.Data;
        }

        private string DownloadRequest(string route, Action<DownloadProgressChangedEventArgs> progressHandler, Action<AsyncCompletedEventArgs> completeHandler)
        {
            route = route.TrimStart('/');

            var client = new WebClient();
            var tempFile = Path.GetTempFileName();

            client.Headers.Add("Authorization", $"Bearer {Token.AccessToken}");
            client.DownloadProgressChanged += (s, e) => progressHandler(e);
            client.DownloadFileCompleted += (s, e) => completeHandler(e);

            client.DownloadFileAsync(new Uri($"{Client.BaseUrl}{route}"), tempFile);

            return tempFile;
        }

        public async Task<AuthResponse> AuthenticateAsync(string username, string password)
        {
            var response = await Client.ExecuteAsync<AuthResponse>(new RestRequest("/api/Auth", Method.POST).AddJsonBody(new AuthRequest()
            {
                UserName = username,
                Password = password
            }));

            switch (response.StatusCode)
            {
                case HttpStatusCode.OK:
                    return response.Data;

                case HttpStatusCode.Forbidden:
                case HttpStatusCode.BadRequest:
                case HttpStatusCode.Unauthorized:
                    throw new WebException("Invalid username or password");

                default:
                    throw new WebException("Could not communicate with the server");
            }
        }

        public async Task<AuthResponse> RegisterAsync(string username, string password)
        {
            var response = await Client.ExecuteAsync<AuthResponse>(new RestRequest("/api/auth/register", Method.POST).AddJsonBody(new AuthRequest()
            {
                UserName = username,
                Password = password
            }));

            switch (response.StatusCode)
            {
                case HttpStatusCode.OK:
                    return response.Data;

                case HttpStatusCode.BadRequest:
                case HttpStatusCode.Forbidden:
                case HttpStatusCode.Unauthorized:
                    throw new WebException(response.Data.Message);

                default:
                    throw new WebException("Could not communicate with the server");
            }
        }

        public async Task<bool> PingAsync()
        {
            var response = await Client.ExecuteAsync(new RestRequest("/api/Ping", Method.GET));

            return response.StatusCode == HttpStatusCode.OK;
        }

        public AuthResponse RefreshToken(AuthToken token)
        {
            var request = new RestRequest("/api/Auth/Refresh")
                .AddJsonBody(token);

            var response = Client.Post<AuthResponse>(request);

            if (response.StatusCode != HttpStatusCode.OK)
                throw new WebException(response.ErrorMessage);

            return response.Data;
        }

        public bool ValidateToken(AuthToken token)
        {
            if (token == null)
                return false;

            var request = new RestRequest("/api/Auth/Validate")
                .AddHeader("Authorization", $"Bearer {token.AccessToken}");

            if (String.IsNullOrEmpty(token.AccessToken) || String.IsNullOrEmpty(token.RefreshToken))
                return false;

            var response = Client.Post(request);

            return response.StatusCode == HttpStatusCode.OK;
        }

        public IEnumerable<Game> GetGames()
        {
            return GetRequest<IEnumerable<Game>>("/api/Games");
        }

        public Game GetGame(Guid id)
        {
            return GetRequest<Game>($"/api/Games/{id}");
        }

        public GameManifest GetGameManifest(Guid id)
        {
            return GetRequest<GameManifest>($"/api/Games/{id}/Manifest");
        }

        public string DownloadGame(Guid id, Action<DownloadProgressChangedEventArgs> progressHandler, Action<AsyncCompletedEventArgs> completeHandler)
        {
            return DownloadRequest($"/api/Games/{id}/Download", progressHandler, completeHandler);
        }

        public string DownloadArchive(Guid id, Action<DownloadProgressChangedEventArgs> progressHandler, Action<AsyncCompletedEventArgs> completeHandler)
        {
            return DownloadRequest($"/api/Archives/Download/{id}", progressHandler, completeHandler);
        }

        public string DownloadSave(Guid id, Action<DownloadProgressChangedEventArgs> progressHandler, Action<AsyncCompletedEventArgs> completeHandler)
        {
            return DownloadRequest($"/api/Saves/Download/{id}", progressHandler, completeHandler);
        }

        public string DownloadLatestSave(Guid gameId, Action<DownloadProgressChangedEventArgs> progressHandler, Action<AsyncCompletedEventArgs> completeHandler)
        {
            return DownloadRequest($"/api/Saves/DownloadLatest/{gameId}", progressHandler, completeHandler);
        }

        public GameSave UploadSave(string gameId, byte[] data)
        {
            var request = new RestRequest($"/api/Saves/Upload/{gameId}", Method.POST)
                .AddHeader("Authorization", $"Bearer {Token.AccessToken}");

            request.AddFile(gameId, data, gameId);

            var response = Client.Post<GameSave>(request);

            return response.Data;
        }

        public string GetKey(Guid id)
        {
            var macAddress = GetMacAddress();

            var request = new KeyRequest()
            {
                GameId = id,
                MacAddress = macAddress,
                ComputerName = Environment.MachineName,
                IpAddress = GetIpAddress(),
            };

            var response = PostRequest<Key>($"/api/Keys/Get", request);

            return response.Value;
        }

        public string GetAllocatedKey(Guid id)
        {
            var macAddress = GetMacAddress();

            var request = new KeyRequest()
            {
                GameId = id,
                MacAddress = macAddress,
                ComputerName = Environment.MachineName,
                IpAddress = GetIpAddress(),
            };

            var response = PostRequest<Key>($"/api/Keys/GetAllocated/{id}", request);

            if (response == null)
                return String.Empty;

            return response.Value;
        }

        public string GetNewKey(Guid id)
        {
            var macAddress = GetMacAddress();

            var request = new KeyRequest()
            {
                GameId = id,
                MacAddress = macAddress,
                ComputerName = Environment.MachineName,
                IpAddress = GetIpAddress(),
            };

            var response = PostRequest<Key>($"/api/Keys/Allocate/{id}", request);

            if (response == null)
                return String.Empty;

            return response.Value;
        }

        private string GetMacAddress()
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(nic => nic.OperationalStatus == OperationalStatus.Up && nic.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .Select(nic => nic.GetPhysicalAddress().ToString())
                .FirstOrDefault();
        }

        private string GetIpAddress()
        {
            return Dns.GetHostEntry(Dns.GetHostName()).AddressList[0].ToString();
        }
    }
}
