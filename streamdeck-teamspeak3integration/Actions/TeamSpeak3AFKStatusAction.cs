﻿using System;
using System.Threading.Tasks;

using BarRaider.SdTools;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using PrimS.Telnet;

using streamdeck_client_csharp;
using streamdeck_client_csharp.Events;

using ZerGo0.TeamSpeak3Integration.Helpers;

using KeyPayload = BarRaider.SdTools.KeyPayload;

namespace ZerGo0.TeamSpeak3Integration.Actions
{
    [PluginActionId("com.zergo0.teamspeak3integration.toggleafkstatus")]
    public class TeamSpeak3AfkStatusAction : PluginBase
    {
        public TeamSpeak3AfkStatusAction(SDConnection connection, InitialPayload payload) : base(connection, payload)
        {
            if (payload.Settings == null || payload.Settings.Count == 0)
                _settings = PluginSettings.CreateDefaultSettings();
            else
                _settings = payload.Settings.ToObject<PluginSettings>();
            connection.StreamDeckConnection.OnSendToPlugin += StreamDeckConnection_OnSendToPlugin;

            SaveSettings();
        }

        public override void Dispose()
        {
            _telnetclient?.Dispose();
            Connection.StreamDeckConnection.OnSendToPlugin -= StreamDeckConnection_OnSendToPlugin;
            Logger.Instance.LogMessage(TracingLevel.INFO, "Destructor called");
        }

        public override async void KeyPressed(KeyPayload payload)
        {
            Logger.Instance.LogMessage(TracingLevel.INFO, "Key Pressed");

            try
            {
                if (_telnetclient == null || !_telnetclient.IsConnected)
                {
                    _telnetclient = await TeamSpeak3Telnet.SetupTelnetClient(_settings.ApiKey);
                    if (_telnetclient == null) return;
                }

                if (payload.IsInMultiAction)
                    await ToggleAwayStatus(_telnetclient, (int) payload.UserDesiredState);
                else
                    await ToggleAwayStatus(_telnetclient);
            }
            catch (Exception)
            {
                _telnetclient?.Dispose();
                _telnetclient = null;
                await SetAwayStatusState();
            }
        }

        public override void KeyReleased(KeyPayload payload)
        {
        }

        public override async void OnTick()
        {
            try
            {
                if (_telnetclient == null || !_telnetclient.IsConnected)
                {
                    _telnetclient = await TeamSpeak3Telnet.SetupTelnetClient(_settings.ApiKey);
                    if (_telnetclient == null) return;
                }

                var clientId = await TeamSpeak3Telnet.GetClientId(_telnetclient);
                if (clientId == null)
                {
                    _telnetclient?.Dispose();
                    return;
                }

                var awayStatus = await TeamSpeak3Telnet.GetAwayStatus(_telnetclient, clientId);
                if (awayStatus == _savedSatus)
                {
                    await SetAwayStatusState(awayStatus);
                    return;
                }

                switch (awayStatus)
                {
                    case -1:
                        return;
                    case 0:
                        await SetAwayStatusState();
                        break;
                    case 1:
                        await SetAwayStatusState(1);
                        break;
                }

                _savedSatus = awayStatus;
            }
            catch (Exception)
            {
                _telnetclient?.Dispose();
                _telnetclient = null;
                await SetAwayStatusState();
            }
        }

        public override void ReceivedSettings(ReceivedSettingsPayload payload)
        {
            Tools.AutoPopulateSettings(_settings, payload.Settings);
            SaveSettings();
        }

        public override void ReceivedGlobalSettings(ReceivedGlobalSettingsPayload payload)
        {
        }

        private class PluginSettings
        {
            [JsonProperty(PropertyName = "apiKey")]
            public string ApiKey { get; set; }

            [JsonProperty(PropertyName = "awayStatusMessage")]
            public string AwayStatusMessage { get; set; }

            public static PluginSettings CreateDefaultSettings()
            {
                var instance = new PluginSettings
                {
                    ApiKey = string.Empty,
                    AwayStatusMessage = string.Empty
                };

                return instance;
            }
        }

#region Private Members

        private readonly PluginSettings _settings;
        private int _savedSatus;
        private Client _telnetclient;

#endregion

#region Private Methods

        private Task SaveSettings()
        {
            return Connection.SetSettingsAsync(JObject.FromObject(_settings));
        }

        private void StreamDeckConnection_OnSendToPlugin(object sender,
            StreamDeckEventReceivedEventArgs<SendToPluginEvent> e)
        {
            var payload = e.Event.Payload;
            if (Connection.ContextId != e.Event.Context) return;
        }

        private async Task ToggleAwayStatus(Client telnetClient, int desiredState = -1)
        {
            try
            {
                var clientId = await TeamSpeak3Telnet.GetClientId(telnetClient);
                if (clientId == null)
                {
                    _telnetclient?.Dispose();
                    return;
                }

                int awayStatus;
                if (desiredState == -1)
                    awayStatus = await TeamSpeak3Telnet.GetAwayStatus(telnetClient, clientId);
                else
                    awayStatus = desiredState == 1 ? 0 : 1;

                var setAwayStatus = false;
                switch (awayStatus)
                {
                    case -1:
                        return;
                    case 0:
                        await TeamSpeak3Telnet.SetInputMuteStatus(telnetClient, "1");
                        await TeamSpeak3Telnet.SetOutputMuteStatus(telnetClient, "1");
                        setAwayStatus = await TeamSpeak3Telnet.SetAwayStatus(telnetClient, "1");
                        if (_settings.AwayStatusMessage.Length > 0)
                            await TeamSpeak3Telnet.SetAwayMessage(telnetClient, _settings.AwayStatusMessage);
                        break;
                    case 1:
                        await TeamSpeak3Telnet.SetInputMuteStatus(telnetClient, "0");
                        await TeamSpeak3Telnet.SetOutputMuteStatus(telnetClient, "0");
                        setAwayStatus = await TeamSpeak3Telnet.SetAwayStatus(telnetClient, "0");
                        break;
                }

                if (!setAwayStatus) return;
            }
            catch (Exception)
            {
                _telnetclient?.Dispose();
                _telnetclient = null;
                await SetAwayStatusState();
            }
        }

        private async Task SetAwayStatusState(int muted = 0)
        {
            switch (muted)
            {
                case 0:
                    await Connection.SetStateAsync(0);
                    break;
                case 1:
                    await Connection.SetStateAsync(1);
                    break;
            }
        }

#endregion
    }
}