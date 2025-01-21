using System.Diagnostics;
using System.Net;
using TwitchLib.Api;
using TwitchLib.Api.Core.Enums;
using TwitchLib.Api.Helix.Models.Users.GetUsers;
using TwitchLib.Client;
using TwitchLib.Client.Models;
using TwitchLib.Communication.Clients;
using TwitchLib.Communication.Models;
using TwitchLib.PubSub;

namespace TwitchBotFramework
{
    public abstract class Framework
    {
        private readonly TwitchAPI _api;
        private readonly TwitchClient _client;
        private readonly TwitchPubSub _pubSub;
        private readonly string _resFile = "response.html";// If this file exists it will be your response html for token auth.
        private Token? _token;
        private User? _owner;

        /// <summary>
        /// Scopes bot needs access to
        /// </summary>
        protected abstract List<AuthScopes> _scopes { get; }

        /// <summary>
        /// Public api access
        /// </summary>
        public TwitchAPI Api => _api;

        /// <summary>
        /// Public client access
        /// </summary>
        public TwitchClient Client => _client;

        /// <summary>
        /// Public pubsub access
        /// </summary>
        public TwitchPubSub PubSub => _pubSub;

        /// <summary>
        /// Twitch bot framework
        /// </summary>
        /// <param name="token">Can take Token object loaded from file</param>
        protected Framework(Token? token = null)
        {
            if (token != null)
                _token = token;
            _api = new TwitchAPI();
            _client = new TwitchClient(new WebSocketClient(new ClientOptions
            {
                MessagesAllowedInPeriod = 750,
                ThrottlingPeriod = TimeSpan.FromSeconds(30)
            }));
            _pubSub = new TwitchPubSub();
        }

        /// <summary>
        /// Initialize the bot framework
        /// </summary>
        /// <param name="clientId"></param>
        /// <param name="secret"></param>
        /// <returns>Returns a token object for saving to file</returns>
        public async Task<Token?> InitAsync(string clientId, string secret)
        {
            if (_token == null)
                await GetTokenAsync(clientId, secret, _scopes);
            if (_token.Expires <= DateTime.UtcNow)
                await RefreshTokenAsync(clientId, secret);
            await LoggingAsync("Initializing API...");
            _api.Settings.ClientId = clientId;
            _api.Settings.AccessToken = _token.AccessToken;
            _api.Settings.Scopes = _scopes;
            await LoggingAsync("Getting token owner...");
            var res = await _api.Helix.Users.GetUsersAsync(accessToken: _token.AccessToken);
            _owner = res.Users.First() ?? null;
            if (_owner == null) await LoggingAsync("Error getting token owner!");
            return _token;
        }

        /// <summary>
        /// Connect bot framework
        /// </summary>
        /// <returns></returns>
        public async Task ConnectAsync()
        {
            await LoggingAsync("Connecting pubsub...");
            _pubSub.OnLog += _pubSub_OnLog;
            _pubSub.OnPubSubServiceConnected += _pubSub_OnPubSubServiceConnected;
            _pubSub.ListenToChannelPoints(_owner.Id);
            _pubSub.Connect();
            await LoggingAsync("Connecting client...");
            _client.OnLog += _client_OnLog;
            _client.Initialize(new ConnectionCredentials(_owner.Login, _token.AccessToken));
            _client.Connect();
        }

        private void _client_OnLog(object? sender, TwitchLib.Client.Events.OnLogArgs e)
        {
            LoggingAsync(e.Data);
        }
        private void _pubSub_OnLog(object? sender, TwitchLib.PubSub.Events.OnLogArgs e)
        {
            LoggingAsync(e.Data);
        }
        private void _pubSub_OnPubSubServiceConnected(object? sender, EventArgs e)
        {
            _pubSub.SendTopics(_token.AccessToken);
        }

