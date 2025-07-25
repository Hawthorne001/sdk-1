// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

#nullable disable

using System.CommandLine;
using System.Text.Json;
using ManifestReaderTests;
using Microsoft.DotNet.Cli.Commands;
using Microsoft.DotNet.Cli.Commands.Workload.Install;
using Microsoft.DotNet.Cli.Commands.Workload.Repair;
using Microsoft.DotNet.Cli.NuGetPackageDownloader;
using Microsoft.DotNet.Cli.Workload.Install.Tests;
using Microsoft.NET.Sdk.WorkloadManifestReader;

namespace Microsoft.DotNet.Cli.Workload.Repair.Tests
{
    public class GivenDotnetWorkloadRepair : SdkTest
    {
        private readonly ParseResult _parseResult;
        private readonly BufferedReporter _reporter;
        private readonly string _manifestPath;

        public GivenDotnetWorkloadRepair(ITestOutputHelper log) : base(log)
        {
            _reporter = new BufferedReporter();
            _parseResult = Parser.Parse("dotnet workload repair");
            _manifestPath = Path.Combine(_testAssetsManager.GetAndValidateTestProjectDirectory("SampleManifest"), "Sample.json");
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void GivenNoWorkloadsAreInstalledRepairIsNoOp(bool userLocal)
        {
            _reporter.Clear();
            var testDirectory = _testAssetsManager.CreateTestDirectory(identifier: userLocal ? "userlocal" : "default").Path;
            var dotnetRoot = Path.Combine(testDirectory, "dotnet");
            var userProfileDir = Path.Combine(testDirectory, "user-profile");
            var nugetDownloader = new MockNuGetPackageDownloader(dotnetRoot);
            var workloadResolver = WorkloadResolver.CreateForTests(new MockManifestProvider(new[] { _manifestPath }), dotnetRoot, userLocal, userProfileDir);
            var sdkFeatureVersion = "6.0.100";

            if (userLocal)
            {
                WorkloadFileBasedInstall.SetUserLocal(dotnetRoot, sdkFeatureVersion);
            }

            var workloadResolverFactory = new MockWorkloadResolverFactory(dotnetRoot, sdkFeatureVersion, workloadResolver, userProfileDir);

            var repairCommand = new WorkloadRepairCommand(_parseResult, reporter: _reporter, workloadResolverFactory,
                nugetPackageDownloader: nugetDownloader);
            repairCommand.Execute();

            _reporter.Lines.Should().Contain(CliCommandStrings.NoWorkloadsToRepair);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void GivenExtraPacksInstalledRepairGarbageCollects(bool userLocal)
        {
            var testDirectory = _testAssetsManager.CreateTestDirectory(identifier: userLocal ? "userlocal" : "default").Path;
            var dotnetRoot = Path.Combine(testDirectory, "dotnet");
            var userProfileDir = Path.Combine(testDirectory, "user-profile");
            var workloadResolver = WorkloadResolver.CreateForTests(new MockManifestProvider(new[] { _manifestPath }), dotnetRoot, userLocal, userProfileDir);
            var nugetDownloader = new MockNuGetPackageDownloader(dotnetRoot);
            var manifestUpdater = new MockWorkloadManifestUpdater();
            var sdkFeatureVersion = "6.0.100";
            var installingWorkload = "xamarin-android";

            string installRoot = userLocal ? userProfileDir : dotnetRoot;
            if (userLocal)
            {
                WorkloadFileBasedInstall.SetUserLocal(dotnetRoot, sdkFeatureVersion);
            }

            var workloadResolverFactory = new MockWorkloadResolverFactory(dotnetRoot, sdkFeatureVersion, workloadResolver, userProfileDir);

            // Install a workload
            var installParseResult = Parser.Parse(new string[] { "dotnet", "workload", "install", installingWorkload });
            var installCommand = new WorkloadInstallCommand(installParseResult, reporter: _reporter, workloadResolverFactory, nugetPackageDownloader: nugetDownloader,
                workloadManifestUpdater: manifestUpdater, tempDirPath: testDirectory);
            installCommand.Execute();

            // Add extra pack dirs and records
            var extraPackRecordPath = Path.Combine(installRoot, "metadata", "workloads", "InstalledPacks", "v1", "Test.Pack.A", "1.0.0", sdkFeatureVersion);
            Directory.CreateDirectory(Path.GetDirectoryName(extraPackRecordPath));
            var extraPackPath = Path.Combine(installRoot, "packs", "Test.Pack.A", "1.0.0");
            Directory.CreateDirectory(extraPackPath);
            var packRecordContents = JsonSerializer.Serialize<WorkloadResolver.PackInfo>(new(new WorkloadPackId("Test.Pack.A"), "1.0.0", WorkloadPackKind.Sdk, extraPackPath, "Test.Pack.A"));
            File.WriteAllText(extraPackRecordPath, packRecordContents);

            var repairCommand = new WorkloadRepairCommand(_parseResult, reporter: _reporter, workloadResolverFactory,
                nugetPackageDownloader: nugetDownloader);
            repairCommand.Execute();

            // Check that pack dirs and records have been removed
            File.Exists(extraPackRecordPath).Should().BeFalse();
            Directory.Exists(Path.GetDirectoryName(Path.GetDirectoryName(extraPackRecordPath))).Should().BeFalse();
            Directory.Exists(extraPackPath).Should().BeFalse();

            // Expected packs are still present
            Directory.GetDirectories(Path.Combine(installRoot, "packs")).Length.Should().Be(7);
            Directory.GetDirectories(Path.Combine(installRoot, "metadata", "workloads", "InstalledPacks", "v1")).Length.Should().Be(8);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void GivenMissingPacksRepairFixesInstall(bool userLocal)
        {
            var testDirectory = _testAssetsManager.CreateTestDirectory(identifier: userLocal ? "userlocal" : "default").Path;
            var dotnetRoot = Path.Combine(testDirectory, "dotnet");
            var userProfileDir = Path.Combine(testDirectory, "user-profile");
            var workloadResolver = WorkloadResolver.CreateForTests(new MockManifestProvider(new[] { _manifestPath }), dotnetRoot, userLocal, userProfileDir);
            var nugetDownloader = new MockNuGetPackageDownloader(dotnetRoot);
            var manifestUpdater = new MockWorkloadManifestUpdater();
            var sdkFeatureVersion = "6.0.100";
            var installingWorkload = "xamarin-android";

            string installRoot = userLocal ? userProfileDir : dotnetRoot;
            if (userLocal)
            {
                WorkloadFileBasedInstall.SetUserLocal(dotnetRoot, sdkFeatureVersion);
            }

            var workloadResolverFactory = new MockWorkloadResolverFactory(dotnetRoot, sdkFeatureVersion, workloadResolver, userProfileDir);

            // Install a workload
            var installParseResult = Parser.Parse(new string[] { "dotnet", "workload", "install", installingWorkload });
            var installCommand = new WorkloadInstallCommand(installParseResult, reporter: _reporter, workloadResolverFactory, nugetPackageDownloader: nugetDownloader,
                workloadManifestUpdater: manifestUpdater, tempDirPath: testDirectory);
            installCommand.Execute();

            // Delete pack dirs/ records
            var deletedPackRecordPath = Path.Combine(installRoot, "metadata", "workloads", "InstalledPacks", "v1", "Xamarin.Android.Sdk", "8.4.7", sdkFeatureVersion);
            File.Delete(deletedPackRecordPath);
            var deletedPackPath = Path.Combine(installRoot, "packs", "Xamarin.Android.Sdk");
            Directory.Delete(deletedPackPath, true);

            var repairCommand = new WorkloadRepairCommand(_parseResult, reporter: _reporter, workloadResolverFactory,
                nugetPackageDownloader: nugetDownloader);
            repairCommand.Execute();

            // Check that pack dirs and records have been replaced
            File.Exists(deletedPackRecordPath).Should().BeTrue();
            Directory.Exists(deletedPackPath).Should().BeTrue();

            // All expected packs are still present
            Directory.GetDirectories(Path.Combine(installRoot, "packs")).Length.Should().Be(7);
            Directory.GetDirectories(Path.Combine(installRoot, "metadata", "workloads", "InstalledPacks", "v1")).Length.Should().Be(8);
        }
    }
}
