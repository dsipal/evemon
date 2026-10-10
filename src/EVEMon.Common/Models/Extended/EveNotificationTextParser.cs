using System.Collections.Generic;
using YamlDotNet.RepresentationModel;

namespace EVEMon.Common.Models.Extended
{
    public abstract class EveNotificationTextParser
    {
        private static readonly EveNotificationTextParser s_parser = new InternalEveNotificationTextParser();

        /// <summary>
        /// Parses the notification text.
        /// </summary>
        /// <param name="notification">The notification.</param>
        /// <param name="pair">The pair.</param>
        /// <param name="parsedDict">The parsed dictionary.</param>
        public abstract void Parse(EveNotification notification, KeyValuePair<YamlNode, YamlNode> pair,
            IDictionary<string, string> parsedDict);

        /// <summary>
        /// Gets the parser.
        /// </summary>
        /// <remarks>
        /// Earlier versions downloaded and compiled an external parser at runtime through CodeDom,
        /// which modern .NET does not support; the built-in parser is always used now.
        /// </remarks>
        /// <returns></returns>
        internal static EveNotificationTextParser GetParser() => s_parser;
    }
}
