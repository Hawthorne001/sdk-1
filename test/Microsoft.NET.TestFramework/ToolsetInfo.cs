﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Reflection;
using Microsoft.DotNet.Cli.Utils;

namespace Microsoft.NET.TestFramework
{
    public class ToolsetInfo
    {
        public const string CurrentTargetFramework = "net10.0";
        public const string CurrentTargetFrameworkVersion = "10.0";
        public const string CurrentTargetFrameworkMoniker = ".NETCoreApp,Version=v" + CurrentTargetFrameworkVersion;
        public const string NextTargetFramework = "net11.0";
        public const string NextTargetFrameworkVersion = "11.0";

        public const string LatestWinRuntimeIdentifier = "win";
        public const string LatestLinuxRuntimeIdentifier = "linux";
        public const string LatestMacRuntimeIdentifier = "osx";
        public const string LatestRuntimeIdentifiers = $"{LatestWinRuntimeIdentifier}-x64;{LatestWinRuntimeIdentifier}-x86;{LatestMacRuntimeIdentifier}-x64;{LatestLinuxRuntimeIdentifier}-x64;linux-musl-x64";

        public string DotNetRoot { get; }
        public string DotNetHostPath { get; }

        private string? _sdkVersion;
        public string SdkVersion
        {
            get
            {
                if (_sdkVersion == null)
                {
                    //  Initialize SdkVersion lazily, as we call `dotnet --version` to get it, so we need to wait
                    //  for the TestContext to finish being initialize
                    InitSdkVersion();
                }
                return _sdkVersion ?? throw new InvalidOperationException("SdkVersion should never be null."); ;
            }
        }

        private string? _msbuildVersion;
        public string? MSBuildVersion
        {
            get
            {
                if (_msbuildVersion == null)
                {
                    //  Initialize MSBuildVersion lazily, as we call `dotnet msbuild -version` to get it, so we need to wait
                    //  for the TestContext to finish being initialize
                    InitMSBuildVersion();
                }
                return _msbuildVersion;
            }
        }

        Lazy<string> _sdkFolderUnderTest;

        public string SdkFolderUnderTest => _sdkFolderUnderTest.Value;

        Lazy<string> _sdksPath;
        public string SdksPath => _sdksPath.Value;

        public string? CliHomePath { get; set; }

        public string? MicrosoftNETBuildExtensionsPathOverride { get; set; }

        public bool ShouldUseFullFrameworkMSBuild => !string.IsNullOrEmpty(FullFrameworkMSBuildPath);

        public string? FullFrameworkMSBuildPath { get; set; }

        public string? SdkResolverPath { get; set; }

        public string? RepoRoot { get; set; }

        public ToolsetInfo(string dotNetRoot)
        {
            DotNetRoot = dotNetRoot;

            DotNetHostPath = Path.Combine(dotNetRoot, $"dotnet{Constants.ExeSuffix}");

            _sdkFolderUnderTest = new Lazy<string>(() => Path.Combine(DotNetRoot, "sdk", SdkVersion));
            _sdksPath = new Lazy<string>(() => Path.Combine(SdkFolderUnderTest, "Sdks"));
        }

        private void InitSdkVersion()
        {
            //  If using full framework MSBuild, then running a command tries to get the SdkVersion in order to set the
            //  DOTNET_MSBUILD_SDK_RESOLVER_SDKS_DIR environment variable.  So turn that off when getting the SDK version
            //  in order to avoid stack overflow
            string? oldFullFrameworkMSBuildPath = FullFrameworkMSBuildPath;
            try
            {
                FullFrameworkMSBuildPath = null;
                var logger = new StringTestLogger();
                var command = new DotnetCommand(logger, "--version")
                {
                    WorkingDirectory = TestContext.Current.TestExecutionDirectory
                };

                var result = command.Execute();

                if (result.ExitCode != 0)
                {
                    throw new Exception("Failed to get dotnet version" + Environment.NewLine + logger.ToString());
                }

                _sdkVersion = result.StdOut?.Trim();
            }
            finally
            {
                FullFrameworkMSBuildPath = oldFullFrameworkMSBuildPath;
            }
        }

