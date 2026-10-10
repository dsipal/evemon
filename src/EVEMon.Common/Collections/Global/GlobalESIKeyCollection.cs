using System;
using System.Collections.Generic;
using System.Linq;
using EVEMon.Common.Enumerations;
using EVEMon.Common.Esi;
using EVEMon.Common.Extensions;
using EVEMon.Common.Helpers;
using EVEMon.Common.Models;
using EVEMon.Common.Serialization.Settings;

namespace EVEMon.Common.Collections.Global
{
    /// <summary>
    /// The characters authorized through EVE SSO, keyed by character ID.
    /// </summary>
    public class GlobalESIKeyCollection : ReadonlyKeyedCollection<long, ESIKey>
    {
        #region Indexer

        /// <summary>
        /// Gets the ESI key of the given character, or null.
        /// </summary>
        /// <param name="characterID">The character ID.</param>
        public ESIKey this[long characterID] => GetByKey(characterID);

        #endregion


        #region Addition / Removal Methods

        /// <summary>
        /// Adds a newly authorized character, or updates its key if it was already authorized,
        /// and creates the character if it is not known yet.
        /// </summary>
        /// <param name="token">The tokens returned by EVE SSO.</param>
        /// <returns>The ESI key.</returns>
        public ESIKey AddOrUpdate(SsoToken token)
        {
            token.ThrowIfNull(nameof(token));

            ESIKey esiKey = this[token.CharacterID];
            if (esiKey != null)
            {
                esiKey.Update(token);
            }
            else
            {
                esiKey = new ESIKey(token);
                Items.Add(esiKey.ID, esiKey);
                esiKey.LinkIdentity();
                EveMonClient.OnESIKeyCollectionChanged();
            }

            // Create the character, or monitor it again if it was deleted earlier
            CharacterIdentity identity = esiKey.CharacterIdentity;
            if (identity.CCPCharacter == null)
                EveMonClient.Characters.Add(new CCPCharacter(identity));

            EveMonClient.OnESIKeyInfoUpdated(esiKey);
            return esiKey;
        }

        /// <summary>
        /// Removes the given key; its character stays, with its last known data.
        /// </summary>
        /// <param name="esiKey">The ESI key.</param>
        public void Remove(ESIKey esiKey)
        {
            esiKey.ThrowIfNull(nameof(esiKey));

            CharacterIdentity identity = esiKey.CharacterIdentity;
            if (identity != null && identity.ESIKey == esiKey)
            {
                identity.ESIKey = null;
                if (identity.CCPCharacter != null)
                    EveMonClient.OnCharacterUpdated(identity.CCPCharacter);
            }

            if (!Items.Remove(esiKey.ID))
                throw new InvalidOperationException("This ESI key does not exist in the list.");

            esiKey.Dispose();
            EveMonClient.Notifications.InvalidateESIKeyError(esiKey);
            EveMonClient.OnESIKeyCollectionChanged();
        }

        #endregion


        #region Import / Export Methods

        /// <summary>
        /// Imports the keys from the settings.
        /// </summary>
        /// <param name="serial">The serialized keys.</param>
        internal void Import(IEnumerable<SerializableESIKey> serial)
        {
            foreach (ESIKey esiKey in Items.Values)
            {
                esiKey.Dispose();
            }

            Items.Clear();
            foreach (SerializableESIKey serialKey in serial)
            {
                try
                {
                    ESIKey esiKey = new ESIKey(serialKey);
                    Items.Add(esiKey.ID, esiKey);
                    esiKey.LinkIdentity();

                    // e.g. settings copied from another computer: the token can't be decrypted
                    if (esiKey.State == ESIKeyState.NeedsAuthorization)
                        EveMonClient.Notifications.NotifyESIKeyError(esiKey, esiKey.LastError);
                }
                catch (ArgumentException ex)
                {
                    EveMonClient.Trace($"An ESI key for character {serialKey.ID} already existed; additional instance ignored.");
                    ExceptionHandler.LogException(ex, true);
                }
            }

            EveMonClient.OnESIKeyCollectionChanged();
        }

        /// <summary>
        /// Exports the keys to the settings.
        /// </summary>
        internal IEnumerable<SerializableESIKey> Export() => Items.Values.Select(esiKey => esiKey.Export());

        #endregion
    }
}
