using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using System.Windows.Forms.VisualStyles;
using EVEMon.Common;
using EVEMon.Common.Constants;
using EVEMon.Common.Controls;
using EVEMon.Common.Enumerations;
using EVEMon.Common.Esi;
using EVEMon.Common.Factories;
using EVEMon.Common.Models;
using EVEMon.Common.Properties;

namespace EVEMon.ApiCredentialsManagement
{
    /// <summary>
    /// Displays a list of API keys.
    /// </summary>
    public sealed class ApiKeysListBox : NoFlickerListBox
    {
        private readonly Font m_smallFont;
        private readonly Font m_smallBoldFont;
        private readonly Font m_middleFont;
        private readonly Font m_boldFont;

        private readonly List<ESIKey> m_esiKeys = new List<ESIKey>();
        private bool m_pendingUpdate;

        /// <summary>
        /// Constructor.
        /// </summary>
        public ApiKeysListBox()
        {
            DrawMode = DrawMode.OwnerDrawFixed;
            DrawItem += OnDrawItem;
            
            m_smallFont = FontFactory.GetFont(Font.FontFamily, 6.5f);
            m_smallBoldFont = FontFactory.GetFont(m_smallFont, FontStyle.Bold);
            m_middleFont = FontFactory.GetFont(Font.FontFamily, 8.0f);
            m_boldFont = FontFactory.GetFont(Font, FontStyle.Bold);
        }

        /// <summary>
        /// Releases the unmanaged resources used by the <see cref="T:System.Windows.Forms.Control"/> 
        /// and its child controls and optionally releases the managed resources.
        /// </summary>
        /// <param name="disposing">true to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
        protected override void Dispose(bool disposing)
        {
            DrawItem -= OnDrawItem;
            base.Dispose(disposing);
        }

        /// <summary>
        /// Gets or sets the enumeration of displayed API keys.
        /// </summary>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Content)]
        public IEnumerable<ESIKey> ESIKeys
        {
            get { return m_esiKeys; }
            set
            {
                m_esiKeys.Clear();
                if (value != null)
                    m_esiKeys.AddRange(value);

                UpdateContent();
            }
        }

        internal static Size CheckBoxSize => new Size(12, 12);

        /// <summary>
        /// Updates the content.
        /// </summary>
        private void UpdateContent()
        {
            if (!Visible)
            {
                m_pendingUpdate = true;
                return;
            }
            m_pendingUpdate = false;

            int scrollBarPosition = TopIndex;
            ESIKey oldSelection = SelectedItem as ESIKey;

            BeginUpdate();
            try
            {
                Items.Clear();
                foreach (ESIKey esiKey in ESIKeys)
                {
                    Items.Add(esiKey);
                    if (esiKey == oldSelection)
                        SelectedIndex = Items.Count - 1;
                }
            }
            finally
            {
                EndUpdate();
                TopIndex = scrollBarPosition;
            }
        }

        /// <summary>
        /// When the visibility changes, we check whether there is an update pending.
        /// </summary>
        /// <param name="e"></param>
        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);

            if (Visible && m_pendingUpdate)
                UpdateContent();
        }

        /// <summary>
        /// Draws the API key info.
        /// </summary>
        /// <param name="sender">The sender.</param>
        /// <param name="e">The <see cref="System.Windows.Forms.DrawItemEventArgs"/> instance containing the event data.</param>
        private void OnDrawItem(object sender, DrawItemEventArgs e)
        {
            ItemHeight = m_boldFont.Height + m_middleFont.Height + m_smallBoldFont.Height * 2;

            // Background
            Graphics g = e.Graphics;
            bool isSelected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            Brush fontBrush = isSelected ? SystemBrushes.HighlightText : SystemBrushes.ControlText;
            e.DrawBackground();

            if (e.Index < 0 || e.Index >= Items.Count)
                return;

            ESIKey esiKey = (ESIKey)Items[e.Index];
            Image icon = GetIcon(esiKey);

            Margin = new Padding((ItemHeight - icon.Height) / 2);
            int left = e.Bounds.Left + Margin.Left;
            int top = e.Bounds.Top + Margin.Top / 4;

            // Draws the checbox
            CheckBoxRenderer.DrawCheckBox(g, new Point(left, (ItemHeight - CheckBoxSize.Height) / 2),
                                          esiKey.Monitored ? CheckBoxState.CheckedNormal : CheckBoxState.UncheckedNormal);
            left += CheckBoxSize.Width + Margin.Left * 2;

            // Draws the picture of the authorization state
            g.DrawImage(icon, new Rectangle(left, top + Margin.Top, icon.Width, icon.Height));

            // Texts drawing
            DrawTexts(g, esiKey, left, top, fontBrush, icon);

            e.DrawFocusRectangle();
        }

        private void DrawTexts(Graphics g, ESIKey esiKey, int left, int top, Brush fontBrush, Image icon)
        {
            // Draws the texts on the upper third
            left += icon.Width + Margin.Left;

            // Character name and ID
            g.DrawString(esiKey.CharacterName, m_boldFont, fontBrush, new Point(left, top + 2));
            int indentedLeft = left + g.MeasureString(esiKey.CharacterName, m_boldFont).ToSize().Width + Margin.Left * 2;

            string characterID = $"Character ID: {esiKey.ID.ToString(CultureConstants.InvariantCulture)}";
            g.DrawString(characterID, Font, fontBrush, new Point(indentedLeft, top));

            // Draw the texts on the middle third
            top += g.MeasureString(characterID, Font).ToSize().Height;

            string statusHeader = "EVE SSO";
            g.DrawString(statusHeader, m_boldFont, fontBrush, new Point(left, top));
            indentedLeft = left + g.MeasureString(statusHeader, m_boldFont).ToSize().Width + Margin.Left * 2;

            Color statusColor;
            string status = GetStatusText(esiKey, out statusColor);
            using (Brush statusBrush = new SolidBrush(statusColor))
            {
                g.DrawString(status, m_middleFont, statusBrush, new Point(indentedLeft, top));
            }

            // Draws the texts on the lower third
            top += g.MeasureString(status, m_middleFont).ToSize().Height;

            int granted = EsiConfiguration.Scopes.Count(esiKey.HasScope);
            string details = String.IsNullOrEmpty(esiKey.LastError) || esiKey.State == ESIKeyState.Valid
                ? $"{granted} of {EsiConfiguration.Scopes.Count} permissions granted"
                : esiKey.LastError;

            CCPCharacter ccpCharacter = esiKey.CharacterIdentity?.CCPCharacter;
            Font font = ccpCharacter != null && ccpCharacter.Monitored ? m_smallBoldFont : m_smallFont;
            g.DrawString(details, font, fontBrush, new Point(left, top));
        }

        private static string GetStatusText(ESIKey esiKey, out Color color)
        {
            switch (esiKey.State)
            {
                case ESIKeyState.NeedsAuthorization:
                    color = Color.Red;
                    return "Must be authorized again (Edit)";
                case ESIKeyState.Error:
                    color = Color.DarkOrange;
                    return "Last check failed, will retry";
                default:
                    if (esiKey.HasMissingScopes)
                    {
                        color = Color.DarkOrange;
                        return "Authorized, but missing permissions (Edit to grant them)";
                    }

                    color = Color.DarkGreen;
                    return esiKey.State == ESIKeyState.Valid ? "Authorized" : "Not checked yet";
            }
        }

        private static Image GetIcon(ESIKey esiKey)
            => esiKey.State == ESIKeyState.NeedsAuthorization
                ? Resources.KeyWrong32
                : Resources.DefaultCharacterImage32;
    }
}