        private void InitMSBuildVersion()
        {
            var logger = new StringTestLogger();
            var command = new MSBuildVersionCommand(logger)
            {
                WorkingDirectory = TestContext.Current.TestExecutionDirectory
            };

            var result = command.Execute();

            if (result.ExitCode != 0)
            {
                throw new Exception("Failed to get msbuild version" + Environment.NewLine + logger.ToString());
            }

            _msbuildVersion = result.StdOut?.Split().Last();
        }

        public string? GetMicrosoftNETBuildExtensionsPath()
        {
            if (!string.IsNullOrEmpty(MicrosoftNETBuildExtensionsPathOverride))
            {
                return MicrosoftNETBuildExtensionsPathOverride;
            }
            else
            {
                if (ShouldUseFullFrameworkMSBuild)
                {
                    string? msbuildRoot = null;
                    var msbuildBinPath = Path.GetDirectoryName(FullFrameworkMSBuildPath);
                    if (msbuildBinPath is not null)
                    {
                        msbuildRoot = Directory.GetParent(msbuildBinPath)?.Parent?.FullName;
                    }
                    return Path.Combine(msbuildRoot ?? string.Empty, @"Microsoft\Microsoft.NET.Build.Extensions");
                }
                else
                {
                    return Path.Combine(DotNetRoot, "sdk", SdkVersion, @"Microsoft\Microsoft.NET.Build.Extensions");
                }
            }
        }

        public void AddTestEnvironmentVariables(IDictionary<string, string> environment)
        {
            if (ShouldUseFullFrameworkMSBuild)
            {
                string sdksPath = Path.Combine(DotNetRoot, "sdk", SdkVersion, "Sdks");

                //  Use stage 2 MSBuild SDK resolver
                if (SdkResolverPath is not null)
                {
                    environment["MSBUILDADDITIONALSDKRESOLVERSFOLDER"] = SdkResolverPath;
                }

                //  Avoid using stage 0 dotnet install dir
                environment["DOTNET_MSBUILD_SDK_RESOLVER_CLI_DIR"] = "";

                //  Put stage 2 on the Path (this is how the MSBuild SDK resolver finds dotnet)
                environment["Path"] = DotNetRoot + ";" + Environment.GetEnvironmentVariable("Path");

                if (!string.IsNullOrEmpty(MicrosoftNETBuildExtensionsPathOverride))
                {
                    var microsoftNETBuildExtensionsPath = GetMicrosoftNETBuildExtensionsPath();
                    if (microsoftNETBuildExtensionsPath is not null)
                    {
                        environment["MicrosoftNETBuildExtensionsTargets"] = Path.Combine(microsoftNETBuildExtensionsPath, "Microsoft.NET.Build.Extensions.targets");
                    }

                    if (UsingFullMSBuildWithoutExtensionsTargets())
                    {
                        environment["CustomAfterMicrosoftCommonTargets"] = Path.Combine(sdksPath, "Microsoft.NET.Build.Extensions",
                            "msbuildExtensions-ver", "Microsoft.Common.targets", "ImportAfter", "Microsoft.NET.Build.Extensions.targets");
                    }
                }

            }

            if (Environment.Is64BitProcess)
            {
                environment.Add("DOTNET_ROOT", DotNetRoot);
            }
            else
            {
                environment.Add("DOTNET_ROOT(x86)", DotNetRoot);
            }

            if (!string.IsNullOrEmpty(CliHomePath))
            {
                environment.Add("DOTNET_CLI_HOME", CliHomePath);
            }

            //  We set this environment variable for in-process tests, but we don't want it to flow to out of process tests
            //  (especially if we're trying to run on full Framework MSBuild)
            environment[Constants.MSBUILD_EXE_PATH] = "";

        }

        public SdkCommandSpec CreateCommandForTarget(string target, IEnumerable<string> args)
        {
            var newArgs = args.ToList();
            if (!string.IsNullOrEmpty(target))
            {
                newArgs.Insert(0, $"/t:{target}");
            }

            return CreateCommand(newArgs.ToArray());
        }

