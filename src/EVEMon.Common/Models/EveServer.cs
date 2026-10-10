using System;
using System.Net;
using System.Threading.Tasks;
using EVEMon.Common.Enumerations;
using EVEMon.Common.Enumerations.CCPAPI;
using EVEMon.Common.Enumerations.UISettings;
using EVEMon.Common.Net;
using EVEMon.Common.Serialization.Esi;
using EVEMon.Common.Serialization.Eve;
using EVEMon.Common.SettingsObjects;

namespace EVEMon.Common.Models
{
    /// <summary>
    /// Represents the status of a server
    /// </summary>
    /// <remarks>
    /// The status is read from the ESI <c>/status/</c> endpoint, which replaced the retired
    /// XML API <c>/server/ServerStatus.xml.aspx</c>.
    /// </remarks>
    public sealed class EveServer
    {
        private const string Name = "Tranquility";
        private static readonly Uri s_statusUrl = new Uri("https://esi.evetech.net/latest/status/?datasource=tranquility");

        private int m_users;
        private ServerStatus m_status;
        private DateTime m_serverDateTime = DateTime.UtcNow;
        private DateTime m_nextUpdate = DateTime.MinValue;
        private bool m_isUpdating;

        /// <summary>
        /// Constructor.
        /// </summary>
        internal EveServer()
        {
            m_status = ServerStatus.Unknown;

            EveMonClient.TimerTick += EveMonClient_TimerTick;
        }

        /// <summary>
        /// Gets the server status message.
        /// </summary>
        public string StatusText
        {
            get
            {
                switch (m_status)
                {
                    case ServerStatus.Online:
                        return $"{Name} Server Online ({m_users:N0} Pilots)";
                    case ServerStatus.Offline:
                        return $"{Name} Server Offline";
                    case ServerStatus.CheckDisabled:
                        return $"{Name} Server Status Check Disabled";
                    default:
                        return $"{Name} Server Status Unknown";
                }
            }
        }

        /// <summary>
        /// Gets the server date time.
        /// </summary>
        public DateTime ServerDateTime => m_serverDateTime;

        /// <summary>
        /// Forces an update of the server status (on the next timer tick).
        /// </summary>
        public void ForceUpdate()
        {
            m_nextUpdate = DateTime.MinValue;
        }

        /// <summary>
        /// Handles the TimerTick event of the EveMonClient control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.EventArgs"/> instance containing the event data.</param>
        private async void EveMonClient_TimerTick(object sender, EventArgs e)
        {
            m_serverDateTime = m_serverDateTime.AddSeconds(1);
            await UpdateIfDueAsync();
        }

        /// <summary>
        /// Queries the server status when the user's update period has elapsed.
        /// </summary>
        private async Task UpdateIfDueAsync()
        {
            if (m_isUpdating || DateTime.UtcNow < m_nextUpdate)
                return;

            // The check can be turned off from the update settings
            UpdatePeriod period = UpdatePeriod.Minutes5;
            string key = CCPAPIGenericMethods.ServerStatus.ToString();
            if (Settings.Updates.Periods.ContainsKey(key))
                period = Settings.Updates.Periods[key];

            if (period == UpdatePeriod.Never)
            {
                if (m_status != ServerStatus.CheckDisabled)
                    SetStatus(ServerStatus.CheckDisabled);
                m_nextUpdate = DateTime.UtcNow.AddMinutes(1);
                return;
            }

            if (!NetworkMonitor.IsNetworkAvailable)
                return;

            m_isUpdating = true;
            try
            {
                await QueryStatusAsync();
            }
            finally
            {
                m_nextUpdate = DateTime.UtcNow.Add(period.ToDuration());
                m_isUpdating = false;
            }
        }

        /// <summary>
        /// Queries ESI for the server status.
        /// </summary>
        private async Task QueryStatusAsync()
        {
            DownloadResult<SerializableESIServerStatus> result =
                await Util.DownloadJsonAsync<SerializableESIServerStatus>(s_statusUrl, acceptEncoded: true);

            // EVE time is UTC
            m_serverDateTime = DateTime.UtcNow;

            if (result.Error != null)
            {
                // ESI answers "service unavailable" while Tranquility is down (e.g. daily downtime)
                HttpStatusCode? statusCode = result.Error.ResponseStatusCode;
                if (statusCode == HttpStatusCode.ServiceUnavailable || statusCode == HttpStatusCode.GatewayTimeout ||
                    statusCode == HttpStatusCode.BadGateway)
                {
                    EveMonClient.Notifications.InvalidateAPIError();
                    SetStatus(ServerStatus.Offline);
                    return;
                }

                EveMonClient.Notifications.NotifyServerStatusError(
                    new CCPAPIResult<SerializableESIServerStatus>(result.Error));
                SetStatus(ServerStatus.Unknown, notifyChange: false);
                return;
            }

            if (result.Result == null)
            {
                SetStatus(ServerStatus.Unknown, notifyChange: false);
                return;
            }

            // In VIP mode only CCP and selected players can log in
            m_users = result.Result.Players;
            EveMonClient.Notifications.InvalidateAPIError();
            SetStatus(result.Result.Vip == true ? ServerStatus.Offline : ServerStatus.Online);
        }

        /// <summary>
        /// Updates the status and notifies the subscribers.
        /// </summary>
        /// <param name="status">The new status.</param>
        /// <param name="notifyChange">if set to <c>true</c>, a notification is sent when the status changed.</param>
        private void SetStatus(ServerStatus status, bool notifyChange = true)
        {
            ServerStatus lastStatus = m_status;
            m_status = status;

            // Notify subscribers about update
            EveMonClient.OnServerStatusUpdated(this, lastStatus, m_status);

            // Send a notification when the server goes up or down
            // (finding it online at start up is not news)
            bool cameOnlineFromUnknown = lastStatus == ServerStatus.Unknown && m_status == ServerStatus.Online;
            if (notifyChange && lastStatus != m_status && !cameOnlineFromUnknown)
            {
                EveMonClient.Notifications.NotifyServerStatusChanged(Name, m_status);
                return;
            }

            EveMonClient.Notifications.InvalidateServerStatusChange();
        }
    }
}
