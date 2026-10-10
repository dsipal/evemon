using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using EVEMon.Common;
using EVEMon.Common.Constants;
using EVEMon.Common.Controls;
using EVEMon.Common.CustomEventArgs;
using EVEMon.Common.Enumerations;
using EVEMon.Common.Factories;
using EVEMon.Common.Models;

namespace EVEMon.ApiCredentialsManagement
{
    public partial class ApiKeysManagementWindow : EVEMonForm
    {
        private readonly Dictionary<Character, bool> m_monitoredCharacters = new Dictionary<Character, bool>();

        private int m_refreshingCharactersCounter;

        /// <summary>
        /// Constructor.
        /// </summary>
        public ApiKeysManagementWindow()
        {
            InitializeComponent();

            apiKeysListBox.Font = FontFactory.GetFont("Tahoma", 9.75f);
            charactersListView.Font = FontFactory.GetFont("Tahoma", 9.75f);
            apiKeysLabel.Font = FontFactory.GetFont("Tahoma", 12F);
            apiKeyListLabel.Font = FontFactory.GetFont("Tahoma", 12F);
            charactersLabel.Font = FontFactory.GetFont("Tahoma", 12F);
            charactersListLabel.Font = FontFactory.GetFont("Tahoma", 12F);
        }

        /// <summary>
        /// On loading, intialize the controls and subscribe events.
        /// </summary>
        /// <param name="e"></param>
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            if (DesignMode)
                return;

            ListViewHelper.EnableDoubleBuffer(charactersListView);

            EveMonClient.ESIKeyCollectionChanged += EveMonClient_ESIKeyCollectionChanged;
            EveMonClient.ESIKeyInfoUpdated += EveMonClient_ESIKeyInfoUpdated;
            EveMonClient.CharacterCollectionChanged += EveMonClient_CharacterCollectionChanged;
            EveMonClient.CharacterUpdated += EveMonClient_CharacterUpdated;
            Disposed += OnDisposing;

            UpdateAPIKeysList();
            UpdateCharactersList();
            AdjustLastColumn();

            // Selects the second page if no API key known so far
            if (EveMonClient.Characters.Count == 0)
                tabControl.SelectedIndex = 1;
        }

        /// <summary>
        /// Occurs on disposing.
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="System.EventArgs"/> instance containing the event data.</param>
        private void OnDisposing(object sender, EventArgs e)
        {
            // Unsubscribe events
            EveMonClient.ESIKeyCollectionChanged -= EveMonClient_ESIKeyCollectionChanged;
            EveMonClient.ESIKeyInfoUpdated -= EveMonClient_ESIKeyInfoUpdated;
            EveMonClient.CharacterCollectionChanged -= EveMonClient_CharacterCollectionChanged;
            EveMonClient.CharacterUpdated -= EveMonClient_CharacterUpdated;

            // Update the monitored status of selected characters
            foreach (KeyValuePair<Character, bool> monitoredCharacter in m_monitoredCharacters
                .Where(character => EveMonClient.Characters.Contains(character.Key)))
            {
                monitoredCharacter.Key.Monitored = monitoredCharacter.Value;
            }
        }

