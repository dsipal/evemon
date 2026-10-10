using System;
using Newtonsoft.Json;

namespace EVEMon.Common.Serialization.Esi
{
    /// <summary>
    /// The response of the ESI <c>/status/</c> endpoint.
    /// </summary>
    public sealed class SerializableESIServerStatus
    {
        /// <summary>
        /// Gets or sets the number of players online.
        /// </summary>
        [JsonProperty("players")]
        public int Players { get; set; }

        /// <summary>
        /// Gets or sets the running server version.
        /// </summary>
        [JsonProperty("server_version")]
        public string ServerVersion { get; set; }

        /// <summary>
        /// Gets or sets the server start time.
        /// </summary>
        [JsonProperty("start_time")]
        public DateTime StartTime { get; set; }

        /// <summary>
        /// Gets or sets whether the server is in VIP mode (only open to CCP and selected players).
        /// </summary>
        [JsonProperty("vip")]
        public bool? Vip { get; set; }
    }
}
