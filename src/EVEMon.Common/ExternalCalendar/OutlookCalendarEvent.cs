using System;
using System.Collections;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using EVEMon.Common.Constants;
using EVEMon.Common.Helpers;

namespace EVEMon.Common.ExternalCalendar
{
    /// <summary>
    /// Class for handling MS Outlook calendar.
    /// </summary>
    /// <remarks>
    /// Outlook is driven through late-bound COM automation (<c>dynamic</c>), so no Office interop
    /// assemblies are required and any installed Outlook version is used.
    /// </remarks>
    public sealed class OutlookCalendarEvent : CalendarEvent
    {
        // Outlook object model constants
        private const int OlFolderCalendar = 9;         // OlDefaultFolders.olFolderCalendar
        private const int OlAppointmentItem = 1;        // OlItemType.olAppointmentItem
        private const int OlBusy = 2;                   // OlBusyStatus.olBusy
        private const int OlAppointmentClass = 26;      // OlObjectClass.olAppointment

        private static dynamic s_outlookApplication;
        private static dynamic s_mapiFolder;


        #region Properties

        /// <summary>
        /// Gets the Outlook application, or <c>null</c> if Outlook is not available.
        /// </summary>
        /// <value>The outlook application.</value>
        internal static dynamic OutlookApplication
        {
            get
            {
                try
                {
                    // Outlook may have been closed since we connected to it
                    if (s_outlookApplication != null && !IsConnected())
                    {
                        s_outlookApplication = null;
                        s_mapiFolder = null;
                    }

                    if (s_outlookApplication == null)
                    {
                        Type outlookType = Type.GetTypeFromProgID("Outlook.Application");
                        if (outlookType != null)
                            s_outlookApplication = Activator.CreateInstance(outlookType);
                    }
                }
                catch (COMException ex)
                {
                    ExceptionHandler.LogException(ex, true);
                }
                catch (ArgumentException ex)
                {
                    ExceptionHandler.LogException(ex, true);
                }
                return s_outlookApplication;
            }
        }

        #endregion


        #region Helper Methods

        /// <summary>
        /// Checks whether the Outlook instance we hold is still running.
        /// </summary>
        /// <returns></returns>
        private static bool IsConnected()
        {
            try
            {
                string version = s_outlookApplication.Version;
                return version != null;
            }
            catch (COMException)
            {
                return false;
            }
            catch (InvalidComObjectException)
            {
                return false;
            }
        }

        #endregion


        #region Internal Methods

        /// <summary>
        /// Add a new event or Update the appropriate event in the calendar.
        /// </summary>
        /// <param name="eventExists">if set to <c>true</c> the event exists.</param>
        /// <param name="queuePosition">The queue position.</param>
        /// <param name="lastSkillInQueue">if set to <c>true</c> skill is the last in queue.</param>
        internal override Task AddOrUpdateEventAsync(bool eventExists, int queuePosition, bool lastSkillInQueue)
            => TaskHelper.RunIOBoundTaskAsync(() =>
            {
                dynamic eventItem = eventExists
                    ? Events[0]
                    : s_mapiFolder.Items.Add(OlAppointmentItem);

                eventItem.Subject = Subject;
                eventItem.Start = StartDate;
                eventItem.End = EndDate;

                string queuePositionText = lastSkillInQueue
                    ? "End Of Queue"
                    : queuePosition.ToString(CultureConstants.DefaultCulture);

                eventItem.Body = eventExists
                    ? $"{eventItem.Body} {Environment.NewLine}Updated: {DateTime.Now} Queue Position: {queuePositionText}"
                    : $"Added: {DateTime.Now} Queue Position: {queuePositionText}";

                eventItem.ReminderSet = ItemReminder || AlternateReminder;
                eventItem.BusyStatus = OlBusy;
                eventItem.AllDayEvent = false;
                eventItem.Location = String.Empty;

                if (AlternateReminder)
                {
                    EarlyReminder = new DateTime(StartDate.Year,
                        StartDate.Month,
                        StartDate.Day,
                        EarlyReminder.Hour,
                        EarlyReminder.Minute,
                        EarlyReminder.Second);

                    LateReminder = new DateTime(StartDate.Year,
                        StartDate.Month,
                        StartDate.Day,
                        LateReminder.Hour,
                        LateReminder.Minute,
                        LateReminder.Second);

                    DateTime dateTimeAlternateReminder = WorkOutAlternateReminders();

                    // Subtract the reminder time from the event time
                    TimeSpan timeSpan = ((DateTime)eventItem.Start).Subtract(dateTimeAlternateReminder);
                    eventItem.ReminderMinutesBeforeStart = Math.Abs(timeSpan.Hours * 60 + timeSpan.Minutes);
                    Minutes = eventItem.ReminderMinutesBeforeStart;
                }

                eventItem.ReminderMinutesBeforeStart = eventItem.ReminderSet ? Minutes : 0;
                eventItem.Save();
            });

