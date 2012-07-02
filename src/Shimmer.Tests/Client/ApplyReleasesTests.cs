using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Reflection;
using Moq;
using NuGet;
using ReactiveUI;
using Shimmer.Client;
using Shimmer.Core;
using Shimmer.Tests.TestHelpers;
using Xunit;

namespace Shimmer.Tests.Client
{
    public class ApplyReleasesTests : IEnableLogger
    {
        [Serializable]
        public class FakeUrlDownloader : IUrlDownloader
        {
            public IObservable<string> DownloadUrl(string url)
            {
                return Observable.Empty<string>();
            }

            public IObservable<Unit> QueueBackgroundDownloads(IEnumerable<string> urls, IEnumerable<string> localPaths)
            {
                return Observable.Empty<Unit>();
            }
        }

        [Fact]
        public void ApplyReleasesWithOneReleaseFile()
        {
            string tempDir;

            using (Utility.WithTempDirectory(out tempDir)) {
                Directory.CreateDirectory(Path.Combine(tempDir, "theApp"));
                Directory.CreateDirectory(Path.Combine(tempDir, "theApp", "packages"));

                new[] {
                    "Shimmer.Core.1.0.0.0-full.nupkg",
                    "Shimmer.Core.1.1.0.0-full.nupkg",
                }.ForEach(x => File.Copy(IntegrationTestHelper.GetPath("fixtures", x), Path.Combine(tempDir, "theApp", "packages", x)));

                var fixture = new UpdateManager("http://lol", "theApp", FrameworkVersion.Net40, tempDir, null, new FakeUrlDownloader());

                var baseEntry = ReleaseEntry.GenerateFromFile(Path.Combine(tempDir, "theApp", "packages", "Shimmer.Core.1.0.0.0-full.nupkg"));
                var latestFullEntry = ReleaseEntry.GenerateFromFile(Path.Combine(tempDir, "theApp", "packages", "Shimmer.Core.1.1.0.0-full.nupkg"));

                var updateInfo = UpdateInfo.Create(baseEntry, new[] { latestFullEntry }, "dontcare", FrameworkVersion.Net40);
                updateInfo.ReleasesToApply.Contains(latestFullEntry).ShouldBeTrue();

                using (fixture.AcquireUpdateLock()) {
                    fixture.ApplyReleases(updateInfo).First();
                }

                var filesToFind = new[] {
                    new {Name = "NLog.dll", Version = new Version("2.0.0.0")},
                    new {Name = "NSync.Core.dll", Version = new Version("1.1.0.0")},
                    new {Name = "Ionic.Zip.dll", Version = new Version("1.9.1.8")},
                };

                filesToFind.ForEach(x => {
                    var path = Path.Combine(tempDir, "theApp", "app-1.1.0.0", x.Name);
                    this.Log().Info("Looking for {0}", path);
                    File.Exists(path).ShouldBeTrue();

                    var vi = FileVersionInfo.GetVersionInfo(path);
                    var verInfo = new Version(vi.FileVersion ?? "1.0.0.0");
                    x.Version.ShouldEqual(verInfo);
                });
            }
        }

        [Fact]
        public void ApplyReleasesWithDeltaReleases()
        {
            string tempDir;

            using (Utility.WithTempDirectory(out tempDir)) {
                Directory.CreateDirectory(Path.Combine(tempDir, "theApp", "packages"));

                new[] {
                    "Shimmer.Core.1.0.0.0-full.nupkg",
                    "Shimmer.Core.1.1.0.0-delta.nupkg",
                    "Shimmer.Core.1.1.0.0-full.nupkg",
                }.ForEach(x => File.Copy(IntegrationTestHelper.GetPath("fixtures", x), Path.Combine(tempDir, "theApp", "packages", x)));

                var fixture = new UpdateManager("http://lol", "theApp", FrameworkVersion.Net40, tempDir, null, new FakeUrlDownloader());

                var baseEntry = ReleaseEntry.GenerateFromFile(Path.Combine(tempDir, "theApp", "packages", "Shimmer.Core.1.0.0.0-full.nupkg"));
                var deltaEntry = ReleaseEntry.GenerateFromFile(Path.Combine(tempDir, "theApp", "packages", "Shimmer.Core.1.1.0.0-delta.nupkg"));
                var latestFullEntry = ReleaseEntry.GenerateFromFile(Path.Combine(tempDir, "theApp", "packages", "Shimmer.Core.1.1.0.0-full.nupkg"));

                var updateInfo = UpdateInfo.Create(baseEntry, new[] { deltaEntry, latestFullEntry }, "dontcare", FrameworkVersion.Net40);
                updateInfo.ReleasesToApply.Contains(deltaEntry).ShouldBeTrue();

                using (fixture.AcquireUpdateLock()) {
                    fixture.ApplyReleases(updateInfo).First();
                }

                var filesToFind = new[] {
                    new {Name = "NLog.dll", Version = new Version("2.0.0.0")},
                    new {Name = "NSync.Core.dll", Version = new Version("1.1.0.0")},
                    new {Name = "Ionic.Zip.dll", Version = new Version("1.9.1.8")},
                };

                filesToFind.ForEach(x => {
                    var path = Path.Combine(tempDir, "theApp", "app-1.1.0.0", x.Name);
                    this.Log().Info("Looking for {0}", path);
                    File.Exists(path).ShouldBeTrue();

                    var vi = FileVersionInfo.GetVersionInfo(path);
                    var verInfo = new Version(vi.FileVersion ?? "1.0.0.0");
                    x.Version.ShouldEqual(verInfo);
                });
            }
        }

