using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using EVEMon.Common;
using EVEMon.Common.Constants;
using EVEMon.Common.Controls;
using EVEMon.Common.Factories;
using EVEMon.Common.Models;

namespace EVEMon.ApiCredentialsManagement
{
    public partial class CharacterDeletionWindow : EVEMonForm
    {
        private readonly Character m_character;
        private ESIKey m_esiKey;

        /// <summary>
        /// Constructor.
        /// </summary>
        private CharacterDeletionWindow()
        {
            InitializeComponent();
            characterToRemoveLabel.Font = FontFactory.GetFont("Tahoma", FontStyle.Bold);
        }

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="character"></param>
        public CharacterDeletionWindow(Character character)
            : this()
        {
            m_character = character;
        }

        /// <summary>
        /// Occurs when the control loads.
        /// </summary>
        /// <param name="e">The <see cref="System.EventArgs"/> instance containing the event data.</param>
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            if (DesignMode)
                return;

            apiKeyslistView.Items.Clear();

            // Replaces end of text with character's name
            characterToRemoveLabel.Text = String.Format(CultureConstants.DefaultCulture,
                                                        characterToRemoveLabel.Text, m_character.Name);

            // The character's EVE SSO authorization, if any
            m_esiKey = (m_character as CCPCharacter)?.Identity.ESIKey;
            bool hasESIKey = m_esiKey != null;

            if (hasESIKey)
                apiKeyslistView.Items.Add(new ListViewItem(m_esiKey.ToString()));

            deleteAPIKeyCheckBox.Checked = hasESIKey;
            deleteAPIKeyCheckBox.Visible = hasESIKey;
            noCharactersLabel.Visible = hasESIKey;

            // Resize window if there is no authorization to remove
            if (!hasESIKey)
                Size = new Size(Size.Width, Size.Height - apiKeyslistView.Height / 2);
        }

        /// <summary>
        /// Deletes the character and, when checked, its authorization.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void deleteButton_Click(object sender, EventArgs e)
        {
            // Note: Keep this order of removal
            if (deleteAPIKeyCheckBox.Checked && m_esiKey != null)
                EveMonClient.ESIKeys.Remove(m_esiKey);

            EveMonClient.Characters.Remove(m_character);

            Close();
        }

        /// <summary>
        /// Closes the window.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void cancelButton_Click(object sender, EventArgs e)
        {
            Close();
        }
    }
}