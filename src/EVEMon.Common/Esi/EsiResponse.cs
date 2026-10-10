using System;

namespace EVEMon.Common.Esi
{
    /// <summary>
    /// The result of an ESI request.
    /// </summary>
    /// <typeparam name="T">The type of the JSON response.</typeparam>
    internal sealed class EsiResponse<T>
    {
        internal EsiResponse(T result, DateTime expires, DateTime serverTime, int pages)
        {
            Result = result;
            Expires = expires;
            ServerTime = serverTime;
            Pages = pages;
        }

        /// <summary>
        /// Gets the deserialized response.
        /// </summary>
        public T Result { get; }

        /// <summary>
        /// Gets when the response stops being cached by ESI (UTC).
        /// </summary>
        public DateTime Expires { get; }

        /// <summary>
        /// Gets the server time of the response (UTC).
        /// </summary>
        public DateTime ServerTime { get; }

        /// <summary>
        /// Gets the number of pages of a paginated route.
        /// </summary>
        public int Pages { get; }
    }
}