        /// <summary>
        /// When the size changes, we adjust the characters' columns.
        /// </summary>
        /// <param name="e"></param>
        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);

            if (charactersListView != null)
                AdjustLastColumn();
        }


        #region Global Events Handlers

        /// <summary>
        /// When the API key collection changes, we update the content.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void EveMonClient_ESIKeyCollectionChanged(object sender, EventArgs e)
        {
            UpdateAPIKeysList();
        }

        /// <summary>
        /// When the API key info updates, we update the content.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void EveMonClient_ESIKeyInfoUpdated(object sender, EventArgs e)
        {
            if (!Visible)
                return;

            apiKeysListBox.Invalidate();
        }

        /// <summary>
        /// When the characters collection changed, we update the characters list.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void EveMonClient_CharacterCollectionChanged(object sender, EventArgs e)
        {
            UpdateCharactersList();
        }

        /// <summary>
        /// When the character changes, the displayed names changes too.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void EveMonClient_CharacterUpdated(object sender, CharacterChangedEventArgs e)
        {
            UpdateCharactersList();
        }

        #endregion


        #region API keys management

        /// <summary>
        /// Updates the API keys list.
        /// </summary>
        private void UpdateAPIKeysList()
        {
            if (!Visible)
                return;

            apiKeysListBox.ESIKeys = EveMonClient.ESIKeys;
            apiKeysMultiPanel.SelectedPage = EveMonClient.ESIKeys.Any() ? apiKeysListPage : noAPIKeysPage;
        }

        /// <summary>
        /// Handles the MouseClick event of the apiKeysListBox control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.Windows.Forms.MouseEventArgs"/> instance containing the event data.</param>
        private void apiKeysListBox_MouseClick(object sender, MouseEventArgs e)
        {
            bool itemClicked = false;

            // Search for the clicked item
            for (int index = 0; index < apiKeysListBox.ESIKeys.Count(); index++)
            {
                Rectangle rect = apiKeysListBox.GetItemRectangle(index);

                // Did click occured generally on the item ?
                if (!rect.Contains(e.Location))
                    continue;

                itemClicked = true;

                int yOffset = (rect.Height - ApiKeysListBox.CheckBoxSize.Height) / 2;
                Rectangle cbRect = new Rectangle(rect.Left + apiKeysListBox.Margin.Left, rect.Top + yOffset,
                                                 ApiKeysListBox.CheckBoxSize.Width, ApiKeysListBox.CheckBoxSize.Height);
                cbRect.Inflate(2, 2);

                // Did click occured on the checkbox ?
                if (e.Button == MouseButtons.Middle || !cbRect.Contains(e.Location))
                    continue;

                ESIKey esiKey = apiKeysListBox.ESIKeys.ElementAt(index);
                esiKey.Monitored = !esiKey.Monitored;
                apiKeysListBox.Invalidate();
            }

            if (!itemClicked)
                apiKeysListBox.SelectedIndex = -1;
        }

        /// <summary>
        /// When the selection changes, we update the controls.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void apiKeysListBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            deleteAPIKeyMenu.Enabled = apiKeysListBox.SelectedIndex != -1;
            editAPIKeyMenu.Enabled = apiKeysListBox.SelectedIndex != -1;
        }

        /// <summary>
        /// On double click, forces the edition.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void apiKeysListBox_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            // Search for the double-clicked item
            int index = 0;
            foreach (ESIKey esiKey in apiKeysListBox.ESIKeys)
            {
                Rectangle rect = apiKeysListBox.GetItemRectangle(index);
                index++;

                if (!rect.Contains(e.Location))
                    continue;

                // Authorize the character again
                using (EsiAuthorizationWindow window = new EsiAuthorizationWindow(esiKey))
                {
                    window.ShowDialog(this);
                    return;
                }
            }
        }

        /// <summary>
        /// API key toolbar > Edit.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void editAPIKeyMenu_Click(object sender, EventArgs e)
        {
            ESIKey esiKey = apiKeysListBox.ESIKeys.ElementAt(apiKeysListBox.SelectedIndex);
            using (EsiAuthorizationWindow window = new EsiAuthorizationWindow(esiKey))
            {
                window.ShowDialog(this);
            }
        }

        /// <summary>
        /// API key toolbar > Add.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void addAPIKeyMenu_Click(object sender, EventArgs e)
        {
            using (EsiAuthorizationWindow window = new EsiAuthorizationWindow())
            {
                window.ShowDialog(this);
            }
        }

        /// <summary>
        /// Accounts toolbar > Delete
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void deleteAPIKeyMenu_Click(object sender, EventArgs e)
        {
            if (apiKeysListBox.SelectedIndex == -1)
                return;

            ESIKey esiKey = apiKeysListBox.ESIKeys.ElementAt(apiKeysListBox.SelectedIndex);
            using (ApiKeyDeletionWindow window = new ApiKeyDeletionWindow(esiKey))
            {
                window.ShowDialog(this);
            }

            deleteAPIKeyMenu.Enabled = apiKeysListBox.SelectedIndex != -1;
            editAPIKeyMenu.Enabled = apiKeysListBox.SelectedIndex != -1;
        }

        /// <summary>
        /// Handles the KeyDown event of the apiKeysListBox control.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void apiKeysListBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Delete)
                deleteAPIKeyMenu_Click(sender, e);
        }

        #endregion


        #region Characters management

        /// <summary>
        /// Updates the characters list.
        /// </summary>
        private void UpdateCharactersList()
        {
            if (!Visible)
                return;

            // Begin the update
            m_refreshingCharactersCounter++;

            // Update the list view item
            UpdateCharactersListContent();

            // Invalidates the API keys list
            apiKeysListBox.Invalidate();

            // Make a help message appears when no characters exist
            charactersMultiPanel.SelectedPage = EveMonClient.Characters.Count == 0 ? noCharactersPage : charactersListPage;

            // End of the update
            m_refreshingCharactersCounter--;
        }

        /// <summary>
        /// Recreate the items in the characters listview
        /// </summary>
        private void UpdateCharactersListContent()
        {
            int position = charactersListView.GetVerticalScrollBarPosition();

            charactersListView.BeginUpdate();
            try
            {
                // Retrieve current selection and grouping option
                List<Character> oldSelection = new List<Character>(charactersListView.SelectedItems.Cast<ListViewItem>()
                                                                       .Select(x => x.Tag).OfType<Character>());

                charactersListView.Groups.Clear();
                charactersListView.Items.Clear();

                // Grouping (not authorized, authorized with EVE SSO, character files, character urls)
                bool isGrouping = groupingMenu.Checked;
                ListViewGroup noESIKeyGroup = new ListViewGroup("Not authorized (cached data only)");
                ListViewGroup esiKeyGroup = new ListViewGroup("Authorized with EVE SSO");
                ListViewGroup fileGroup = new ListViewGroup("Character files");
                ListViewGroup urlGroup = new ListViewGroup("Character urls");

                if (isGrouping)
                    ArrangeByGroup(noESIKeyGroup, esiKeyGroup, fileGroup, urlGroup);

                // Add items
                foreach (Character character in EveMonClient.Characters.OrderBy(x => x.Name))
                {
                    ListViewItem item = new ListViewItem { Checked = character.Monitored, Tag = character };

                    // Retrieve the texts for the different columns
                    ESIKey esiKey = character.Identity.ESIKey;
                    string esiKeyText = esiKey == null
                        ? String.Empty
                        : esiKey.State == ESIKeyState.NeedsAuthorization ? "Must be authorized again" : "Authorized";
                    string typeText = "CCP";
                    string uriText = "-";

                    UriCharacter uriCharacter = character as UriCharacter;
                    if (uriCharacter != null)
                    {
                        typeText = uriCharacter.Uri.IsFile ? "File" : "Url";
                        uriText = uriCharacter.Uri.ToString();

                        if (isGrouping)
                            item.Group = uriCharacter.Uri.IsFile ? fileGroup : urlGroup;
                    }
                        // Grouping CCP characters
                    else if (isGrouping)
                        item.Group = esiKey == null ? noESIKeyGroup : esiKeyGroup;

                    // Add the item and its subitems
                    item.SubItems.Add(new ListViewItem.ListViewSubItem(item, typeText));
                    item.SubItems.Add(new ListViewItem.ListViewSubItem(item,
                                                                       character.CharacterID.ToString(
                                                                           CultureConstants.DefaultCulture)));
                    item.SubItems.Add(new ListViewItem.ListViewSubItem(item, character.Name));
                    item.SubItems.Add(new ListViewItem.ListViewSubItem(item, esiKeyText));
                    item.SubItems.Add(new ListViewItem.ListViewSubItem(item, uriText));

                    charactersListView.Items.Add(item);

                    if (oldSelection.Contains(character))
                        item.Selected = true;
                }

                // Auto-resize the columns
                charactersListView.Columns.Cast<ColumnHeader>().Where(
                    column => column.Index != charactersListView.Columns.Count - 1).ToList().ForEach(column => column.Width = -2);

                AdjustLastColumn();
            }
            finally
            {
                charactersListView.EndUpdate();
                charactersListView.SetVerticalScrollBarPosition(position);
            }

            // Forces a refresh of the enabled/disabled items
            UpdateControlsUsability();
        }

        /// <summary>
        /// Adds the groups that have characters.
        /// </summary>
        private void ArrangeByGroup(ListViewGroup noESIKeyGroup, ListViewGroup esiKeyGroup, ListViewGroup fileGroup,
            ListViewGroup urlGroup)
        {
            List<UriCharacter> uriCharacters = EveMonClient.Characters.OfType<UriCharacter>().ToList();
            List<CCPCharacter> ccpCharacters = EveMonClient.Characters.OfType<CCPCharacter>().ToList();

            if (ccpCharacters.Any(character => character.Identity.ESIKey == null))
                charactersListView.Groups.Add(noESIKeyGroup);

            if (ccpCharacters.Any(character => character.Identity.ESIKey != null))
                charactersListView.Groups.Add(esiKeyGroup);

            if (uriCharacters.Any(character => character.Uri.IsFile))
                charactersListView.Groups.Add(fileGroup);

            if (uriCharacters.Any(character => !character.Uri.IsFile))
                charactersListView.Groups.Add(urlGroup);
        }

        /// <summary>
        /// Adjusts the last column width.
        /// </summary>
        private void AdjustLastColumn()
        {
            ColumnHeader lastColumn = charactersListView.Columns[charactersListView.Columns.Count - 1];
            int pad = Size.Width - charactersListView.Size.Width;
            int width = charactersListView.Columns.Cast<ColumnHeader>().Where(column => column.Index != lastColumn.Index).Select(
                column => column.Width).Sum();

            int lastColumnMaxWidth = charactersListView.Columns[lastColumn.Index].ListView.Items.Cast<ListViewItem>().Select(
                item => TextRenderer.MeasureText(item.SubItems[lastColumn.Index].Text, Font).Width).Concat(
                    new[] { TextRenderer.MeasureText(charactersListView.Columns[lastColumn.Index].Text, Font).Width }).
                                         Concat(new[] { charactersListView.ClientSize.Width - width - pad }).Max() + pad;

            lastColumn.Width = lastColumnMaxWidth;
        }

        /// <summary>
        /// Updates the controls usability.
        /// </summary>
        private void UpdateControlsUsability()
        {
            // "Edit uri" enabled when an uri char is selected
            editUriMenu.Enabled = charactersListView.SelectedItems.Count > 0 &&
                                  charactersListView.SelectedItems[0].Tag is UriCharacter;

            // Delete char enabled if one character selected
            deleteCharacterMenu.Enabled = charactersListView.SelectedItems.Count > 0;
        }

        /// <summary>
        /// We monitor/unmonitor characters as they are checked.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void charactersListView_ItemChecked(object sender, ItemCheckedEventArgs e)
        {
            if (m_refreshingCharactersCounter != 0)
                return;

            // Add the character with changed monitoring status to the dictionary,
            // we will deal with them on closing
            Character character = (Character)e.Item.Tag;
            m_monitoredCharacters[character] = e.Item.Checked;
        }

        /// <summary>
        /// Handle the "delete" button.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void charactersListView_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Delete)
                deleteCharacterMenu_Click(sender, e);
        }

        /// <summary>
        /// On double click, we edit if this is an uri character.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void charactersListView_DoubleClick(object sender, EventArgs e)
        {
            editUriButton_Click(sender, e);
        }

        /// <summary>
        /// When the index changes, we enable or disable the toolbar buttons.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void charactersListView_SelectedIndexChanged(object sender, EventArgs e)
        {
            UpdateControlsUsability();
        }

        /// <summary>
        /// Characters toolbar > Import...
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void importCharacterMenu_Click(object sender, EventArgs e)
        {
            using (CharacterImportationWindow form = new CharacterImportationWindow())
            {
                form.ShowDialog(this);
            }
        }

        /// <summary>
        /// Characters toolbar > Delete...
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void deleteCharacterMenu_Click(object sender, EventArgs e)
        {
            // Retrieve the selected URI character
            if (charactersListView.SelectedItems.Count == 0)
                return;

            ListViewItem item = charactersListView.SelectedItems[0];
            Character character = item.Tag as Character;

            // Opens the character deletion
            using (CharacterDeletionWindow window = new CharacterDeletionWindow(character))
            {
                window.ShowDialog(this);
            }
        }

        /// <summary>
        /// Characters toolbar > Edit Uri...
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void editUriButton_Click(object sender, EventArgs e)
        {
            // Retrieve the selected URI character
            if (charactersListView.SelectedItems.Count == 0)
                return;

            ListViewItem item = charactersListView.SelectedItems[0];
            UriCharacter uriCharacter = item.Tag as UriCharacter;

            // Returns if the selected item is not an Uri character
            if (uriCharacter == null)
                return;

            // Opens the importation form
            using (CharacterImportationWindow form = new CharacterImportationWindow(uriCharacter))
            {
                form.ShowDialog(this);
            }
        }

        /// <summary>
        /// Characters toolbar > Group items
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void groupingMenu_Click(object sender, EventArgs e)
        {
            m_refreshingCharactersCounter++;
            UpdateCharactersListContent();
            m_refreshingCharactersCounter--;
        }

        #endregion


        #region Local Events

        /// <summary>
        /// Close on "close" button click.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void closeButton_Click(object sender, EventArgs e)
        {
            Close();
        }

        /// <summary>
        /// Handles the ColumnWidthChanging event of the charactersListView control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.Windows.Forms.ColumnWidthChangingEventArgs"/> instance containing the event data.</param>
        private void charactersListView_ColumnWidthChanging(object sender, ColumnWidthChangingEventArgs e)
        {
            e.Cancel = true;
            e.NewWidth = charactersListView.Columns[e.ColumnIndex].Width;
        }

        #endregion
    }
}