        private SdkCommandSpec CreateCommand(params string[] args)
        {
            SdkCommandSpec ret = new();

            //  Run tests on full framework MSBuild if environment variable is set pointing to it
            if (ShouldUseFullFrameworkMSBuild)
            {
                ret.FileName = FullFrameworkMSBuildPath;
                ret.Arguments = args.ToList();
                // Don't propagate DOTNET_HOST_PATH to the msbuild process, to match behavior
                // when running desktop msbuild outside of the test harness.
                ret.Environment["DOTNET_HOST_PATH"] = string.Empty;
            }
            else
            {
                var newArgs = args.ToList();
                newArgs.Insert(0, $"msbuild");

                ret.FileName = DotNetHostPath;
                ret.Arguments = newArgs;
            }

            TestContext.Current.AddTestEnvironmentVariables(ret.Environment);

            return ret;
        }

        private static string GetDotnetHostPath(string? dotnetRoot)
            => Path.Combine(dotnetRoot ?? string.Empty, "dotnet" + Constants.ExeSuffix);

        public static ToolsetInfo Create(string? repoRoot, string? repoArtifactsDir, string configuration, TestCommandLine commandLine)
        {
            repoRoot = commandLine.SDKRepoPath ?? repoRoot;
            configuration = commandLine.SDKRepoConfiguration ?? configuration;

            string? dotnetInstallDirFromEnvironment = Environment.GetEnvironmentVariable("DOTNET_INSTALL_DIR");

            string? dotnetRoot;
            string hostNotFoundReason;

            if (!string.IsNullOrEmpty(commandLine.DotnetHostPath))
            {
                dotnetRoot = Path.GetDirectoryName(commandLine.DotnetHostPath);
                hostNotFoundReason = "Command line argument -dotnetPath is incorrect.";
            }
            else if (repoRoot != null && repoArtifactsDir is not null)
            {
                dotnetRoot = Path.Combine(repoArtifactsDir, "bin", "redist", configuration, "dotnet");
                hostNotFoundReason = "Is 'redist.csproj' built?";
            }
            else if (!string.IsNullOrEmpty(dotnetInstallDirFromEnvironment))
            {
                dotnetRoot = dotnetInstallDirFromEnvironment;
                hostNotFoundReason = "The value of DOTNET_INSTALL_DIR is incorrect.";
            }
            else
            {
                if (TryResolveCommand("dotnet", out string? pathToDotnet))
                {
                    dotnetRoot = Path.GetDirectoryName(pathToDotnet);
                }
                else
                {
                    throw new InvalidOperationException("Could not resolve path to dotnet");
                }
                hostNotFoundReason = "";
            }

            var dotnetHost = GetDotnetHostPath(dotnetRoot);
            if (dotnetRoot is null || !File.Exists(dotnetHost))
            {
                throw new FileNotFoundException($"Host '{dotnetHost}' not found. {hostNotFoundReason}");
            }

            var ret = new ToolsetInfo(dotnetRoot)
            {
                RepoRoot = repoRoot,
            };

            if (!string.IsNullOrEmpty(commandLine.FullFrameworkMSBuildPath))
            {
                ret.FullFrameworkMSBuildPath = commandLine.FullFrameworkMSBuildPath;
            }
            else if (commandLine.UseFullFrameworkMSBuild)
            {
                if (TryResolveCommand("MSBuild", out string? pathToMSBuild))
                {
                    ret.FullFrameworkMSBuildPath = pathToMSBuild;
                }
                else
                {
                    throw new InvalidOperationException("Could not resolve path to MSBuild");
                }
            }

            var microsoftNETBuildExtensionsTargetsFromEnvironment = Environment.GetEnvironmentVariable("MicrosoftNETBuildExtensionsTargets");
            if (!string.IsNullOrWhiteSpace(microsoftNETBuildExtensionsTargetsFromEnvironment))
            {
                ret.MicrosoftNETBuildExtensionsPathOverride = Path.GetDirectoryName(microsoftNETBuildExtensionsTargetsFromEnvironment);
            }
            else if (repoRoot != null && ret.ShouldUseFullFrameworkMSBuild && repoArtifactsDir is not null)
            {
                //  Find path to Microsoft.NET.Build.Extensions for full framework
                string sdksPath = Path.Combine(repoArtifactsDir, "bin", configuration, "Sdks");
                var buildExtensionsSdkPath = Path.Combine(sdksPath, "Microsoft.NET.Build.Extensions");
                ret.MicrosoftNETBuildExtensionsPathOverride = Path.Combine(buildExtensionsSdkPath, "msbuildExtensions", "Microsoft", "Microsoft.NET.Build.Extensions");
            }

            if (ret.ShouldUseFullFrameworkMSBuild)
            {
                if (repoRoot != null && repoArtifactsDir is not null)
                {
                    // Find path to MSBuildSdkResolver for full framework
                    ret.SdkResolverPath = Path.Combine(repoArtifactsDir, "bin", "Microsoft.DotNet.MSBuildSdkResolver", configuration, "net472", "SdkResolvers");
                }
                else if (!string.IsNullOrWhiteSpace(commandLine.MsbuildAdditionalSdkResolverFolder))
                {
                    ret.SdkResolverPath = Path.Combine(commandLine.MsbuildAdditionalSdkResolverFolder, configuration, "net472", "SdkResolvers");
                }
                else if (Environment.GetEnvironmentVariable("DOTNET_SDK_TEST_MSBUILDSDKRESOLVER_FOLDER") != null)
                {
                    ret.SdkResolverPath = Path.Combine(Environment.GetEnvironmentVariable("DOTNET_SDK_TEST_MSBUILDSDKRESOLVER_FOLDER")!, configuration, "net472", "SdkResolvers");
                }
                else
                {
                    throw new InvalidOperationException("Microsoft.DotNet.MSBuildSdkResolver path is not provided, set msbuildAdditionalSdkResolverFolder on test commandline or set repoRoot");
                }
            }

            if (repoRoot != null && repoArtifactsDir is not null)
            {
                ret.CliHomePath = Path.Combine(repoArtifactsDir, "tmp", configuration, "testing");
            }

            return ret;
        }

