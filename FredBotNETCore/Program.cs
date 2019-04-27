﻿using Discord;
using Discord.Commands;
using Discord.WebSocket;
using FredBotNETCore.Services;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Victoria;

namespace FredBotNETCore
{
    public class Program
    {

        #region Fields

        private DiscordSocketClient _client;
        private CommandHandler _commands;
        private Lavalink _lavaLink;
        public static ServiceProvider _provider;
        private bool running = false;
        private bool retryConnection = false;
        private readonly string downloadPath = Path.Combine(Directory.GetCurrentDirectory(), "TextFiles");

        #endregion

        #region Startup

        // Convert sync main to an async main.
#pragma warning disable IDE0060 // Remove unused parameter
        public static void Main(string[] args)
#pragma warning restore IDE0060 // Remove unused parameter
        {
            new Program().Start().GetAwaiter().GetResult();
        }

        public async Task Start()
        {
            if (_client != null)
            {
                if (_client.ConnectionState == ConnectionState.Connecting ||
                _client.ConnectionState == ConnectionState.Connected)
                {
                    return;
                }
            }

            _client = new DiscordSocketClient(new DiscordSocketConfig
            {
                MessageCacheSize = 100,
                LogLevel = LogSeverity.Verbose,
            });
            _provider = ConfigureServices();
            _client.Log += Log;
            _lavaLink = _provider.GetRequiredService<Lavalink>();
            _lavaLink.Log += Log;
            _commands = new CommandHandler();
            running = false;
            retryConnection = true;

            while (true)
            {
                try
                {
                    await _client.LoginAsync(tokenType: TokenType.Bot, token: new StreamReader(path: Path.Combine(downloadPath, "Token.txt")).ReadLine());
                    await _client.StartAsync();

                    Task.WaitAny(Task.Factory.StartNew(() => CheckStatus(_client)), Task.Factory.StartNew(() => GameLoop()), Task.Factory.StartNew(async () => await _commands.Install(_client, _lavaLink, _provider)));

                    running = true;

                    break;
                }
                catch
                {
                    await Log(new LogMessage(LogSeverity.Error, "RunAsync", "Failed to connect."));
                    if (retryConnection == false)
                    {
                        return;
                    }
                    await Task.Delay(1000);
                }
            }

            while (running) { await Task.Delay(1000); }

            if (_client.ConnectionState == ConnectionState.Connecting ||
                _client.ConnectionState == ConnectionState.Connected)
            {
                try { _client.StopAsync().Wait(); }
                catch { }
            }
        }

        private ServiceProvider ConfigureServices()
        {
            return new ServiceCollection()
                .AddSingleton(_client)
                .AddSingleton(new CommandService(new CommandServiceConfig { CaseSensitiveCommands = false, DefaultRunMode = RunMode.Async }))
                .AddSingleton<Lavalink>()
                .AddSingleton<AudioService>()
                .BuildServiceProvider();

        }

        #endregion

        #region Timer Loop

        public static async Task CheckStatus(DiscordSocketClient client)
        {
            HttpClient web = new HttpClient();
            string hint = Extensions.GetBetween(await web.GetStringAsync("https://pr2hub.com/files/artifact_hint.txt"), "{\"hint\":\"", "\",\"finder_name\":\"");
            string finder = Extensions.GetBetween(await web.GetStringAsync("https://pr2hub.com/files/artifact_hint.txt"), "\",\"finder_name\":\"", "\",\"bubbles_name\":\"");
            string bubbles = Extensions.GetBetween(await web.GetStringAsync("https://pr2hub.com/files/artifact_hint.txt"), "\",\"bubbles_name\":\"", "\",\"updated_time\":");
            string time = Extensions.GetBetween(await web.GetStringAsync("https://pr2hub.com/files/artifact_hint.txt"), "\",\"updated_time\":", "}");
            bool valid = false;
            while (true)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(10));
                    #region HH
                    string status = await web.GetStringAsync("https://pr2hub.com/files/server_status_2.txt");
                    string[] servers = status.Split('}');
                    string happyHour = "", guildId = "";

                    foreach (string server_name in servers)
                    {
                        guildId = Extensions.GetBetween(server_name, "guild_id\":\"", "\"");
                        if (guildId.Equals("0"))
                        {
                            happyHour = Extensions.GetBetween(server_name, "happy_hour\":\"", "\"");
                            string serverName = Extensions.GetBetween(server_name, "server_name\":\"", "\"");
                            if (!serverName.Equals("Tournament"))
                            {
                                if (happyHour.Equals("1"))
                                {
                                    await CommandHandler.CheckStatusAsync(true, serverName);
                                }
                                else
                                {
                                    await CommandHandler.CheckStatusAsync(false, serverName);
                                }
                            }
                        }
                    }

                    #endregion