        [Fact]
        public void CreateFullPackagesFromDeltaSmokeTest()
        {
            string tempDir;
            using (Utility.WithTempDirectory(out tempDir)) {
                Directory.CreateDirectory(Path.Combine(tempDir, "theApp", "packages"));

                new[] {
                    "Shimmer.Core.1.0.0.0-full.nupkg",
                    "Shimmer.Core.1.1.0.0-delta.nupkg"
                }.ForEach(x => File.Copy(IntegrationTestHelper.GetPath("fixtures", x), Path.Combine(tempDir, "theApp", "packages", x)));

                var urlDownloader = new Mock<IUrlDownloader>();
                var fixture = new UpdateManager("http://lol", "theApp", FrameworkVersion.Net40, tempDir, null, urlDownloader.Object);

                var baseEntry = ReleaseEntry.GenerateFromFile(Path.Combine(tempDir, "theApp", "packages", "Shimmer.Core.1.0.0.0-full.nupkg"));
                var deltaEntry = ReleaseEntry.GenerateFromFile(Path.Combine(tempDir, "theApp", "packages", "Shimmer.Core.1.1.0.0-delta.nupkg"));

                var resultObs = (IObservable<ReleaseEntry>)fixture.GetType().GetMethod("createFullPackagesFromDeltas", BindingFlags.NonPublic | BindingFlags.Instance)
                    .Invoke(fixture, new object[] { new[] {deltaEntry}, baseEntry });

                var result = resultObs.First();
                var zp = new ZipPackage(Path.Combine(tempDir, "theApp", "packages", result.Filename));

                zp.Version.ToString().ShouldEqual("1.1.0.0");
            }
        }

        [Fact]
        public void ShouldCallAppUninstallOnTheOldVersion()
        {
            throw new NotImplementedException();
        }

        [Fact]
        public void CallAppInstallOnTheJustInstalledVersion()
        {
            throw new NotImplementedException();
        }

        [Fact]
        public void ShouldCreateAppShortcutsBasedOnClientExe()
        {
            throw new NotImplementedException();
        }

        [Fact]
        public void DeletedShortcutsShouldntBeRecreatedOnUpgrade()
        {
            throw new NotImplementedException();
        }

        [Fact]
        public void IfAppSetupThrowsWeFailTheInstall()
        {
            string tempDir;
            using (Utility.WithTempDirectory(out tempDir)) {
                var di = new DirectoryInfo(Path.Combine(tempDir, "theApp", "app-1.1.0.0"));
                di.CreateRecursive();

                File.Copy(getPathToShimmerTestTarget(), Path.Combine(di.FullName, "ShimmerIAppUpdateTestTarget.exe"));
                setShouldThrow();

                var fixture = new UpdateManager("http://lol", "theApp", FrameworkVersion.Net40, tempDir, null, null);

                bool shouldDie = true;
                try {
                    this.Log().Info("Invoking post-install");

                    var mi = fixture.GetType().GetMethod("runPostInstallOnDirectory", BindingFlags.NonPublic | BindingFlags.Instance);
                    mi.Invoke(fixture, new object[] { di.FullName, true, new Version(1, 1, 0, 0), Enumerable.Empty<ShortcutCreationRequest>() });
                } catch (TargetInvocationException ex) {
                    this.Log().Info("Expected to receive Exception", ex);

                    // NB: This is the exception explicitly rigged in OnAppInstall
                    if (ex.InnerException is FileNotFoundException) {
                        shouldDie = false;
                    } else {
                        this.Log().ErrorException("Expected FileNotFoundException, didn't get it", ex);
                    }
                }

                shouldDie.ShouldBeFalse();
            }
        }

        string getPathToShimmerTestTarget()
        {
#if DEBUG
            const string config = "Debug";
#else
            const string config = "Release";
#endif

            var ret = IntegrationTestHelper.GetPath("..", "ShimmerIAppUpdateTestTarget", "bin", config, "ShimmerIAppUpdateTestTarget.exe");
            File.Exists(ret).ShouldBeTrue();

            return ret;
        }

        void setShouldThrow()
        {
            setEnvVar("ShouldThrow", true);
        }

        static string getEnvVar(string name)
        {
            return Environment.GetEnvironmentVariable(String.Format("__IAPPSETUP_TEST_{0}", name.ToUpperInvariant()));
        }

        static void setEnvVar(string name, object val)
        {
            Environment.SetEnvironmentVariable(String.Format("__IAPPSETUP_TEST_{0}", name.ToUpperInvariant()), val.ToString(), EnvironmentVariableTarget.Process);
        }
    }
}