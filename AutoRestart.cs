using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Config;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Admin;
using System.Text.Json.Serialization;
using System;
using Microsoft.Extensions.Localization;
using CSTimer = CounterStrikeSharp.API.Modules.Timers.Timer;

namespace AutoRestart
{
    public class AutoRestartConfig : BasePluginConfig
    {
        [JsonPropertyName("AutoRestartEnabled")]
        public bool AutoRestartEnabled { get; set; } = true;

        [JsonPropertyName("EnableManualRestart")]
        public bool EnableManualRestart { get; set; } = true;

        [JsonPropertyName("Flag")]
        public string Flag { get; set; } = "@css/root";

        [JsonPropertyName("AutoRestartTime")]
        public string AutoRestartTime { get; set; } = "01:00:00";
    }

    public class AutoRestart : BasePlugin, IPluginConfig<AutoRestartConfig>
    {
        private const int WarningSeconds = 30;
        private const int CountdownSeconds = 10;
        private const int CenterHtmlDurationSeconds = 1;

        public override string ModuleName => "AutoRestart";
        public override string ModuleVersion => "1.0.0";
        public override string ModuleAuthor => "M1k@c";

        public required AutoRestartConfig Config { get; set; }
        private static IStringLocalizer? _Localizer;
        private CSTimer? _restartTimer;
        private CSTimer? _warningTimer;
        private CSTimer? _countdownStartTimer;
        private CSTimer? _countdownTimer;
        private int _countdownSeconds;

        public void OnConfigParsed(AutoRestartConfig config)
        {
            Config = config;
        }

        public override void Load(bool hotReload)
        {
            Console.WriteLine("[AutoRestart] Plugin loaded successfully!");

            _Localizer = Localizer;

            AddCommand("css_restartserver", "Restart the server immediately", (player, info) => RestartCommand(player));
            RegisterListener<Listeners.OnMapStart>(OnMapStart);

            if (Config.AutoRestartEnabled)
            {
                ScheduleAutoRestart(hotReload ? "plugin hot reload" : "plugin load");
            }
        }

        private bool HasAdminPermission(CCSPlayerController? player)
        {
            return player == null || AdminManager.PlayerHasPermissions(player, Config.Flag);
        }

        private void OnMapStart(string mapName)
        {
            if (Config.AutoRestartEnabled)
            {
                ScheduleAutoRestart($"map start ({mapName})");
            }
        }

        private void ScheduleAutoRestart(string reason)
        {
            if (!TimeSpan.TryParse(Config.AutoRestartTime, out TimeSpan restartTime))
            {
                Console.WriteLine($"[AutoRestart] Invalid AutoRestartTime format: {Config.AutoRestartTime}. Expected format: HH:mm:ss");
                return;
            }

            DateTime now = DateTime.Now;
            DateTime nextRestart = now.Date.Add(restartTime);
            if (nextRestart <= now)
            {
                nextRestart = nextRestart.AddDays(1);
            }

            ScheduleRestartAt(nextRestart, reason);
        }

        private void ScheduleRestartAt(DateTime restartAt, string reason)
        {
            DateTime now = DateTime.Now;
            TimeSpan delay = restartAt - now;
            if (delay.TotalSeconds < 0)
            {
                delay = TimeSpan.Zero;
            }

            ClearScheduledTimers();

            if (delay.TotalSeconds <= 0)
            {
                RestartServer();
                return;
            }

            _restartTimer = AddTimer((float)delay.TotalSeconds, RestartServer);
            ScheduleWarningAndCountdown(delay);

            Console.WriteLine($"[AutoRestart] Next restart scheduled for {restartAt:yyyy-MM-dd HH:mm:ss} (in {delay.TotalSeconds:F0}s). Reason: {reason}");
        }

        private void ScheduleWarningAndCountdown(TimeSpan delay)
        {
            if (delay.TotalSeconds > WarningSeconds)
            {
                _warningTimer = AddTimer((float)(delay.TotalSeconds - WarningSeconds), () => NotifyAllWarning(WarningSeconds));
            }
            else
            {
                NotifyAllWarning(Math.Max(1, (int)Math.Ceiling(delay.TotalSeconds)));
            }

            if (delay.TotalSeconds > CountdownSeconds)
            {
                _countdownStartTimer = AddTimer((float)(delay.TotalSeconds - CountdownSeconds), () => StartCountdown(CountdownSeconds));
            }
            else
            {
                StartCountdown(Math.Max(1, (int)Math.Ceiling(delay.TotalSeconds)));
            }
        }

