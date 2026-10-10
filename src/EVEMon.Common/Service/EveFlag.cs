using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using EVEMon.Common.Constants;
using EVEMon.Common.Helpers;
using EVEMon.Common.Models;
using EVEMon.Common.Net;
using EVEMon.Common.Serialization;

namespace EVEMon.Common.Service
{
    public static class EveFlag
    {
        private static Collection<SerializableEveFlagsListItem> s_eveFlags =
            new Collection<SerializableEveFlagsListItem>();
        private static bool s_isLoaded;
        private static Dictionary<string, short> s_flagIDsByName;
        private static bool s_queryPending;
        private static DateTime s_nextCheckTime;

        public const string Filename = "Flags";

        /// <summary>
        /// Gets the description of the flag.
        /// </summary>
        /// <param name="id">The flag id.</param>
        /// <returns></returns>
        internal static string GetFlagText(int id)
        {
            if (EveMonClient.IsDebugBuild)
                EnsureInitialized();
            else
                EnsureImportation();

            SerializableEveFlagsListItem flag = s_eveFlags?.FirstOrDefault(x => x.ID == id);
            return flag?.Text ?? EveMonConstants.UnknownText;
        }

        /// <summary>
        /// Ensures the eve flags data have been intialized.
        /// </summary>
        /// <summary>
        /// Gets the ID of a flag from its name (e.g. "Hangar", "HiSlot0"), as ESI returns it.
        /// </summary>
        /// <param name="flagName">The flag name.</param>
        /// <returns>The flag ID, or 0 (None) if unknown.</returns>
        internal static short GetFlagID(string flagName)
        {
            if (String.IsNullOrEmpty(flagName))
                return 0;

            if (s_flagIDsByName == null)
            {
                SerializableEveFlags flags = Util.DeserializeXmlFromString<SerializableEveFlags>(Properties.Resources.Flags,
                    APIProvider.RowsetsTransform);
                s_flagIDsByName = flags?.EVEFlags
                    .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(x => x.Key, x => (short)x.First().ID, StringComparer.OrdinalIgnoreCase)
                    ?? new Dictionary<string, short>();
            }

            short id;
            return s_flagIDsByName.TryGetValue(flagName, out id) ? id : (short)0;
        }

        private static void EnsureInitialized()
        {
            if (s_isLoaded)
                return;

            SerializableEveFlags result =
                Util.DeserializeXmlFromString<SerializableEveFlags>(Properties.Resources.Flags,
                    APIProvider.RowsetsTransform);

            Import(result);
        }

        /// <summary>
        /// Ensures the importation.
        /// </summary>
        private static void EnsureImportation()
        {
            // Quit if we already checked a minute ago or query is pending
            if (s_nextCheckTime > DateTime.UtcNow || s_queryPending)
                return;

            s_nextCheckTime = DateTime.UtcNow.AddMinutes(1);

            string filename = LocalXmlCache.GetFileInfo(Filename).FullName;

            // Update the file if we don't have it
            if (!File.Exists(filename))
            {
                // Use the copy shipped with EVEMon meanwhile
                EnsureInitialized();

                Task.WhenAll(UpdateFileAsync());
                return;
            }

            // Exit if we have already imported the list
            if (s_isLoaded)
                return;

            // Deserialize the xml file
            SerializableEveFlags result = Util.DeserializeXmlFromFile<SerializableEveFlags>(filename, APIProvider.RowsetsTransform);

            // In case the file has an error we prevent the importation
            if (result == null)
            {
                FileHelper.DeleteFile(filename);

                s_nextCheckTime = DateTime.UtcNow;

                return;
            }

            // Import the data
            Import(result);
        }

        /// <summary>
        /// Imports the specified result.
        /// </summary>
        /// <param name="result">The result.</param>
        private static void Import(SerializableEveFlags result)
        {
            if (result == null)
            {
                EveMonClient.Trace("failed");
                return;
            }

            EveMonClient.Trace("begin");

            s_eveFlags = result.EVEFlags;
            s_isLoaded = true;

            EveMonClient.Trace("done");
        }

        /// <summary>
        /// Updates the file.
        /// </summary>
        private static async Task UpdateFileAsync()
        {
            // Quit if query is pending
            if (s_queryPending)
                return;

            var url = new Uri($"{NetworkConstants.BitBucketWikiBase}" +
                              $"{NetworkConstants.EveFlags}");

            s_queryPending = true;

            DownloadResult<SerializableEveFlags> result =
                await Util.DownloadXmlAsync<SerializableEveFlags>(url, acceptEncoded: true, transform: APIProvider.RowsetsTransform);
            OnDownloaded(result);
        }

        /// <summary>
        /// Processes the queried eve flags.
        /// </summary>
        /// <param name="result">The result.</param>
        private static void OnDownloaded(DownloadResult<SerializableEveFlags> result)
        {
            if (result.Error != null)
            {
                // Reset query pending flag
                s_queryPending = false;

                EveMonClient.Trace(result.Error.Message);

                // Fallback
                EnsureInitialized();
                return;
            }

            // Import the list
            Import(result.Result);

            // Reset query pending flag
            s_queryPending = false;

            // Notify the subscribers
            EveMonClient.OnEveFlagsUpdated();

            // Save the file in cache
            LocalXmlCache.SaveAsync(Filename, Util.SerializeToXmlDocument(result.Result)).ConfigureAwait(false);
        }
    }
}
