﻿using Discord;
using Discord.Commands;
using Discord.WebSocket;
using FredBotNETCore.Services;
using Google.Apis.Services;
using Google.Apis.YouTube.v3;
using Google.Apis.YouTube.v3.Data;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Victoria.Entities;

namespace FredBotNETCore.Modules.Public
{
    [Name("Audio")]
    [Summary("Module containing all of the music commands.")]
    public class AudioModule : ModuleBase<SocketCommandContext>
    {
        private static readonly string downloadPath = Path.Combine(Directory.GetCurrentDirectory(), "TextFiles");
        private readonly YouTubeService youtubeService = new YouTubeService(new BaseClientService.Initializer()
        {
            ApiKey = new StreamReader(path: Path.Combine(downloadPath, "YoutubeApiKey.txt")).ReadLine(),
            ApplicationName = "Fred bot"
        });
        private readonly AudioService audioService = new AudioService();

        private static bool Blacklisted(SocketUser user)
        {
            if (File.ReadAllText(path: Path.Combine(downloadPath, "BlacklistedMusic.txt")).Contains(user.Id.ToString()))
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        [Command("add", RunMode = RunMode.Async)]
        [Alias("addsong")]
        [Summary("Adds a song to play.")]
        [RequireContext(ContextType.Guild)]
        public async Task Add([Remainder] string url = null)
        {
            if (Context.Channel.Id == 528696379325808655 || Context.Channel.Id == 528692074917134346)
            {
                if (Blacklisted(Context.User))
                {
                    return;
                }
                if (string.IsNullOrWhiteSpace(url))
                {
                    EmbedBuilder embed = new EmbedBuilder()
                    {
                        Title = "Command: /add",
                        Description = "**Description:** Add a song to the music queue.\n**Usage:** /add [url]\n**Example:** /add https://www.youtube.com/watch?v=ifFNeqzB5os",
                        Color = new Color(220, 220, 220)
                    };
                    await Context.Channel.SendMessageAsync("", false, embed.Build());
                }
                else
                {
                    SocketVoiceChannel _voiceChannel = (Context.User as SocketGuildUser).VoiceChannel;
                    if (_voiceChannel == null || _voiceChannel.Id != 528688237812908057)
                    {
                        await Context.Channel.SendMessageAsync($"{Context.User.Mention} you need to be in the Music voice channel to use this command.");
                    }
                    else
                    {
                        bool result = Uri.TryCreate(url, UriKind.Absolute, out Uri uriResult)
                                  && (uriResult.Scheme == "http" || uriResult.Scheme == "https");
                        if (Context.Guild.CurrentUser.VoiceChannel == null)
                        {
                            await audioService.Connect((Context.User as SocketGuildUser).VoiceChannel, Context.Channel);
                        }
                        if (audioService.Queue() != null && audioService.Queue().Items.Count() >= 20)
                        {
                            await Context.Channel.SendMessageAsync($"{Context.User.Mention} the queue is full.");
                        }
                        else if (result)
                        {
                            if (url.Contains("&list="))
                            {
                                url = url.Split("&list=").First();
                            }
                            WebClient myDownloader = new WebClient
                            {
                                Encoding = System.Text.Encoding.UTF8
                            };
                            string[] urlS = url.Split("watch?v=");
                            string jsonResponse = myDownloader.DownloadString(
                            "https://www.googleapis.com/youtube/v3/videos?part=snippet&id=" + urlS[1] + "&key="
                            + File.ReadAllText(Path.Combine(downloadPath, "YoutubeApiKey.txt")));
                            string title = Extensions.GetBetween(jsonResponse, "\"title\": \"", "\",");
                            LavaTrack track = await audioService.GetTrack(title);
                            if (track.Length.Minutes > 10 || (track.Length.Minutes == 10 && track.Length.Seconds > 0))
                            {
                                await Context.Channel.SendMessageAsync($"{Context.User.Mention} the maximum song length is 10 minutes.");
                            }
                            else if (audioService.Queue().Items.Any(x => x.Uri.Equals(track.Uri)))
                            {
                                await Context.Channel.SendMessageAsync($"{Context.User.Mention} that video is already in the queue.");
                            }
                            else
                            {
                                SearchResource.ListRequest searchListRequest = youtubeService.Search.List("snippet");
                                searchListRequest.Q = title;
                                searchListRequest.MaxResults = 1;
                                searchListRequest.Type = "video";
                                SearchListResponse searchListResponse = await searchListRequest.ExecuteAsync();
                                string channel = "";
                                ThumbnailDetails thumbnails = null;
                                foreach (Google.Apis.YouTube.v3.Data.SearchResult searchResult in searchListResponse.Items)
                                {
                                    switch (searchResult.Id.Kind)
                                    {
                                        case "youtube#video":
                                            channel = searchResult.Snippet.ChannelTitle;
                                            thumbnails = searchResult.Snippet.Thumbnails;
                                            break;
                                    }
                                }
                                EmbedBuilder embed = new EmbedBuilder()
                                {
                                    Color = new Color(Extensions.random.Next(256), Extensions.random.Next(256), Extensions.random.Next(256)),
                                    Author = new EmbedAuthorBuilder()
                                    {
                                        Name = "Add song",
                                        Url = url
                                    },
                                    Fields = new List<EmbedFieldBuilder>
                                        {
                                            new EmbedFieldBuilder
                                            {
                                                Name = "Song",
                                                Value = Format.Sanitize(title),
                                                IsInline = false
                                            },
                                            new EmbedFieldBuilder
                                            {
                                                Name = "Duration",
                                                Value = track.Length.Minutes + ":" + track.Length.Seconds,
                                                IsInline = false
                                            },
                                            new EmbedFieldBuilder
                                            {
                                                Name = "Channel",
                                                Value = Format.Sanitize(channel),
                                                IsInline = false
                                            }
                                        },
                                    Footer = new EmbedFooterBuilder()
                                    {
                                        IconUrl = Context.User.GetAvatarUrl(),
                                        Text = $"Queued by {Context.User.Username}#{Context.User.Discriminator}"
                                    },
                                    ThumbnailUrl = thumbnails.High.Url
                                };
                                embed.WithCurrentTimestamp();
                                await Context.Channel.SendMessageAsync("", false, embed.Build());
                                if (audioService.Playing())
                                {
                                    audioService.QueueAdd(track);
                                }
                                else
                                {
                                    await audioService.Play(track);
                                }
                            }
                        }
                        else
                        {
                            SearchResource.ListRequest searchListRequest = youtubeService.Search.List("snippet");
                            searchListRequest.Q = url;
                            searchListRequest.MaxResults = 1;
                            searchListRequest.Type = "video";
                            SearchListResponse searchListResponse = await searchListRequest.ExecuteAsync();
                            string channel = "";
                            ThumbnailDetails thumbnails = null;
                            foreach (Google.Apis.YouTube.v3.Data.SearchResult searchResult in searchListResponse.Items)
                            {
                                switch (searchResult.Id.Kind)
                                {
                                    case "youtube#video":
                                        channel = searchResult.Snippet.ChannelTitle;
                                        thumbnails = searchResult.Snippet.Thumbnails;
                                        break;
                                }
                            }
                            LavaTrack track = await audioService.GetTrack(url);
                            if (track.Length.Minutes > 10 || (track.Length.Minutes == 10 && track.Length.Seconds > 0))
                            {
                                await Context.Channel.SendMessageAsync($"{Context.User.Mention} the maximum song length is 10 minutes.");
                            }
                            else if (audioService.Queue().Items.Any(x => x.Uri.Equals(track.Uri)))
                            {
                                await Context.Channel.SendMessageAsync($"{Context.User.Mention} that video is already in the queue.");
                            }
                            else
                            {
                                EmbedBuilder embed = new EmbedBuilder()
                                {
                                    Color = new Color(Extensions.random.Next(256), Extensions.random.Next(256), Extensions.random.Next(256)),
                                    Author = new EmbedAuthorBuilder()
                                    {
                                        Name = "Add song",
                                        Url = url
                                    },
                                    Fields = new List<EmbedFieldBuilder>
                                        {
                                            new EmbedFieldBuilder
                                            {
                                                Name = "Song",
                                                Value = Format.Sanitize(track.Title),
                                                IsInline = false
                                            },
                                            new EmbedFieldBuilder
                                            {
                                                Name = "Duration",
                                                Value = track.Length.Minutes + ":" + track.Length.Seconds,
                                                IsInline = false
                                            },
                                            new EmbedFieldBuilder
                                            {
                                                Name = "Channel",
                                                Value = Format.Sanitize(channel),
                                                IsInline = false
                                            }
                                        },
                                    Footer = new EmbedFooterBuilder()
                                    {
                                        IconUrl = Context.User.GetAvatarUrl(),
                                        Text = $"Queued by {Context.User.Username}#{Context.User.Discriminator}"
                                    },
                                    ThumbnailUrl = thumbnails.High.Url
                                };
                                embed.WithCurrentTimestamp();
                                await Context.Channel.SendMessageAsync("", false, embed.Build());
                                if (audioService.Playing())
                                {
                                    audioService.QueueAdd(track);
                                }
                                else
                                {
                                    await audioService.Play(track);
                                }
                            }
                        }
                    }
                }
            }
            else
            {
                return;
            }
        }

        //[Command("queue", RunMode = RunMode.Async)]
        //[Alias("q")]
        //[Summary("Displays song queue.")]
        //[RequireContext(ContextType.Guild)]
        //public async Task ShowQueue()
        //{
        //    if (Context.Channel.Id == 528696379325808655 || Context.Channel.Id == 528692074917134346)
        //    {
        //        if (Blacklisted(Context.User))
        //        {
        //            return;
        //        }
        //        _voiceChannel = (Context.User as IGuildUser).VoiceChannel;
        //        if (_voiceChannel == null)
        //        {
        //            await Context.Channel.SendMessageAsync($"{Context.User.Mention} you need to be in a voice channel to use this command.");
        //        }
        //        else if (_voiceChannel.Id != 528688237812908057)
        //        {
        //            await Context.Channel.SendMessageAsync($"{Context.User.Mention} you need to be in the Music voice channel to use this command.");
        //        }
        //        else
        //        {
        //            await SendQueue(Context.Channel);
        //        }
        //    }
        //    else
        //    {
        //        return;
        //    }
        //}

        //[Command("loop", RunMode = RunMode.Async)]
        //[Alias("repeat", "queueloop", "loopqueue", "qloop", "loopq")]
        //[Summary("Toggles looping of the queue.")]
        //[RequireUserPermission(GuildPermission.KickMembers)]
        //[RequireContext(ContextType.Guild)]
        //public async Task QueueLoop()
        //{
        //    if (Context.Channel.Id == 528696379325808655 || Context.Channel.Id == 528692074917134346)
        //    {
        //        if (Blacklisted(Context.User))
        //        {
        //            return;
        //        }
        //        _voiceChannel = (Context.User as IGuildUser).VoiceChannel;
        //        if (_voiceChannel == null)
        //        {
        //            await Context.Channel.SendMessageAsync($"{Context.User.Mention} you need to be in a voice channel to use this command.");
        //        }
        //        else if (_voiceChannel.Id != 528688237812908057)
        //        {
        //            await Context.Channel.SendMessageAsync($"{Context.User.Mention} you need to be in the Music voice channel to use this command.");
        //        }
        //        else
        //        {
        //            if (Loop)
        //            {
        //                Loop = false;
        //                await Context.Channel.SendMessageAsync($"{Context.User.Mention} the queue will no longer loop.");
        //            }
        //            else
        //            {
        //                Loop = true;
        //                await Context.Channel.SendMessageAsync($"{Context.User.Mention} the queue will now loop.");
        //            }
        //        }
        //    }
        //}

        //[Command("pause", RunMode = RunMode.Async)]
        //[Alias("p", "pausemusic")]
        //[Summary("pauses the music")]
        //[RequireUserPermission(GuildPermission.KickMembers)]
        //[RequireContext(ContextType.Guild)]
        //public async Task PauseMusic()
        //{
        //    if (Context.Channel.Id == 528696379325808655 || Context.Channel.Id == 528692074917134346)
        //    {
        //        if (Blacklisted(Context.User))
        //        {
        //            return;
        //        }
        //        _voiceChannel = (Context.User as IGuildUser).VoiceChannel;
        //        if (_voiceChannel == null)
        //        {
        //            await Context.Channel.SendMessageAsync($"{Context.User.Mention} you need to be in a voice channel to use this command.");
        //        }
        //        else if (_voiceChannel.Id != 528688237812908057)
        //        {
        //            await Context.Channel.SendMessageAsync($"{Context.User.Mention} you need to be in the Music voice channel to use this command.");
        //        }
        //        else
        //        {
        //            if (Pause)
        //            {
        //                await Context.Channel.SendMessageAsync($"{Context.User.Mention} the music is already paused.");
        //            }
        //            else
        //            {
        //                Pause = true;
        //                Playing = false;
        //                await Context.Channel.SendMessageAsync($"{Context.User.Mention} paused the music.");
        //            }
        //        }
        //    }
        //    else
        //    {
        //        return;
        //    }
        //}

        //[Command("resume", RunMode = RunMode.Async)]
        //[Summary("Resumes play of music or adds another song.")]
        //[RequireUserPermission(GuildPermission.KickMembers)]
        //[RequireContext(ContextType.Guild)]
        //public async Task Resume()
        //{
        //    if (Context.Channel.Id == 528696379325808655 || Context.Channel.Id == 528692074917134346)
        //    {
        //        if (Blacklisted(Context.User))
        //        {
        //            return;
        //        }
        //        _voiceChannel = (Context.User as IGuildUser).VoiceChannel;
        //        if (_voiceChannel == null)
        //        {
        //            await Context.Channel.SendMessageAsync($"{Context.User.Mention} you need to be in a voice channel to use this command.");
        //        }
        //        else if (_voiceChannel.Id != 528688237812908057)
        //        {
        //            await Context.Channel.SendMessageAsync($"{Context.User.Mention} you need to be in the Music voice channel to use this command.");
        //        }
        //        else
        //        {
        //            if (!Playing)
        //            {
        //                Pause = false;
        //                Playing = true;
        //                await Context.Channel.SendMessageAsync($"{Context.User.Mention} resumed the music.");
        //            }
        //        }
        //    }
        //}

        //[Command("play", RunMode = RunMode.Async)]
        //[Alias("playmusic")]
        //[Summary("Resumes play of music or adds another song.")]
        //[RequireContext(ContextType.Guild)]
        //public async Task Play([Remainder] string url = null)
        //{
        //    if (Context.Channel.Id == 528696379325808655 || Context.Channel.Id == 528692074917134346)
        //    {
        //        if (Blacklisted(Context.User))
        //        {
        //            return;
        //        }
        //        if (url == null)
        //        {
        //            SocketGuildUser user = Context.User as SocketGuildUser;
        //            if (user.Roles.Any(e => e.Name.ToUpperInvariant() == "Discord Staff".ToUpperInvariant()) && !Playing)
        //            {
        //                Pause = false;
        //                Playing = true;
        //                await Context.Channel.SendMessageAsync($"{Context.User.Mention} resumed the music.");
        //            }
        //            else
        //            {
        //                await Add(url);
        //            }
        //        }
        //        else
        //        {
        //            await Add(url);
        //        }
        //    }
        //    else
        //    {
        //        return;
        //    }
        //}

        //[Command("qremove", RunMode = RunMode.Async)]
        //[Alias("queueremove")]
        //[Summary("Remove an item from the queue.")]
        //[RequireUserPermission(GuildPermission.KickMembers)]
        //[RequireContext(ContextType.Guild)]
        //public async Task QueueRemove(string position = null)
        //{
        //    if (Context.Channel.Id == 528696379325808655 || Context.Channel.Id == 528692074917134346)
        //    {
        //        if (Blacklisted(Context.User))
        //        {
        //            return;
        //        }
        //        if (string.IsNullOrWhiteSpace(position) || !int.TryParse(position, out int pos) || pos < 1)
        //        {
        //            EmbedBuilder embed = new EmbedBuilder()
        //            {
        //                Title = "Command: /qremove",
        //                Description = "**Description:** Remove a song from the queue.\n**Usage:** /qremove [position]\n**Example:** /qremove 1",
        //                Color = new Color(220, 220, 220)
        //            };
        //            await Context.Channel.SendMessageAsync("", false, embed.Build());
        //        }
        //        else
        //        {
        //            if (Queue.Count <= 0)
        //            {
        //                await Context.Channel.SendMessageAsync($"{Context.User.Mention} there is nothing in the queue.");
        //            }
        //            else if (Queue.Count < pos)
        //            {
        //                await Context.Channel.SendMessageAsync($"{Context.User.Mention} there is not that many items in the queue.");
        //            }
        //            else
        //            {
        //                Tuple<string, string, string, string, string, string, string> item = Queue.ElementAt(pos - 1);
        //                Queue = new Queue<Tuple<string, string, string, string, string, string, string>>(Queue.Where(s => s != item));
        //                await Context.Channel.SendMessageAsync($"{Context.User.Mention} removed **{Format.Sanitize(item.Item2)}** from the queue.");
        //            }
        //        }
        //    }
        //    else
        //    {
        //        return;
        //    }
        //}

        //[Command("qclear", RunMode = RunMode.Async)]
        //[Alias("clearqueue", "clearq")]
        //[Summary("Removes all songs from the queue")]
        //[RequireUserPermission(GuildPermission.KickMembers)]
        //[RequireContext(ContextType.Guild)]
        //public async Task QueueClear()
        //{
        //    if (Context.Channel.Id == 528696379325808655 || Context.Channel.Id == 528692074917134346)
        //    {
        //        if (Blacklisted(Context.User))
        //        {
        //            return;
        //        }
        //        _voiceChannel = (Context.User as IGuildUser).VoiceChannel;
        //        if (_voiceChannel == null)
        //        {
        //            await Context.Channel.SendMessageAsync($"{Context.User.Mention} you need to be in a voice channel to use this command.");
        //        }
        //        else if (_voiceChannel.Id != 528688237812908057)
        //        {
        //            await Context.Channel.SendMessageAsync($"{Context.User.Mention} you need to be in the Music voice channel to use this command.");
        //        }
        //        else
        //        {
        //            if (Queue.Count <= 0)
        //            {
        //                await Context.Channel.SendMessageAsync($"{Context.User.Mention} the queue is already empty.");
        //            }
        //            else
        //            {
        //                Queue.Clear();
        //                await Context.Channel.SendMessageAsync($"{Context.User.Mention} cleared the queue.");
        //            }
        //        }
        //    }
        //    else
        //    {
        //        return;
        //    }
        //}

        //[Command("come", RunMode = RunMode.Async)]
        //[Alias("summon")]
        //[Summary("Brings bot to voice channel")]
        //[RequireUserPermission(GuildPermission.KickMembers)]
        //[RequireContext(ContextType.Guild)]
        //public async Task Come()
        //{
        //    if (Context.Channel.Id == 528696379325808655 || Context.Channel.Id == 528692074917134346)
        //    {
        //        if (Blacklisted(Context.User))
        //        {
        //            return;
        //        }
        //        _voiceChannel = (Context.User as IGuildUser).VoiceChannel;
        //        if (_voiceChannel == null)
        //        {
        //            await Context.Channel.SendMessageAsync($"{Context.User.Mention} you need to be in a voice channel to use this command.");
        //        }
        //        else if (_voiceChannel.Id != 528688237812908057)
        //        {
        //            await Context.Channel.SendMessageAsync($"{Context.User.Mention} you need to be in the Music voice channel to use this command.");
        //        }
        //        else if (Context.Guild.CurrentUser.VoiceChannel != null && Context.Guild.CurrentUser.VoiceChannel.Id == 528688237812908057)
        //        {
        //            await Context.Channel.SendMessageAsync($"{Context.User.Mention} I am already in the Music voice channel.");
        //        }
        //        else
        //        {
        //            Audio?.Dispose();
        //            await Context.Channel.SendMessageAsync($"Joined voice channel **{Format.Sanitize(_voiceChannel.Name)}**.");
        //            Audio = await _voiceChannel.ConnectAsync();
        //            Discord = Audio.CreatePCMStream(AudioApplication.Mixed, 64000);
        //            return;
        //        }
        //    }
        //    else
        //    {
        //        return;
        //    }
        //}

        //[Command("skip", RunMode = RunMode.Async)]
        //[Alias("skipsong")]
        //[Summary("Votes to skip current song")]
        //[RequireContext(ContextType.Guild)]
        //public async Task SkipSong()
        //{
        //    if (Context.Channel.Id == 528696379325808655 || Context.Channel.Id == 528692074917134346)
        //    {
        //        if (Blacklisted(Context.User))
        //        {
        //            return;
        //        }
        //        _voiceChannel = (Context.User as IGuildUser).VoiceChannel;
        //        if (_voiceChannel == null)
        //        {
        //            await Context.Channel.SendMessageAsync($"{Context.User.Mention} you need to be in a voice channel to use this command.");
        //        }
        //        else if (_voiceChannel.Id != 528688237812908057)
        //        {
        //            await Context.Channel.SendMessageAsync($"{Context.User.Mention} you need to be in the Music voice channel to use this command.");
        //        }
        //        else
        //        {
        //            if (NowPlaying.Count <= 0)
        //            {
        //                await Context.Channel.SendMessageAsync($"{Context.User.Mention} there is nothing to skip.");
        //            }
        //            else
        //            {
        //                int users = await _voiceChannel.GetUsersAsync().Count();
        //                int votesNeeded = Convert.ToInt32(Math.Round(Convert.ToDouble((await _voiceChannel.GetUsersAsync().Count()) / 3)));
        //                if (votesNeeded < 1)
        //                {
        //                    votesNeeded = 1;
        //                }
        //                SkipCount++;
        //                if (votesNeeded - SkipCount == 0)
        //                {
        //                    Skip = true;
        //                    Pause = false;
        //                    await Context.Channel.SendMessageAsync($"**{Format.Sanitize(NowPlaying.Peek().Item2)}** requested by **{Format.Sanitize(Context.Guild.GetUser(Convert.ToUInt64(NowPlaying.Peek().Item4)).Username)}#{Context.Guild.GetUser(Convert.ToUInt64(NowPlaying.Peek().Item4)).Discriminator}** was skipped.");
        //                }
        //                else
        //                {
        //                    if (votesNeeded - SkipCount == 1)
        //                    {
        //                        await Context.Channel.SendMessageAsync($"{Context.User.Mention} voted to skip **{Format.Sanitize(NowPlaying.Peek().Item2)}**. {votesNeeded - SkipCount} more vote needed to skip.");
        //                    }
        //                    else
        //                    {
        //                        await Context.Channel.SendMessageAsync($"{Context.User.Mention} voted to skip **{Format.Sanitize(NowPlaying.Peek().Item2)}**. {votesNeeded - SkipCount} more votes needed to skip.");
        //                    }
        //                }
        //            }
        //        }
        //    }
        //    else
        //    {
        //        return;
        //    }
        //}

        //[Command("forceskip", RunMode = RunMode.Async)]
        //[Alias("fskip", "forceskipsong")]
        //[Summary("Skips the current song.")]
        //[RequireUserPermission(GuildPermission.KickMembers)]
        //[RequireContext(ContextType.Guild)]
        //public async Task ForceSkip()
        //{
        //    if (Context.Channel.Id == 528696379325808655 || Context.Channel.Id == 528692074917134346)
        //    {
        //        if (Blacklisted(Context.User))
        //        {
        //            return;
        //        }
        //        _voiceChannel = (Context.User as IGuildUser).VoiceChannel;
        //        if (_voiceChannel == null)
        //        {
        //            await Context.Channel.SendMessageAsync($"{Context.User.Mention} you need to be in a voice channel to use this command.");
        //        }
        //        else if (_voiceChannel.Id != 528688237812908057)
        //        {
        //            await Context.Channel.SendMessageAsync($"{Context.User.Mention} you need to be in the Music voice channel to use this command.");
        //        }
        //        else
        //        {
        //            if (NowPlaying.Count <= 0)
        //            {
        //                await Context.Channel.SendMessageAsync($"{Context.User.Mention} there is nothing to skip.");
        //            }
        //            else
        //            {
        //                Skip = true;
        //                Pause = false;
        //                await Context.Channel.SendMessageAsync($"{Context.User.Mention} force skipped **{Format.Sanitize(NowPlaying.Peek().Item2)}** requested by **{Format.Sanitize(Context.Guild.GetUser(Convert.ToUInt64(NowPlaying.Peek().Item4)).Username)}#{Context.Guild.GetUser(Convert.ToUInt64(NowPlaying.Peek().Item4)).Discriminator}**.");
        //            }
        //        }
        //    }
        //    else
        //    {
        //        return;
        //    }
        //}

        //[Command("np", RunMode = RunMode.Async)]
        //[Alias("nowplaying")]
        //[Summary("Displays current song playing.")]
        //[RequireContext(ContextType.Guild)]
        //public async Task NP()
        //{
        //    if (Context.Channel.Id == 528696379325808655 || Context.Channel.Id == 528692074917134346)
        //    {
        //        if (Blacklisted(Context.User))
        //        {
        //            return;
        //        }
        //        _voiceChannel = (Context.User as IGuildUser).VoiceChannel;
        //        if (_voiceChannel == null)
        //        {
        //            await Context.Channel.SendMessageAsync($"{Context.User.Mention} you need to be in a voice channel to use this command.");
        //        }
        //        else if (_voiceChannel.Id != 528688237812908057)
        //        {
        //            await Context.Channel.SendMessageAsync($"{Context.User.Mention} you need to be in the Music voice channel to use this command.");
        //        }
        //        else
        //        {
        //            if (NowPlaying.Count <= 0)
        //            {
        //                await Context.Channel.SendMessageAsync($"{Context.User.Mention} nothing is playing right now.");
        //            }
        //            else
        //            {
        //                EmbedBuilder embed = new EmbedBuilder()
        //                {
        //                    Color = new Color(Extensions.random.Next(256), Extensions.random.Next(256), Extensions.random.Next(256)),
        //                    Author = new EmbedAuthorBuilder()
        //                    {
        //                        Name = "Now playing",
        //                        Url = NowPlaying.Peek().Item7
        //                    },
        //                    Fields = new List<EmbedFieldBuilder>
        //                        {
        //                        new EmbedFieldBuilder
        //                        {
        //                            Name = "Song",
        //                            Value = Format.Sanitize(NowPlaying.Peek().Item2),
        //                            IsInline = false
        //                        },
        //                        new EmbedFieldBuilder
        //                        {
        //                            Name = "Duration",
        //                            Value = NowPlaying.Peek().Item3,
        //                            IsInline = false
        //                        },
        //                        new EmbedFieldBuilder
        //                        {
        //                            Name = "Channel",
        //                            Value = Format.Sanitize(NowPlaying.Peek().Item5),
        //                            IsInline = false
        //                        }
        //                        },
        //                    Footer = new EmbedFooterBuilder()
        //                    {
        //                        IconUrl = Context.User.GetAvatarUrl(),
        //                        Text = $"Queued by {Context.Guild.GetUser(Convert.ToUInt64(NowPlaying.Peek().Item4)).Username}#{Context.Guild.GetUser(Convert.ToUInt64(NowPlaying.Peek().Item4)).Discriminator}"
        //                    },
        //                    ThumbnailUrl = NowPlaying.Peek().Item6
        //                };
        //                embed.WithCurrentTimestamp();
        //                await Context.Channel.SendMessageAsync("", false, embed.Build());
        //            }
        //        }
        //    }
        //    else
        //    {
        //        return;
        //    }
        //}

        //[Command("voiceping")]
        //[Alias("voicelatency")]
        //[Summary("Gets bot voice latency.")]
        //[RequireUserPermission(GuildPermission.KickMembers)]
        //[RequireContext(ContextType.Guild)]
        //public async Task VoiceLatency()
        //{
        //    if (Context.Channel.Id == 528696379325808655 || Context.Channel.Id == 528692074917134346)
        //    {
        //        if (Blacklisted(Context.User))
        //        {
        //            return;
        //        }
        //        if (Audio != null)
        //        {
        //            await Context.Channel.SendMessageAsync($"Websocket Latency: **{Audio.Latency}** ms\nUDP Latency: **{Audio.UdpLatency}** ms");
        //        }
        //        else
        //        {
        //            await Context.Channel.SendMessageAsync($"{Context.User.Mention} I am not in a voice channel.");
        //        }
        //    }
        //    else
        //    {
        //        return;
        //    }
        //}

        //[Command("stop", RunMode = RunMode.Async)]
        //[Alias("stopmusic")]
        //[Summary("Stops the music and makes bot leave voice channel.")]
        //[RequireOwner]
        //public async Task Stop()
        //{
        //    if (Context.Channel.Id == 528696379325808655 || Context.Channel.Id == 528692074917134346)
        //    {
        //        if (Blacklisted(Context.User))
        //        {
        //            return;
        //        }
        //        _voiceChannel = (Context.User as IGuildUser).VoiceChannel;
        //        if (_voiceChannel == null)
        //        {
        //            await Context.Channel.SendMessageAsync($"{Context.User.Mention} you need to be in a voice channel to use this command.");
        //        }
        //        else if (_voiceChannel.Id != 528688237812908057)
        //        {
        //            await Context.Channel.SendMessageAsync($"{Context.User.Mention} you need to be in the Music voice channel to use this command.");
        //        }
        //        else
        //        {
        //            if (Queue.Count > 0)
        //            {
        //                Tuple<string, string, string, string, string, string, string> song = Queue.Peek();
        //                Queue.Clear();
        //                NowPlaying.Clear();
        //                PlayingUrl = song.Item7;
        //                Task _ = RemoveFiles();
        //            }
        //            Playing = false;
        //            Pause = true;
        //            MusicStarted = false;
        //            await Audio.StopAsync();
        //            await ReplyAsync($"The music was successfully stopped by {Context.User.Mention} .");
        //        }
        //    }
        //    else
        //    {
        //        return;
        //    }
        //}

        //#region Audio

        //private static Process GetFfmpeg(string path)
        //{
        //    ProcessStartInfo ffmpeg = new ProcessStartInfo
        //    {
        //        FileName = "ffmpeg",
        //        Arguments = $"-xerror -i \"{path}\" -ac 2 -f s16le -ar 48000 pipe:1",
        //        UseShellExecute = false,
        //        RedirectStandardOutput = true
        //    };
        //    return Process.Start(ffmpeg);
        //}

        //private async Task SendAudio(string path)
        //{
        //    while (!File.Exists(path))
        //    {
        //        path = path.Replace("webm", "mp3");
        //        await Task.Delay(500);
        //        if (!File.Exists(path))
        //        {
        //            path = path.Replace("mp3", "webm");
        //        }
        //    }
        //    Process ffmpeg = GetFfmpeg(path);
        //    using (Stream output = ffmpeg.StandardOutput.BaseStream)
        //    {
        //        using (Discord)
        //        {
        //            int bufferSize = 1024;
        //            int bytesSent = 0;
        //            bool fail = false;
        //            bool exit = false;
        //            byte[] buffer = new byte[bufferSize];

        //            while (
        //                !Skip &&
        //                !fail &&
        //                !_disposeToken.IsCancellationRequested &&
        //                !exit
        //                    )
        //            {
        //                try
        //                {
        //                    int read = await output.ReadAsync(buffer, 0, bufferSize, _disposeToken.Token);
        //                    if (read == 0)
        //                    {
        //                        exit = true;
        //                        break;
        //                    }

        //                    await Discord.WriteAsync(buffer, 0, read, _disposeToken.Token);

        //                    if (Pause)
        //                    {
        //                        bool pauseAgain;

        //                        do
        //                        {
        //                            pauseAgain = await _tcs.Task;
        //                            _tcs = new TaskCompletionSource<bool>();
        //                        } while (pauseAgain);
        //                    }

        //                    bytesSent += read;
        //                }
        //                catch (TaskCanceledException)
        //                {
        //                    exit = true;
        //                }
        //                catch (Exception e)
        //                {
        //                    SocketUser user = Context.Client.GetUser(181853112045142016);
        //                    IEnumerable<string> parts = e.ToString().SplitInParts(1990);
        //                    foreach (string part in parts)
        //                    {
        //                        await user.SendMessageAsync("```" + part + "```");
        //                    }
        //                    fail = true;
        //                }
        //            }
        //            await Discord.FlushAsync();
        //        }
        //    }
        //}

        //private async Task RemoveFiles()
        //{
        //    string[] id = PlayingUrl.Split("watch?v=");
        //    DirectoryInfo di = new DirectoryInfo(Path.Combine(Directory.GetCurrentDirectory(), "Temp"));
        //    await Task.Delay(600000);
        //    foreach (FileInfo file in di.EnumerateFiles())
        //    {
        //        if (file.Name.Contains(id[1]))
        //        {
        //            file.Delete();
        //        }
        //    }
        //}

        //private async Task MusicPlay()
        //{
        //    bool next = false;
        //    await Task.Delay(10000);
        //    while (true)
        //    {
        //        bool pause = false;
        //        if (!next)
        //        {
        //            pause = await _tcs.Task;
        //            _tcs = new TaskCompletionSource<bool>();
        //        }
        //        else
        //        {
        //            next = false;
        //        }
        //        if (!(Queue.Count <= 0))
        //        {
        //            if (!pause)
        //            {
        //                Tuple<string, string, string, string, string, string, string> song = Queue.Peek();
        //                await Context.Channel.SendMessageAsync($"Now playing: **{Format.Sanitize(song.Item2)}** ({song.Item3})");
        //                NowPlaying.Enqueue(song);
        //                await SendAudio(song.Item1);
        //                SkipCount = 0;
        //                try
        //                {
        //                    PlayingUrl = song.Item7;
        //                    Task _ = RemoveFiles();
        //                }
        //                catch (Exception e)
        //                {
        //                    SocketUser user = Context.Client.GetUser(181853112045142016);
        //                    IEnumerable<string> parts = e.ToString().SplitInParts(1990);
        //                    foreach (string part in parts)
        //                    {
        //                        await user.SendMessageAsync("```" + part + "```");
        //                    }
        //                }
        //                finally
        //                {
        //                    if (Loop)
        //                    {
        //                        Tuple<string, string, string, string, string, string, string> item = Queue.Peek();
        //                        if (song == item)
        //                        {
        //                            Queue.Dequeue();
        //                            Queue.Enqueue(song);
        //                        }
        //                        NowPlaying.Dequeue();
        //                    }
        //                    else
        //                    {
        //                        Tuple<string, string, string, string, string, string, string> item = Queue.Peek();
        //                        if (song == item)
        //                        {
        //                            Queue.Dequeue();
        //                        }
        //                        NowPlaying.Dequeue();
        //                    }
        //                }
        //                SocketVoiceChannel voiceChannel = Context.Guild.CurrentUser.VoiceChannel;
        //                if (voiceChannel.Users.Count < 2)
        //                {
        //                    await Context.Channel.SendMessageAsync("Voice channel empty. Stopping music.");
        //                    if (Queue.Count > 0)
        //                    {
        //                        Tuple<string, string, string, string, string, string, string> song1 = Queue.Peek();
        //                        Queue.Clear();
        //                        NowPlaying.Clear();
        //                        PlayingUrl = song.Item7;
        //                        Task _ = RemoveFiles();
        //                    }
        //                    Playing = false;
        //                    Pause = true;
        //                    MusicStarted = false;
        //                    await Audio.StopAsync();
        //                }
        //                next = true;
        //            }
        //        }
        //        else
        //        {
        //            Playing = false;
        //            Pause = true;
        //            MusicStarted = false;
        //            await Audio.StopAsync();
        //        }
        //    }
        //}
        //
        //#endregion
    }
}