        private async Task GetTokenAsync(string clientId, string secret, List<AuthScopes> scopes)
        {
            await LoggingAsync("Getting token...");
            string redirectUri = "http://localhost:3000";
            string hash = _api.Auth.GetHashCode().ToString();
            var authUrl = _api.Auth.GetAuthorizationCodeUrl(redirectUri, scopes, false, hash, clientId);
            Process.Start(new ProcessStartInfo { UseShellExecute = true, FileName = authUrl });
            var code = await ListenHttpAsync(hash);
            if (string.IsNullOrEmpty(code))
            {
                await LoggingAsync("Failed to retrieve auth code!");
                return;
            }
            var tokenResult = await _api.Auth.GetAccessTokenFromCodeAsync(code, secret, redirectUri, clientId);
            if (tokenResult == null)
            {
                await LoggingAsync("Error getting token!");
                return;
            }
            _token = new Token
            {
                AccessToken = tokenResult.AccessToken,
                Expires = DateTime.UtcNow.AddSeconds(tokenResult.ExpiresIn),
                RefreshToken = tokenResult.RefreshToken,
                Scopes = tokenResult.Scopes,
                TokenType = tokenResult.TokenType
            };
        }

        private async Task RefreshTokenAsync(string clientId, string secret)
        {
            await LoggingAsync("Refresing token...");
            var tokenType = _token.TokenType;
            var res = await _api.Auth.RefreshAuthTokenAsync(_token.RefreshToken, secret, clientId);
            if (res == null)
            {
                await LoggingAsync("Error refreshing token!");
                return;
            }
            _token = new Token
            {
                AccessToken = res.AccessToken,
                Expires = DateTime.UtcNow.AddSeconds(res.ExpiresIn),
                RefreshToken = res.RefreshToken,
                Scopes = res.Scopes,
                TokenType = tokenType
            };
        }

        private async Task<string?> ListenHttpAsync(string state)
        {
            HttpListener listener = new HttpListener();
            listener.Prefixes.Add("http://localhost:3000/");
            listener.Start();
            var context = await listener.GetContextAsync();
            var request = context.Request;
            var response = context.Response;
            string responseString = "<!DOCTYPE html>\r\n<style type=\"text/css\">\r\n    body {\r\n        font: 12px  Helvetica, sans-serif;\r\n        margin: 0;\r\n        overflow-y: auto;\r\n        height: 100%;\r\n    }\r\n    html {\r\n        background-color: #1b1323;\r\n        height:100%;\r\n        margin:0;\r\n        overflow-y: auto;\r\n    }\r\n    .celestial { \r\n        text-align: center;    \r\n        padding-left: 100px;\r\n        padding-right: 100px;\r\n        padding-top: 5px;\r\n        padding-bottom: 5px;\r\n        color: pink;\r\n    }\r\n\t.bold-text {\r\n\t\tfont-weight: bold;\r\n\t}\r\n\r\n    .btn {\r\n        background-color: #360032;\r\n        border: 1px solid #2b4f4f;\r\n        border-radius: 10px;\r\n        color: white;\r\n        padding: 8px 8px;\r\n        text-align: center;\r\n        text-decoration: none;\r\n        display: inline-block;\r\n        font: 18;\r\n        width: 80px;\r\n    }\r\n    .btn-active,\r\n    .btn:hover {\r\n        background-color: #2b2b2b;\r\n    }\r\n    .btn-active:after {\r\n        content: \"\\2212\"\r\n    }\r\n</style>\r\n<body>\r\n    <div class=\"celestial\">\r\n        <p style=\"font-size:30px;\" class=\"bold-text\">Authorization completed!</p>\r\n\t\t<p>You can close this window.</p>\r\n        <button class=\"btn\" onclick=\"self.close()\">Close</button>\r\n    </div>\r\n</body>"; //"<html><body>Authorization completed. You can close this window.</body></html>";
            if (File.Exists(_resFile))
                responseString = File.ReadAllText(_resFile);
            byte[] buffer = System.Text.Encoding.UTF8.GetBytes(responseString);
            response.ContentLength64 = buffer.Length;
            await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            response.OutputStream.Close();
            listener.Stop();
            string? requestState = request.QueryString["state"];
            string? code = request.QueryString["code"];
            if (requestState != state)
            {
                await LoggingAsync("Invalid state parameter.");
                return string.Empty;
            }
            return code;
        }

        /// <summary>
        /// Logging output
        /// </summary>
        /// <param name="msg">Log message</param>
        /// <returns></returns>
        protected abstract Task LoggingAsync(string msg);
    }

    public class Token
    {
        public string AccessToken { get; set; }
        public DateTime Expires { get; set; }
        public string RefreshToken { get; set; }
        public string[] Scopes { get; set; }
        public string TokenType { get; set; }
    }
}