        /// <summary>
        /// Get the relevant event item and populate the details.
        /// </summary>
        /// <returns>
        /// 	<c>true</c> if an event is found, <c>false</c> otherwise.
        /// </returns>
        internal override bool GetEvent()
        {
            if (Events.Count < 1)
                return false;

            dynamic eventItem = Events[0];
            StartDate = eventItem.Start;
            EndDate = eventItem.End;
            Subject = eventItem.Subject;
            ItemReminder = eventItem.ReminderSet;
            Minutes = eventItem.ReminderMinutesBeforeStart;
            return true;
        }

        /// <summary>
        /// Pull all the events and populate the event array.
        /// </summary>
        internal override Task ReadEventsAsync()
            => TaskHelper.RunIOBoundTaskAsync(() =>
            {
                Events.Clear();
                Events.AddRange(GetEventItems());
            });

        /// <summary>
        /// Delete the specified event.
        /// </summary>
        /// <param name="eventIndex">The event index.</param>
        internal override Task DeleteEventAsync(int eventIndex)
            => TaskHelper.RunIOBoundTaskAsync(() =>
            {
                ((dynamic)Events[eventIndex]).Delete();
            });

        /// <summary>
        /// Gets true if the Outlook calendar exist.
        /// </summary>
        /// <param name="useDefaultCalendar">if set to <c>true</c> [use default calendar].</param>
        /// <param name="path">The path.</param>
        /// <returns></returns>
        internal static bool OutlookCalendarExist(bool useDefaultCalendar, string path = null)
        {
            s_mapiFolder = null;
            return GetMapiFolder(OutlookApplication.Session.Folders, useDefaultCalendar, path);
        }

        /// <summary>
        /// Gets the mapi folder.
        /// </summary>
        /// <param name="folders">The folders.</param>
        /// <param name="useDefaultCalendar">if set to <c>true</c> [use default calendar].</param>
        /// <param name="path">The path.</param>
        /// <returns></returns>
        private static bool GetMapiFolder(dynamic folders, bool useDefaultCalendar = false, string path = null)
        {
            if (useDefaultCalendar)
            {
                s_mapiFolder = OutlookApplication.Session.GetDefaultFolder(OlFolderCalendar);
                return s_mapiFolder != null;
            }

            if (String.IsNullOrWhiteSpace(path))
                path = Settings.Calendar.OutlookCustomCalendarPath;

            if (!path.StartsWith(@"\\", StringComparison.Ordinal))
                return s_mapiFolder != null;

            string pathRoot = GetFolderPathRoot(path);

            // Outlook collections are 1-based
            int count = folders.Count;
            for (int i = 1; i <= count && s_mapiFolder == null; i++)
            {
                dynamic folder = folders.Item(i);
                string folderPath = folder.FolderPath;

                if (GetFolderPathRoot(folderPath) != pathRoot)
                    continue;

                if (folder.DefaultItemType == OlAppointmentItem && folderPath == path)
                {
                    s_mapiFolder = folder;
                    break;
                }

                if (folder.Folders.Count > 0)
                    GetMapiFolder(folder.Folders, path: path);
            }

            return s_mapiFolder != null && s_mapiFolder.FolderPath == path;
        }

        #endregion


        #region Private Methods

        /// <summary>
        /// Get events matching the subject.
        /// </summary>
        /// <returns>
        /// Array of relevant events or empty array.
        /// </returns>
        private ArrayList GetEventItems()
        {
            // Use a Jet Query to filter the details we need initially between the two specified dates
            string dateFilter = $"[Start] >= '{StartDate:g}' and [End] <= '{EndDate:g}'";
            dynamic calendarItems = s_mapiFolder.Items.Restrict(dateFilter);
            calendarItems.Sort("[Start]", Type.Missing);
            calendarItems.IncludeRecurrences = true;

            // Must use 'like' comparison for Find/FindNext
            string subjectFilter = !String.IsNullOrEmpty(Subject)
                ? $"@SQL=\"urn:schemas:httpmail:subject\" like '%{Subject.Replace("'", "''")}%'"
                : "@SQL=\"urn:schemas:httpmail:subject\" <> '!@#'";

            // Use Find and FindNext methods to get all the items
            ArrayList resultArray = new ArrayList();
            dynamic eventItem = calendarItems.Find(subjectFilter);
            while (eventItem != null)
            {
                // Only keep appointments (Find can also match meeting requests, tasks, ...)
                if (eventItem.Class == OlAppointmentClass)
                    resultArray.Add(eventItem);

                // Find the next event
                eventItem = calendarItems.FindNext();
            }

            return resultArray;
        }

        /// <summary>
        /// Gets the folder path root.
        /// </summary>
        /// <param name="folderPath">The folder path.</param>
        /// <returns></returns>
        private static string GetFolderPathRoot(string folderPath)
        {
            // Strip header directory seperator characters
            folderPath = folderPath.Remove(0, 2);

            // Find the index of a directory seperator character
            int index = folderPath.IndexOf(Path.DirectorySeparatorChar, 0);

            // Reconstruct the root path according to the index found
            return $"\\{(index > 0 ? folderPath.Substring(0, index) : folderPath)}";
        }

        #endregion

    }
}