        private void StartCountdown(int seconds)
        {
            if (seconds <= 0)
            {
                return;
            }

            _countdownSeconds = seconds;
            _countdownTimer?.Kill();
            _countdownTimer = AddTimer(1.0f, CountdownTick, TimerFlags.REPEAT);
            CountdownTick();
        }

        private void CountdownTick()
        {
            if (_countdownSeconds <= 0)
            {
                _countdownTimer?.Kill();
                _countdownTimer = null;
                return;
            }

            NotifyAllCountdown(_countdownSeconds);
            _countdownSeconds--;
        }

        private void ClearScheduledTimers()
        {
            _restartTimer?.Kill();
            _warningTimer?.Kill();
            _countdownStartTimer?.Kill();
            _countdownTimer?.Kill();

            _restartTimer = null;
            _warningTimer = null;
            _countdownStartTimer = null;
            _countdownTimer = null;
            _countdownSeconds = 0;
        }

        private void RestartServer()
        {
            Console.WriteLine("[AutoRestart] Restarting server via command...");
            NotifyAllRestarting();
            ClearScheduledTimers();
            Server.ExecuteCommand("quit");
        }

        private void NotifyAllWarning(int seconds)
        {
            BroadcastToAll(player =>
            {
                var message = _Localizer?.ForPlayer(player, "restart_warning", seconds) ?? $"Server will restart in {seconds} seconds.";
                player.PrintToChat(message);
            });
        }

        private void NotifyAllCountdown(int seconds)
        {
            BroadcastToAll(player =>
            {
                var chatMessage = _Localizer?.ForPlayer(player, "restart_countdown_chat", seconds) ?? $"Server restarting in {seconds}...";
                player.PrintToChat(chatMessage);

                var centerMessage = _Localizer?.ForPlayer(player, "restart_countdown_center", seconds) ?? $"Server restarting in {seconds}...";
                player.PrintToCenterHtml(centerMessage, CenterHtmlDurationSeconds);
            });
        }

        private void NotifyAllRestarting()
        {
            BroadcastToAll(player =>
            {
                var chatMessage = _Localizer?.ForPlayer(player, "restart_now_chat") ?? "Server is restarting now...";
                player.PrintToChat(chatMessage);

                var centerMessage = _Localizer?.ForPlayer(player, "restart_now_center") ?? "Server is restarting now...";
                player.PrintToCenterHtml(centerMessage, CenterHtmlDurationSeconds);
            });
        }

        private void BroadcastToAll(Action<CCSPlayerController> action)
        {
            foreach (var player in Utilities.GetPlayers())
            {
                if (player?.IsValid != true)
                {
                    continue;
                }

                action(player);
            }
        }

        private void RestartCommand(CCSPlayerController? player)
        {
            if (!HasAdminPermission(player))
            {
                Console.WriteLine("[AutoRestart] Player has no admin permission for restart.");
                return;
            }

            if (!Config.EnableManualRestart)
            {
                Server.NextFrame(() =>
                {
                    if (player?.IsValid == true)
                        player.PrintToChat(_Localizer?.ForPlayer(player, "manual_restart_disabled") ?? "Manual restart is disabled.");
                });
                return;
            }

            Console.WriteLine("[AutoRestart] Admin executed 'css_restart' command.");
            ScheduleManualRestart(player);
        }

        private void ScheduleManualRestart(CCSPlayerController? player)
        {
            DateTime restartAt = DateTime.Now.AddSeconds(WarningSeconds);
            ScheduleRestartAt(restartAt, "manual command");

            if (player?.IsValid == true)
            {
                var message = _Localizer?.ForPlayer(player, "manual_restart_scheduled", WarningSeconds)
                    ?? $"Manual restart scheduled. Server will restart in {WarningSeconds} seconds.";
                player.PrintToChat(message);
            }
        }
    }
}
