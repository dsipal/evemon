using System.Xml.Serialization;

namespace EVEMon.Common.Serialization.Settings
{
    /// <summary>
    /// Represents a character authorized through EVE SSO, in the settings file.
    /// </summary>
    public sealed class SerializableESIKey
    {
        /// <summary>
        /// Gets or sets the character ID.
        /// </summary>
        [XmlAttribute("id")]
        public long ID { get; set; }

        /// <summary>
        /// Gets or sets the character name.
        /// </summary>
        [XmlAttribute("name")]
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets the character owner hash.
        /// </summary>
        [XmlAttribute("owner")]
        public string OwnerHash { get; set; }

        /// <summary>
        /// Gets or sets the granted scopes, space separated.
        /// </summary>
        [XmlAttribute("scopes")]
        public string Scopes { get; set; }

        /// <summary>
        /// Gets or sets the refresh token, encrypted for the current Windows user.
        /// </summary>
        [XmlAttribute("refreshToken")]
        public string RefreshToken { get; set; }

        /// <summary>
        /// Gets or sets whether the key is monitored.
        /// </summary>
        [XmlAttribute("monitored")]
        public bool Monitored { get; set; }
    }
}
