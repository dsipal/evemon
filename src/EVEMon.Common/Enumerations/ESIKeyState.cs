namespace EVEMon.Common.Enumerations
{
    /// <summary>
    /// The state of a character's EVE SSO authorization.
    /// </summary>
    public enum ESIKeyState
    {
        /// <summary>
        /// Not checked yet.
        /// </summary>
        Unknown,

        /// <summary>
        /// The authorization works.
        /// </summary>
        Valid,

        /// <summary>
        /// The authorization was revoked or can't be used; the character must be authorized again.
        /// </summary>
        NeedsAuthorization,

        /// <summary>
        /// The last check failed (e.g. EVE SSO unreachable); it will be retried.
        /// </summary>
        Error
    }
}
