using System;
using System.Collections.Generic;
using System.Linq;
using EVEMon.Common.Constants;
using EVEMon.Common.Esi;
using EVEMon.Common.Enumerations.CCPAPI;
using EVEMon.Common.Interfaces;
using EVEMon.Common.Serialization.Eve;
using EVEMon.Common.Service;

namespace EVEMon.Common.Models
{
    public sealed class EveNotification : IEveMessage
    {
        private readonly CCPCharacter m_ccpCharacter;

        private bool m_queryPending;
        private string m_typeName;
        private readonly string m_esiTypeName;
        private string m_title;


        #region Constructor

        /// <summary>
        /// Constructor from the API.
        /// </summary>
        /// <param name="ccpCharacter">The CCP character.</param>
        /// <param name="src">The source.</param>
        internal EveNotification(CCPCharacter ccpCharacter, SerializableNotificationsListItem src)
        {
            m_ccpCharacter = ccpCharacter;

            NotificationID = src.NotificationID;
            TypeID = src.TypeID;
            m_esiTypeName = String.IsNullOrEmpty(src.TypeName) ? null : EsiCharacterQueries.ToTitleWords(src.TypeName);
            m_typeName = m_esiTypeName ?? EveNotificationType.GetName(src.TypeID);
            SenderName = src.SenderName;
            SentDate = src.SentDate;
            Recipient = new List<string> { ccpCharacter.Name };
            EVENotificationText = new EveNotificationText(this, new SerializableNotificationTextsListItem
            {
                NotificationID = String.IsNullOrEmpty(src.Text) ? 0 : src.NotificationID,
                NotificationText = src.Text ?? String.Empty,
            });
        }

        #endregion


        #region Properties

        /// <summary>
        /// Gets the CCP character.
        /// </summary>
        /// <value>
        /// The CCP character.
        /// </value>
        public CCPCharacter CCPCharacter => m_ccpCharacter;

        /// <summary>
        /// Gets the EVE notification ID.
        /// </summary>
        /// <value>The notification ID.</value>
        public long NotificationID { get; }

        /// <summary>
        /// Gets the EVE notification type.
        /// </summary>
        /// <value>The type.</value>
        public int TypeID { get; }

        /// <summary>
        /// Gets the name of the type.
        /// </summary>
        /// <value>
        /// The name of the type.
        /// </value>
        public string TypeName
        {
            get
            {
                if (m_typeName == EveMonConstants.UnknownText && m_esiTypeName == null)
                    m_typeName = EveNotificationType.GetName(TypeID);

                return m_typeName;
            }
        }

        /// <summary>
        /// Gets the EVE notification sender name.
        /// </summary>
        public string SenderName { get; }

        /// <summary>
        /// Gets the sent date of the EVE notification.
        /// </summary>
        /// <value>The sent date.</value>
        public DateTime SentDate { get; }

        /// <summary>
        /// Gets the EVE notification recipient.
        /// </summary>
        /// <value>The recipient.</value>
        public IEnumerable<string> Recipient { get; }

        /// <summary>
        /// Gets the EVE notification text.
        /// </summary>
        /// <value>The EVE notification text.</value>
        public EveNotificationText EVENotificationText { get; private set; }

        /// <summary>
        /// Gets the EVE notification title.
        /// </summary>
        /// <value>The title.</value>
        public string Title
        {
            get
            {
                if (!String.IsNullOrWhiteSpace(m_title) && !m_title.Contains(EveMonConstants.UnknownText))
                    return m_title;

                // ESI notifications have no subject layout: their type is the title
                if (m_esiTypeName != null)
                    return m_title = m_esiTypeName;

                string subjectLayout = EveNotificationType.GetSubjectLayout(TypeID);
                if (subjectLayout.Contains("{") && String.IsNullOrWhiteSpace(EVENotificationText.NotificationText))
                {
                    GetNotificationText();
                    return EveMonConstants.UnknownText;
                }

                m_title = String.IsNullOrWhiteSpace(subjectLayout)
                    ? EveMonConstants.UnknownText
                    : EVENotificationText.Parse(subjectLayout);

                m_title = m_title.Contains("{") || m_title == EVENotificationText.NotificationText
                    ? EveMonConstants.UnknownText
                    : m_title;

                return m_title;
            }
        }

        /// <summary>
        /// Gets the EVE notification text.
        /// </summary>
        /// <value>The text.</value>
        public string Text => EVENotificationText.ParsedText;

        #endregion

        #region Querying

        /// <summary>
        /// Gets the EVE notification.
        /// </summary>
        public void GetNotificationText()
        {
            // Exit if we are already trying to download the mail message body text
            if (m_queryPending)
                return;

            m_queryPending = true;

            // Quits if access denied
            ESIKey esiKey = m_ccpCharacter.Identity.FindESIKeyWithAccess(CCPAPICharacterMethods.NotificationTexts);
            if (esiKey == null)
                return;

            EsiQueries.QueryAsync<SerializableAPINotificationTexts>(
                CCPAPICharacterMethods.NotificationTexts, esiKey, m_ccpCharacter.CharacterID,
                NotificationID,
                OnEVENotificationTextDownloaded);
        }

        /// <summary>
        /// Processes the queried EVE notification text.
        /// </summary>
        /// <param name="result">The result.</param>
        private void OnEVENotificationTextDownloaded(CCPAPIResult<SerializableAPINotificationTexts> result)
        {
            m_queryPending = false;

            // Notify an error occured
            if (m_ccpCharacter.ShouldNotifyError(result, CCPAPICharacterMethods.NotificationTexts))
                EveMonClient.Notifications.NotifyEVENotificationTextsError(m_ccpCharacter, result);

            // Quits if there is an error
            if (result.HasError)
                return;

            // If there is an error response on missing IDs inform the user
            if (!String.IsNullOrEmpty(result.Result.MissingMessageIDs))
            {
                result.Result.Texts.Add(
                    new SerializableNotificationTextsListItem
                    {
                        NotificationID = long.Parse(result.Result.MissingMessageIDs, CultureConstants.InvariantCulture),
                        NotificationText = "The text for this notification was reported missing."
                    });
            }

            // Quit if for any reason there is no text
            if (!result.Result.Texts.Any())
                return;

            // Import the data
            EVENotificationText = new EveNotificationText(this, result.Result.Texts.First());

            EveMonClient.OnCharacterEVENotificationTextDownloaded(m_ccpCharacter);
        }

        #endregion
    }
}