        /// <summary>
        /// Attempts to resolve full path to command from PATH/PATHEXT environment variable.
        /// </summary>
        /// <param name="command">The command to resolve.</param>
        /// <param name="fullExePath">The full path to the command</param>
        /// <returns><see langword="true"/> when command can be resolved, <see langword="false"/> otherwise.</returns>
        public static bool TryResolveCommand(string command, out string? fullExePath)
        {
            fullExePath = null;
            char pathSplitChar;
            string[] extensions = new string[] { string.Empty };
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                pathSplitChar = ';';
                extensions = extensions
                    .Concat(Environment.GetEnvironmentVariable("PATHEXT")?.Split(pathSplitChar) ?? Array.Empty<string>())
                    .ToArray();
            }
            else
            {
                pathSplitChar = ':';
            }

            var paths = Environment.GetEnvironmentVariable("PATH")?.Split(pathSplitChar);
            string? result = extensions.SelectMany(ext => paths?.Select(p => Path.Combine(p, command + ext)) ?? Array.Empty<string>())
                .FirstOrDefault(File.Exists);

            if (result == null)
            {
                return false;
            }

            fullExePath = result;
            return true;
        }

        private bool UsingFullMSBuildWithoutExtensionsTargets()
        {
            if (!ShouldUseFullFrameworkMSBuild)
            {
                return false;
            }
            string? fullMSBuildDirectory = Path.GetDirectoryName(FullFrameworkMSBuildPath);
            string extensionsImportAfterPath = Path.Combine(fullMSBuildDirectory ?? string.Empty, "..", "Microsoft.Common.targets", "ImportAfter", "Microsoft.NET.Build.Extensions.targets");
            return !File.Exists(extensionsImportAfterPath);
        }

        internal static IEnumerable<(string versionPropertyName, string version)> GetPackageVersionProperties()
            => typeof(ToolsetInfo).Assembly
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .Where(a => a.Key is not null && a.Key.EndsWith("PackageVersion"))
                .Select(a => (a.Key ?? string.Empty, a.Value ?? string.Empty));

        public static string GetPackageVersion(string packageName)
        {
            var propertyName = packageName.Replace(".", "") + "PackageVersion";
            return GetPackageVersionProperties().Single(p => p.versionPropertyName == propertyName).version;
        }

        private static readonly Lazy<string> s_newtonsoftJsonPackageVersion = new(() => GetPackageVersion("Newtonsoft.Json"));
        private static readonly Lazy<string> s_systemDataSqlClientPackageVersion = new(() => GetPackageVersion("System.Data.SqlClient"));

        public static string GetNewtonsoftJsonPackageVersion()
            =>s_newtonsoftJsonPackageVersion.Value;

        public static string GetSystemDataSqlClientPackageVersion()
            => s_systemDataSqlClientPackageVersion.Value;
    }
}