                    #region Arti
                    string artifactHint = await web.GetStringAsync("https://pr2hub.com/files/artifact_hint.txt");
                    if (!hint.Equals(Extensions.GetBetween(artifactHint, "{\"hint\":\"", "\",\"finder_name\":\"")))
                    {
                        hint = Extensions.GetBetween(artifactHint, "{\"hint\":\"", "\",\"finder_name\":\"");
                        if (!time.Equals(Extensions.GetBetween(artifactHint, "\",\"updated_time\":", "}")))
                        {
                            time = Extensions.GetBetween(artifactHint, "\",\"updated_time\":", "}");
                            valid = true;
                        }
                        if (valid)
                        {
                            await CommandHandler.AnnouceHintUpdatedAsync(hint, true);
                            valid = false;
                        }
                        else
                        {
                            await CommandHandler.AnnouceHintUpdatedAsync(hint, false);
                        }
                    }
                    if (!finder.Equals(Extensions.GetBetween(artifactHint, "\",\"finder_name\":\"", "\",\"bubbles_name\":\"")))
                    {
                        finder = Extensions.GetBetween(artifactHint, "\",\"finder_name\":\"", "\",\"bubbles_name\":\"");
                        bubbles = Extensions.GetBetween(artifactHint, "\",\"bubbles_name\":\"", "\",\"updated_time\":");
                        if (finder.Length > 0 && finder == bubbles)
                        {
                            await CommandHandler.AnnounceArtifactFoundAsync(finder, true);
                        }
                        else if (finder.Length > 0)
                        {
                            await CommandHandler.AnnounceArtifactFoundAsync(finder);
                        }
                    }
                    if (!bubbles.Equals(Extensions.GetBetween(artifactHint, "\",\"bubbles_name\":\"", "\",\"updated_time\":")))
                    {
                        bubbles = Extensions.GetBetween(artifactHint, "\",\"bubbles_name\":\"", "\",\"updated_time\":");
                        if (bubbles.Length > 0)
                        {
                            await CommandHandler.AnnounceBubblesAwardedAsync(bubbles);
                        }
                    }
                    #endregion
                }
                catch (HttpRequestException)
                {
                    //failed to connect
                }
                catch (Exception e)
                {
                    await Extensions.LogError(client, e.Message + e.StackTrace);
                }
            }
        }

        public async Task GameLoop()
        {
            while (true)
            {
                await Task.Delay(new Random().Next(300000, 600000));
                Process process = Process.GetCurrentProcess();
                TimeSpan time = DateTime.Now - process.StartTime;
                StringBuilder sb = new StringBuilder();
                if (time.Days > 0)
                {
                    sb.Append($"{time.Days}d ");  /*Pulls the Uptime in Days*/
                }
                if (time.Hours > 0)
                {
                    sb.Append($"{time.Hours}h ");  /*Pulls the Uptime in Hours*/
                }
                if (time.Minutes > 0)
                {
                    sb.Append($"{time.Minutes}m ");  /*Pulls the Uptime in Minutes*/
                }
                sb.Append($"{time.Seconds}s ");  /*Pulls the Uptime in Seconds*/
                await _client.SetGameAsync($"/help for {sb.ToString()}", null, type: ActivityType.Playing);
                await Task.Delay(new Random().Next(300000, 600000));
                await _client.SetGameAsync($"/help in {_client.Guilds.Count} servers", null, type: ActivityType.Watching);
                await Task.Delay(new Random().Next(300000, 600000));
                await _client.DownloadUsersAsync(_client.Guilds);
                int users = _client.Guilds.Sum(g => g.Users.Count);
                await _client.SetGameAsync($"/help with {users} users", null, type: ActivityType.Listening);
            }
        }

        #endregion

        #region Log

        private Task Log(LogMessage msg)
        {
            ConsoleColor log = Console.ForegroundColor;
            switch (msg.Severity)
            {
                case LogSeverity.Critical:
                    Console.ForegroundColor = ConsoleColor.Red;
                    break;
                case LogSeverity.Error:
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    break;
                case LogSeverity.Warning:
                    Console.ForegroundColor = ConsoleColor.Green;
                    break;
                case LogSeverity.Info:
                    Console.ForegroundColor = ConsoleColor.Blue;
                    break;
                case LogSeverity.Verbose:
                    Console.ForegroundColor = ConsoleColor.DarkMagenta;
                    break;
                case LogSeverity.Debug:
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    break;
            }

            Console.WriteLine($"{DateTime.Now,-19} [{msg.Severity,8}] {msg.Source}: {msg.Message}");
            Console.ForegroundColor = log;

            return Task.CompletedTask;
        }

#pragma warning disable IDE0060 // Remove unused parameter
        internal static Task Start(object workingDirectly, object friendlyName)
#pragma warning restore IDE0060 // Remove unused parameter
        {
            throw new NotImplementedException();
        }

#pragma warning disable IDE0060 // Remove unused parameter
        internal static Task Start(string v)
#pragma warning restore IDE0060 // Remove unused parameter
        {
            throw new NotImplementedException();
        }

        #endregion

    }
}
