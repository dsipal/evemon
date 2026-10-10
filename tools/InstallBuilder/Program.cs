using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using ICSharpCode.SharpZipLib.Zip;

namespace EVEMon.InstallBuilder
{
    internal static class Program
    {
        private static string s_evemonOutputDir;
        private static string s_sourceFilesDir;
        private static string s_outputPath;
        private static string s_installerDir;
        private static string s_snapshotDir;
        private static string s_binariesDir;
        private static string s_solutionDir;
        private static string s_projectDir;
        private static string s_nsisExe;
        private static bool s_isSnapshot;

        private static FileVersionInfo s_fileVersionInfo;

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        /// <param name="args">The args.</param>
        /// <returns></returns>
        [STAThread]
        private static int Main(string[] args)
        {
            CheckIsSnapshot();

            bool versionOnly = args.Any() && args[0].StartsWith("-v", StringComparison.OrdinalIgnoreCase);

            // Publish EVEMon (self-contained) into the folder the installer and zip are built from
            if (!versionOnly && PublishEVEMon() != 0)
                return 1;

            // Sign the published binaries before they are packaged (no-op unless signing is configured)
            if (!versionOnly && SignPublishedFiles() != 0)
                return 1;

            if (!HasVersion())
                return 1;

            if (args.Any())
            {
                if (args[0] == "-version" || args[0] == "-v")
                {
                    Console.WriteLine(s_fileVersionInfo.ProductVersion);
                    return 0;
                }

                if (args[0] == "-version=tc" || args[0] == "-v=tc")
                {
                    Console.WriteLine("##teamcity[buildNumber '{0}']", s_fileVersionInfo.ProductVersion);
                    return 0;
                }
            }

            try
            {
                if (CheckNsisPresent() && !s_isSnapshot)
                {
                    // Create the appropriate folder if it doesn't exist
                    if (!Directory.Exists(InstallerDirectory))
                        Directory.CreateDirectory(InstallerDirectory);

                    // Create an installer in the appropriate folder
                    Console.WriteLine("Starting Installer creation.");
                    if (BuildInstaller() != 0)
                        return 1;
                    if (SignFiles(Directory.GetFiles(InstallerDirectory, "*.exe")) != 0)
                        return 1;
                    Console.WriteLine("Installer creation finished.");
                    Console.WriteLine();
                }

                // Create the appropriate folder if it doesn't exist
                string directory = s_isSnapshot ? SnapshotDirectory : BinariesDirectory;
                if (!Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                // Create a zip file in the appropriate folder
                string description = s_isSnapshot ? "Snapshot" : "Binaries";
                Console.WriteLine("Starting {0} Zip creation.", description);
                if (BuildZip() != 0)
                    return 1;
                Console.WriteLine("{0} Zip creation finished.", description);
                Console.WriteLine("Done");

                if (Debugger.IsAttached)
                    Console.ReadLine();

                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("An error occurred: {0} in {1}", ex.Message, ex.Source);
                Console.WriteLine();
                Console.WriteLine(ex.StackTrace);
                if (Debugger.IsAttached)
                    Console.ReadLine();
                return 1;
            }
        }

        /// <summary>
        /// Gets the solution directory.
        /// </summary>
        /// <returns></returns>
        private static string SolutionDirectory
        {
            get
            {
                if (String.IsNullOrWhiteSpace(s_solutionDir))
                {
                    s_solutionDir = Regex.Match(AppContext.BaseDirectory, @"[a-zA-Z]+:.*\\(?=tools)",
                        RegexOptions.Compiled | RegexOptions.IgnoreCase).ToString();
                }
                return s_solutionDir;
            }
        }

        /// <summary>
        /// Gets the project directory.
        /// </summary>
        /// <returns></returns>
        private static string ProjectDirectory
        {
            get
            {
                if (String.IsNullOrWhiteSpace(s_projectDir))
                {
                    s_projectDir = Regex.Match(AppContext.BaseDirectory, @"[a-zA-Z]+:.*\\(?=bin)",
                        RegexOptions.Compiled | RegexOptions.IgnoreCase).ToString();
                }
                return s_projectDir;
            }
        }

        /// <summary>
        /// Gets the output path.
        /// </summary>
        /// <returns></returns>
        private static string OutputPath
        {
            get
            {
                if (String.IsNullOrWhiteSpace(s_outputPath))
                {
                    s_outputPath = AppContext.BaseDirectory.Remove(0, ProjectDirectory.Length);
                }
                return s_outputPath;
            }
        }

        /// <summary>
        /// Gets the EVEMon build output directory (e.g. src\EVEMon\bin\Release\).
        /// </summary>
        /// <returns></returns>
        private static string EVEMonOutputDirectory
        {
            get
            {
                if (String.IsNullOrWhiteSpace(s_evemonOutputDir))
                {
                    s_evemonOutputDir = Path.GetFullPath(Path.Combine(SolutionDirectory, @"src\EVEMon\", OutputPath));
                }
                return s_evemonOutputDir;
            }
        }

        /// <summary>
        /// Gets the source files directory, where EVEMon is published to.
        /// </summary>
        /// <returns></returns>
        private static string SourceFilesDirectory
        {
            get
            {
                if (String.IsNullOrWhiteSpace(s_sourceFilesDir))
                {
                    s_sourceFilesDir = Path.Combine(EVEMonOutputDirectory, @"publish\");
                }
                return s_sourceFilesDir;
            }
        }

        /// <summary>
        /// Gets the installer directory.
        /// </summary>
        /// <returns></returns>
        private static string InstallerDirectory
        {
            get
            {
                if (String.IsNullOrWhiteSpace(s_installerDir))
                {
                    s_installerDir = GetInstallbuilderDirectory("Installer");
                }
                return s_installerDir;
            }
        }

        /// <summary>
        /// Gets the snapshot directory.
        /// </summary>
        /// <returns></returns>
        private static string SnapshotDirectory
        {
            get
            {
                if (String.IsNullOrWhiteSpace(s_snapshotDir))
                {
                    s_snapshotDir = GetInstallbuilderDirectory("Snapshot");
                }
                return s_snapshotDir;
            }
        }

        /// <summary>
        /// Gets the binaries directory.
        /// </summary>
        /// <returns></returns>
        private static string BinariesDirectory
        {
            get
            {
                if (String.IsNullOrWhiteSpace(s_binariesDir))
                {
                    s_binariesDir = GetInstallbuilderDirectory("Binaries");
                }
                return s_binariesDir;
            }
        }

        /// <summary>
        /// Gets the installbuilder directory.
        /// </summary>
        /// <param name="directory">The directory.</param>
        /// <returns></returns>
        private static string GetInstallbuilderDirectory(string directory)
            => Path.GetFullPath(
                Regex.Replace(EVEMonOutputDirectory, "Debug|Release",
                    Path.Combine("Installbuilder", directory),
                    RegexOptions.Compiled | RegexOptions.IgnoreCase));

        /// <summary>
        /// Checks the configuration is Snapshot.
        /// </summary>
        [Conditional("SNAPSHOT")]
        private static void CheckIsSnapshot()
        {
            s_isSnapshot = true;
        }

        /// <summary>
        /// Checks that NSIS is present.
        /// </summary>
        private static bool CheckNsisPresent()
        {
            s_nsisExe = FindMakeNsisExe();
            Console.WriteLine("NSIS : {0}", String.IsNullOrEmpty(s_nsisExe)
                ? "Not Found - Installer will not be created."
                : s_nsisExe);

            Console.WriteLine();

            Console.WriteLine("Project directory : {0}", ProjectDirectory);
            Console.WriteLine("Source directory : {0}", SourceFilesDirectory);
            if (s_isSnapshot)
                Console.WriteLine("Snapshot directory : {0}", SnapshotDirectory);
            else
            {
                Console.WriteLine("Installer directory : {0}", InstallerDirectory);
                Console.WriteLine("Binaries directory : {0}", BinariesDirectory);
            }
            Console.WriteLine();

            return !String.IsNullOrEmpty(s_nsisExe);
        }

        /// <summary>
        /// Finds the 'makensis' executable.
        /// </summary>
        /// <returns></returns>
        private static string FindMakeNsisExe()
        {
            string path = Path.Combine(ProjectDirectory, @"NSIS\makensis.exe");
            return File.Exists(path) ? path : String.Empty;
        }

        /// <summary>
        /// Publishes EVEMon as a self-contained win-x64 application into <see cref="SourceFilesDirectory"/>.
        /// </summary>
        /// <returns>The exit code of 'dotnet publish'.</returns>
        private static int PublishEVEMon()
        {
            string project = Path.Combine(SolutionDirectory, @"src\EVEMon\EVEMon.csproj");
            string configuration = s_isSnapshot ? "Snapshot" : "Release";

            // Start from an empty folder so no stale files end up in the installer
            if (Directory.Exists(SourceFilesDirectory))
                Directory.Delete(SourceFilesDirectory, true);

            string param = String.Format(CultureInfo.InvariantCulture,
                "publish \"{0}\" -c {1} -r win-x64 --self-contained true -p:PublishSingleFile=false -o \"{2}\"",
                project, configuration, SourceFilesDirectory.TrimEnd('\\'));

            Console.WriteLine("Publishing EVEMon : dotnet {0}", param);

            ProcessStartInfo psi = new ProcessStartInfo("dotnet", param)
            {
                WorkingDirectory = SolutionDirectory,
                UseShellExecute = false
            };

            using (Process publishProcess = Process.Start(psi))
            {
                publishProcess.WaitForExit();

                if (publishProcess.ExitCode != 0)
                    Console.WriteLine("dotnet publish exited with errors.");

                Console.WriteLine();
                return publishProcess.ExitCode;
            }
        }

        /// <summary>
        /// Gets the signtool arguments (everything except the file list), e.g.
        /// <c>sign /fd SHA256 /tr http://timestamp.acs.microsoft.com /td SHA256 /dlib ... /dmdf ...</c>.
        /// Signing is skipped when the EVEMON_SIGN_ARGS environment variable is not set.
        /// </summary>
        private static string SignArguments => Environment.GetEnvironmentVariable("EVEMON_SIGN_ARGS");

        /// <summary>
        /// Gets the signtool executable; EVEMON_SIGNTOOL, otherwise 'signtool.exe' from the PATH.
        /// </summary>
        private static string SignToolExe
            => Environment.GetEnvironmentVariable("EVEMON_SIGNTOOL") is string path && !String.IsNullOrWhiteSpace(path)
                ? path
                : "signtool.exe";

        /// <summary>
        /// Signs every executable and library in <see cref="SourceFilesDirectory"/> that has no Authenticode
        /// signature yet. The .NET runtime files are already signed by Microsoft and are left untouched.
        /// </summary>
        /// <returns>0 on success or when signing is not configured.</returns>
        private static int SignPublishedFiles()
        {
            if (String.IsNullOrWhiteSpace(SignArguments))
            {
                Console.WriteLine("Code signing : Not configured (set EVEMON_SIGN_ARGS) - binaries will be unsigned.");
                Console.WriteLine();
                return 0;
            }

            string[] unsigned = Directory.GetFiles(SourceFilesDirectory, "*", SearchOption.AllDirectories)
                .Where(file => file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                               file.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                .Where(file => !HasAuthenticodeSignature(file))
                .ToArray();

            return SignFiles(unsigned);
        }

        /// <summary>
        /// Gets true if the file carries an embedded Authenticode signature (a non-empty PE certificate table).
        /// </summary>
        private static bool HasAuthenticodeSignature(string file)
        {
            using (FileStream stream = File.OpenRead(file))
            using (PEReader peReader = new PEReader(stream))
            {
                try
                {
                    return peReader.PEHeaders.PEHeader?.CertificateTableDirectory.Size > 0;
                }
                catch (BadImageFormatException)
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// Signs the specified files with signtool.
        /// </summary>
        /// <returns>0 on success or when signing is not configured; otherwise the signtool exit code.</returns>
        private static int SignFiles(string[] files)
        {
            if (String.IsNullOrWhiteSpace(SignArguments) || !files.Any())
                return 0;

            // Keep each command line well under the Windows limit
            const int BatchSize = 40;
            for (int i = 0; i < files.Length; i += BatchSize)
            {
                string[] batch = files.Skip(i).Take(BatchSize).ToArray();
                string param = String.Format(CultureInfo.InvariantCulture, "{0} {1}", SignArguments,
                    String.Join(" ", batch.Select(file => "\"" + file + "\"")));

                Console.WriteLine("Signing {0} file(s) : {1}", batch.Length,
                    String.Join(", ", batch.Select(Path.GetFileName)));

                ProcessStartInfo psi = new ProcessStartInfo(SignToolExe, param)
                {
                    WorkingDirectory = SolutionDirectory,
                    UseShellExecute = false
                };

                using (Process signProcess = Process.Start(psi))
                {
                    signProcess.WaitForExit();
                    if (signProcess.ExitCode != 0)
                    {
                        Console.WriteLine("signtool exited with errors.");
                        return signProcess.ExitCode;
                    }
                }
            }

            Console.WriteLine();
            return 0;
        }

        /// <summary>
        /// Gets true if a release version has been compiled.
        /// </summary>
        private static bool HasVersion()
        {
            try
            {
                s_fileVersionInfo = FileVersionInfo.GetVersionInfo(Path.Combine(SourceFilesDirectory, "EVEMon.exe"));
            }
            catch (Exception)
            {
                Console.WriteLine("A \"Release\" has to be published first.");
                Console.WriteLine("Install Builder will now close.");
                Console.ReadLine();
                return false;
            }
            return true;
        }

        /// <summary>
        /// Builds the zip.
        /// </summary>
        private static int BuildZip()
        {
            string directory = s_isSnapshot ? SnapshotDirectory : BinariesDirectory;

            // Delete any existing files in directory
            DeleteFiles(directory);

            string filename = s_isSnapshot
                ? String.Format(CultureInfo.InvariantCulture, "EVEMon_{0}_{1:yyyy-MM-dd}.zip",
                    s_fileVersionInfo.ProductPrivatePart, DateTime.Now)
                : String.Format(CultureInfo.InvariantCulture, "EVEMon-binaries-{0}.zip", s_fileVersionInfo.ProductVersion);

            string zipFileName = Path.Combine(directory, filename);

            string[] filenames = Directory.GetFiles(SourceFilesDirectory, "*", SearchOption.AllDirectories);

            Stream stream = null;
            try
            {
                stream = File.Create(zipFileName);

                using (ZipOutputStream zipStream = new ZipOutputStream(stream))
                {
                    stream = null;
                    zipStream.SetLevel(9);
                    zipStream.UseZip64 = UseZip64.Off;

                    byte[] buffer = new byte[4096];

                    foreach (string file in filenames.Where(file => !file.Contains("vshost") && !file.Contains(".config")))
                    {
                        string entryName = String.Format(CultureInfo.InvariantCulture, "EVEMon\\{0}",
                            file.Remove(0, SourceFilesDirectory.Length));
                        Console.WriteLine("Zipping {0}", entryName);
                        ZipEntry entry = new ZipEntry(entryName) { DateTime = DateTime.Now };

                        zipStream.PutNextEntry(entry);

                        using (FileStream fs = File.OpenRead(file))
                        {
                            int sourceBytes;
                            do
                            {
                                sourceBytes = fs.Read(buffer, 0, buffer.Length);
                                zipStream.Write(buffer, 0, sourceBytes);
                            } while (sourceBytes > 0);
                        }
                    }
                    zipStream.Finish();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                return 1;
            }
            finally
            {
                stream?.Dispose();
            }

            return 0;
        }

        /// <summary>
        /// Builds the installer.
        /// </summary>
        private static int BuildInstaller()
        {
            // Delete any existing files in directory
            DeleteFiles(InstallerDirectory);

            int exitCode;
            try
            {
                string nsisScript = Path.Combine(ProjectDirectory, OutputPath, "EVEMonInstallerScript.nsi");
                string resourcesDir = Path.Combine(SolutionDirectory, @"src\\EVEMon.Common\Resources");
                string appCopyright =
                    ((AssemblyCopyrightAttribute)
                        Attribute.GetCustomAttribute(typeof(Program).Assembly, typeof(AssemblyCopyrightAttribute)))
                        .Copyright;
                string productName = String.Format(CultureInfo.InvariantCulture, "/DPRODUCTNAME=\"{0}\"", Application.ProductName);
                string companyName = String.Format(CultureInfo.InvariantCulture, "/DCOMPANYNAME=\"{0}\"", Application.CompanyName);
                string copyright = String.Format(CultureInfo.InvariantCulture, "/DCOPYRIGHT=\"{0}\"", appCopyright);
                string description = String.Format(CultureInfo.InvariantCulture, "/DDESCRIPTION=\"{0}\"", Application.ProductName);
                string version = String.Format(CultureInfo.InvariantCulture, "/DVERSION={0}", s_fileVersionInfo.ProductVersion);
                string fullVersion = String.Format(CultureInfo.InvariantCulture, "/DFULLVERSION={0}",
                    s_fileVersionInfo.FileVersion);
                string installerDir = String.Format(CultureInfo.InvariantCulture, "/DOUTDIR={0}", InstallerDirectory);
                string sourceDir = String.Format(CultureInfo.InvariantCulture, "/DSOURCEDIR={0}", SourceFilesDirectory);
                string resourceDir = String.Format(CultureInfo.InvariantCulture, "/DRESOURCESDIR={0}", resourcesDir);

                string param = String.Format(CultureInfo.InvariantCulture, "{0} {1} {2} {3} {4} {5} {6} {7} {8} {9}",
                    productName, companyName, copyright, description, version, fullVersion, installerDir, sourceDir, resourceDir,
                    nsisScript);

                Console.WriteLine("NSIS script : {0}", nsisScript);
                Console.WriteLine("Output directory : {0}", InstallerDirectory);

                ProcessStartInfo psi = new ProcessStartInfo(s_nsisExe, param)
                {
                    WorkingDirectory = ProjectDirectory,
                    UseShellExecute = false,
                    RedirectStandardOutput = true
                };

                using (Process makensisProcess = new Process())
                {
                    makensisProcess.StartInfo = psi;
                    makensisProcess.Start();
                    makensisProcess.ProcessorAffinity = (IntPtr)0x3;
                    Console.WriteLine(makensisProcess.StandardOutput.ReadToEnd());
                    makensisProcess.WaitForExit();
                    exitCode = makensisProcess.ExitCode;
                }

                if (exitCode != 0)
                    Console.WriteLine("MakeNSIS exited with errors.");
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                return 1;
            }

            return exitCode;
        }

        /// <summary>
        /// Deletes the files in the specified directory.
        /// </summary>
        /// <param name="directoryPath">The directory path.</param>
        private static void DeleteFiles(string directoryPath)
        {
            Console.WriteLine("Deleting all files in {0}", directoryPath);

            foreach (string file in Directory.GetFiles(directoryPath))
            {
                try
                {
                    File.Delete(file);
                }
                catch (ArgumentException ex)
                {
                    MessageBox.Show(ex.Message);
                }
                catch (IOException ex)
                {
                    MessageBox.Show(ex.Message);
                }
                catch (UnauthorizedAccessException ex)
                {
                    MessageBox.Show(ex.Message);
                }
            }
        }
    }
}