using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using EVEMon.Common;
using EVEMon.Common.Controls;
using EVEMon.Common.Esi;
using EVEMon.Common.Models;

namespace EVEMon.ApiCredentialsManagement
{
    /// <summary>
    /// Authorizes a character with EVE SSO: opens the EVE login page in the browser and waits for the result.
    /// </summary>
    public sealed class EsiAuthorizationWindow : EVEMonForm
    {
        private readonly ESIKey m_esiKey;
        private readonly Label m_messageLabel;
        private readonly ProgressBar m_progressBar;
        private readonly Button m_retryButton;
        private readonly Button m_cancelButton;
        private CancellationTokenSource m_cancellation;

        /// <summary>
        /// Constructor to authorize a new character.
        /// </summary>
        public EsiAuthorizationWindow()
            : this(null)
        {
        }

        /// <summary>
        /// Constructor to authorize a character again (e.g. after the authorization was revoked).
        /// </summary>
        /// <param name="esiKey">The ESI key to authorize again, or null for a new character.</param>
        public EsiAuthorizationWindow(ESIKey esiKey)
        {
            m_esiKey = esiKey;

            Text = esiKey == null ? "Add Character" : $"Authorize {esiKey.CharacterName}";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(440, 150);

            m_messageLabel = new Label
            {
                Location = new Point(12, 12),
                Size = new Size(416, 64),
                AutoSize = false
            };

            m_progressBar = new ProgressBar
            {
                Location = new Point(12, 82),
                Size = new Size(416, 18),
                Style = ProgressBarStyle.Marquee
            };

            m_retryButton = new Button
            {
                Text = "&Try Again",
                Location = new Point(272, 114),
                Size = new Size(75, 23),
                Visible = false
            };
            m_retryButton.Click += RetryButton_Click;

            m_cancelButton = new Button
            {
                Text = "&Cancel",
                Location = new Point(353, 114),
                Size = new Size(75, 23),
                DialogResult = DialogResult.Cancel
            };

            CancelButton = m_cancelButton;
            Controls.AddRange(new Control[] { m_messageLabel, m_progressBar, m_retryButton, m_cancelButton });
        }

        /// <summary>
        /// Gets the authorized ESI key, once the authorization succeeded.
        /// </summary>
        public ESIKey AuthorizedKey { get; private set; }

        /// <summary>
        /// Starts the authorization when the window is shown.
        /// </summary>
        protected override async void OnShown(EventArgs e)
        {
            base.OnShown(e);
            await StartAuthorizationAsync();
        }

        /// <summary>
        /// Starts the authorization again after a failure.
        /// </summary>
        private async void RetryButton_Click(object sender, EventArgs e)
        {
            await StartAuthorizationAsync();
        }

        /// <summary>
        /// Cancels a pending authorization when the window closes.
        /// </summary>
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            m_cancellation?.Cancel();
            base.OnFormClosed(e);
        }

        private async Task StartAuthorizationAsync()
        {
            if (!EsiConfiguration.IsClientIdConfigured)
            {
                ShowError("EVEMon has no EVE SSO application configured, so characters can't be authorized. " +
                          "See EsiConfiguration.cs for how to register one.", false);
                return;
            }

            m_cancellation?.Dispose();
            m_cancellation = new CancellationTokenSource();

            m_messageLabel.ForeColor = SystemColors.ControlText;
            m_messageLabel.Text = "Your browser has opened the EVE Online login page. " +
                                  "Log in, choose the character to add to EVEMon and authorize it." +
                                  Environment.NewLine + Environment.NewLine +
                                  "Waiting for the login to complete...";
            m_progressBar.Visible = true;
            m_retryButton.Visible = false;

            SsoToken token;
            try
            {
                token = await SsoClient.AuthorizeAsync(m_cancellation.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (SsoException ex)
            {
                if (!IsDisposed)
                    ShowError(ex.Message, true);
                return;
            }

            if (IsDisposed)
                return;

            if (m_esiKey != null && token.CharacterID != m_esiKey.ID)
            {
                DialogResult answer = MessageBox.Show(this,
                    $"You authorized {token.CharacterName} instead of {m_esiKey.CharacterName}." +
                    $"{Environment.NewLine}Add {token.CharacterName} to EVEMon?",
                    Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question);

                if (answer != DialogResult.Yes)
                {
                    ShowError($"{m_esiKey.CharacterName} was not authorized.", true);
                    return;
                }
            }

            AuthorizedKey = EveMonClient.ESIKeys.AddOrUpdate(token);

            DialogResult = DialogResult.OK;
            Close();
        }

        private void ShowError(string message, bool canRetry)
        {
            m_progressBar.Visible = false;
            m_messageLabel.ForeColor = Color.Red;
            m_messageLabel.Text = message;
            m_retryButton.Visible = canRetry;
            m_cancelButton.Text = "&Close";
        }

        /// <summary>
        /// Disposes the resources.
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
                m_cancellation?.Dispose();

            base.Dispose(disposing);
        }
    }
}
