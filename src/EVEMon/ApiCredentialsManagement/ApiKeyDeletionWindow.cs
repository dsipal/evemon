using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using EVEMon.Common;
using EVEMon.Common.Constants;
using EVEMon.Common.Controls;
using EVEMon.Common.Extensions;
using EVEMon.Common.Factories;
using EVEMon.Common.Models;

namespace EVEMon.ApiCredentialsManagement
{
    public sealed partial class ApiKeyDeletionWindow : EVEMonForm
    {
        private readonly ESIKey m_esiKey;

        /// <summary>
        /// Constructor used in designer.
        /// </summary>
        private ApiKeyDeletionWindow()
        {
            InitializeComponent();
            deletionLabel.Font = FontFactory.GetFont("Tahoma", FontStyle.Bold);
        }

        /// <summary>
        /// Constructor used in code.
        /// </summary>
        /// <param name="esiKey">The ESI key to remove.</param>
        public ApiKeyDeletionWindow(ESIKey esiKey)
            : this()
        {
            esiKey.ThrowIfNull(nameof(esiKey), "ESI key can't be null");

            m_esiKey = esiKey;
        }

        /// <summary>
        /// On load, fills the character to remove.
        /// </summary>
        /// <param name="e"></param>
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            if (DesignMode)
                return;

            deletionLabel.Text = String.Format(CultureConstants.DefaultCulture, deletionLabel.Text, m_esiKey.CharacterName);

            charactersListView.Items.Clear();

            CCPCharacter ccpCharacter = m_esiKey.CharacterIdentity?.CCPCharacter;
            if (ccpCharacter != null)
                charactersListView.Items.Add(new ListViewItem(ccpCharacter.Name) { Tag = ccpCharacter });

            // If there is no character, resize the window
            if (charactersListView.Items.Count != 0)
                return;

            charactersListGroupBox.Visible = false;
            Size = new Size(Size.Width - charactersListGroupBox.Height / 2, Size.Height - charactersListGroupBox.Height);
        }

        /// <summary>
        /// Removes the authorization and, when checked, the character.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void deleteButton_Click(object sender, EventArgs e)
        {
            EveMonClient.ESIKeys.Remove(m_esiKey);

            foreach (CCPCharacter ccpCharacter in charactersListView.Items.Cast<ListViewItem>()
                .Where(item => item.Checked)
                .Select(item => item.Tag)
                .OfType<CCPCharacter>())
            {
                EveMonClient.Characters.Remove(ccpCharacter);
            }

            Close();
        }
    }
}