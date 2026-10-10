[![GPL licensed](https://img.shields.io/badge/license-GPL%20v2-blue.svg)]()
[![GitHub tag](https://img.shields.io/github/tag/evemondevteam/evemon.svg)]()

[![Open Hub](https://www.openhub.net/p/evemon/widgets/project_thin_badge.gif)](https://www.openhub.net/p/evemon)

[![Twitter Follow](https://img.shields.io/twitter/follow/EVEMon.svg?style=social)](https://twitter.com/evemon)

# **EVEMon** 

A lightweight, easy-to-use standalone Windows application designed to assist you in keeping track of your EVE Online character progression.

## Building

EVEMon targets .NET 10 (`net10.0-windows`). Only the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) is required; Visual Studio is optional.

```shell
dotnet build EVEMon.sln
dotnet test EVEMon.sln
```

To produce a release, run the install builder. It publishes EVEMon as a self-contained win-x64 application
(no .NET runtime needed on the user's machine), then creates the NSIS installer and the binaries zip under
`src\EVEMon\bin\Installbuilder\`:

```shell
dotnet run --project tools/InstallBuilder -c Release
```

Use `-c Snapshot` instead for a snapshot zip. Release and Snapshot builds stamp `SharedAssemblyInfo.cs` with the git commit count.

### EVE SSO application

Characters are authorized with EVE SSO (OAuth 2.0 with PKCE), which needs an application registered at
<https://developers.eveonline.com/>:

- **Connection type:** Authentication & API Access
- **Permissions:** the scopes listed in `src/EVEMon.Common/Esi/EsiConfiguration.cs` (`EsiConfiguration.Scopes`)
- **Callback URL:** `http://localhost:52714/callback`

Put the application's Client ID in `DefaultClientId` in `EsiConfiguration.cs`, or set the `EVEMON_ESI_CLIENT_ID`
environment variable to try one without rebuilding. No client secret is used.

For complete info on *How To Contribute* please visit the [wiki](https://bitbucket.org/EVEMonDevTeam/evemon/wiki) section

**Owner:** 

[EVEMonDevTeam](https://github.com/evemondevteam/) (GitHub)

[EVEMonDevTeam](https://bitbucket.org/EVEMonDevTeam) (Bitbucket)

**Website:** [EVEMon](https://evemondevteam.github.io/evemon/)

**Documentation:** [EVEMon Docs](https://evemon.readthedocs.org/)

**Feedback & Ideas:** [On UserVoice](https://evemon.uservoice.com/)

**Admins:**

[JimiC](https://github.com/JimiC) (GitHub)

[Jimi C](https://bitbucket.org/Desmont_McCallock) (Bitbucket)
