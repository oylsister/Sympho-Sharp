﻿using System.Xml.Schema;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Timers;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Sympho.Functions;
using Sympho.Models;

namespace Sympho
{
    public partial class Sympho : BasePlugin, IPluginConfig<Settings>
    {
        public override string ModuleName => "Sympho Audio Player";
        public override string ModuleVersion => "Alpha 1.1";
        public override string ModuleAuthor => "Oylsister";

        private ILogger<Sympho> _logger;
        private AudioHandler? _handler;
        private Youtube? _youtube;
        private Event? _event;
        public AudioService? AudioService { get; private set; }
        public Settings Config { get; set; } = new();
        public CounterStrikeSharp.API.Modules.Timers.Timer? SpamTimerCheck = null;

        public Sympho(ILogger<Sympho> logger)
        {
            _logger = logger;
        }

        public void OnConfigParsed(Settings config)
        {
            Config = config;

            _event?.InitialConfigs(config);
        }

        public override void Load(bool hotReload)
        {
            AudioService = new AudioService();
            AudioService.PluginDirectory = ModuleDirectory;

            _handler = new(this, _logger);
            _youtube = new(this, _handler, _logger);
            _event = new(this, _logger);

            LoadConfig();

            _handler.Initialize(AudioService);
            _youtube.Initialize();
            _event.Initialize(AudioService, _handler);
        }

        [CommandHelper(1, "css_yt <video-url> [start-seconds]")]
        [ConsoleCommand("css_yt")]
        public void YoutubeCommand(CCSPlayerController client, CommandInfo info)
        {
            bool allow = true;
            bool admin = AdminManager.PlayerHasPermissions(client, "@css/kick");

            if (!Config.AllowYoutube)
            {
                if (admin)
                {
                    if(Config.BypassYoutubeAdmin)
                        allow = true;

                    else
                        allow = false;
                }

                else
                    allow = false;

                if(!allow)
                {
                    info.ReplyToCommand($" {Localizer["Prefix"]} {Localizer["Youtube.NotAllowed"]}");
                    return;
                }
            }

            if(AntiSpamData.GetBlockStatus() && !admin)
            {
                info.ReplyToCommand($" {Localizer["Prefix"]} {Localizer["AntiSpam.Cooldown"]}");
                return;
            }

            var fullarg = info.ArgString;
            var splitArg = fullarg.Split(" ");
            bool start = false;
            int starttime = 0;

            if (splitArg.Length > 1)
            {
                start = int.TryParse(splitArg[1], out starttime);
            }

            var url = splitArg[0];

            // if normal player then
            if(!admin)
                AntiSpamData.SetPlayedCount(AntiSpamData.PlayedCount + 1);

            Task.Run(async () => {

                if(Config.MaxAudioLength > 0)
                {
                    if (start)
                        await _youtube!.ProceedYoutubeVideo(url, starttime, admin ? 0 : starttime + Config.MaxAudioLength);

                    else
                        await _youtube!.ProceedYoutubeVideo(url, 0, Config.MaxAudioLength);
                }

                else
                {
                    if (start)
                        await _youtube!.ProceedYoutubeVideo(url, starttime);

                    else
                        await _youtube!.ProceedYoutubeVideo(url);
                }
            });
        }

        [RequiresPermissions("@css/kick")]
        [ConsoleCommand("css_stopall")]
        public void StopAllSound(CCSPlayerController client, CommandInfo info)
        {
            if (AudioPlayer.IsAllPlaying())
            {
                Server.PrintToChatAll($" {Localizer["Prefix"]} {Localizer["Audio.AllStop"]}");
                AudioHandler.StopAudio();
            }
        }

        void LoadConfig()
        {
            if (AudioService == null)
            {
                _logger.LogError("AudioServices is null!");
                return;
            }

            var configPath = Path.Combine(ModuleDirectory, "sounds/sounds.json");

            if(!File.Exists(configPath))
            {
                _logger.LogError("Couldn't find config file! {0}", configPath);
                AudioService.ConfigsLoaded = false;
                return;
            }

            AudioService.AudioList = JsonConvert.DeserializeObject<List<CAudioConfig>>(File.ReadAllText(configPath));

            var totalCommand = 0;
            var totalSound = 0;

            foreach(var audio in AudioService.AudioList!)
            {
                if(audio.name == null || audio.sounds == null)
                    continue;

                foreach(var name in audio.name)
                {
                    totalCommand++;    
                }

                foreach(var sound in audio.sounds)
                {
                    totalSound++;
                }
            }

            _logger.LogInformation("Found config file with {0} commands and {1} sound files", totalCommand, totalSound);
            AudioService.ConfigsLoaded = true;
        }

        public int GetAudioIndex(string command)
        {
            if (AudioService == null)
                return -1;

            if (AudioService.AudioList == null)
                return -1;

            for(int i = 0; i < AudioService.AudioList.Count; i++)
            {
                for(int j = 0; j < AudioService.AudioList[i].name!.Count; j++)
                {
                    if (AudioService.AudioList[i].name![j] == command)
                        return i;
                }
            }

            return -1;
        }

        public void CheckSpam()
        {
            // if already blocked then do nothing.
            if(AntiSpamData.BlockPlay)
            {
                return;
            }

            // if played count reach limit then stop it.
            if(AntiSpamData.GetPlayedCount() >= Config.MaxSpamPerInterval)
            {
                // stop sound
                AudioHandler.StopAudio();

                // set block to true
                AntiSpamData.SetBlock(true);

                // annoucne all player.
                Server.PrintToChatAll($" {Localizer["Prefix"]} {Localizer["AntiSpam.StopByAntiSpam", Config.AntiSpamCooldown]}");

                // Set timer for cooldown.
                AddTimer(Config.AntiSpamCooldown, () => {
                    AntiSpamData.SetBlock(false);
                    AntiSpamData.SetPlayedCount(0);
                    Server.PrintToChatAll($" {Localizer["Prefix"]} {Localizer["AntiSpam.CooldownEnd"]}");
                });
            }

            // just reset the played count.
            else
            {
                AntiSpamData.SetPlayedCount(0);
            }
        }
    }
}
