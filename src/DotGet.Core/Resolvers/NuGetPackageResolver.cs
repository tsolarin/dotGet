using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

using DotGet.Core.Configuration;
using DotGet.Core.Exceptions;
using DotGet.Core.Logging;

using NuGet.Commands;
using NuGet.Configuration;
using NuGet.Frameworks;
using NuGet.LibraryModel;
using NuGet.ProjectModel;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

namespace DotGet.Core.Resolvers
{
    internal class NuGetPackageResolver : Resolver
    {
        private SourceRepository _sourceRepository;
        private string _nuGetPackagesRoot;
        private NuGetLogger _nugetLogger;
        private static readonly string _nuGetFeed = "https://api.nuget.org/v3/index.json";

        public NuGetPackageResolver()
        {
            List<Lazy<INuGetResourceProvider>> providers = new List<Lazy<INuGetResourceProvider>>();
            providers.AddRange(Repository.Provider.GetCoreV3());

            _sourceRepository = new SourceRepository(new PackageSource(_nuGetFeed), providers);
            _nuGetPackagesRoot = Path.Combine(Globals.GlobalNuGetDirectory, "packages");
        }

        public override bool CanResolve(string source)
            => !source.Contains("/") && !source.Contains(@"\") && !source.StartsWith(".");

        public override bool DidResolve(string path) => path.Contains(_nuGetPackagesRoot);

        public override string GetSource(string path)
        {
            string[] parts = SplitPath(path);
            return $"{parts[0]}";
        }

        public override string GetFullSource(string path)
        {
            string[] parts = SplitPath(path);
            return $"{parts[0]}@{parts[1]}";
        }

        public override string Resolve(string source, ResolutionType resolutionType, ILogger logger)
        {
            _nugetLogger = new NuGetLogger(logger);
            var options = BuildOptions(source);
            bool hasVersion = options.TryGetValue("version", out string version);
            string package = options["package"];

            IPackageSearchMetadata packageSearchMetadata = GetPackageFromFeed(package, hasVersion && resolutionType == ResolutionType.Install ? version : "");
            if (packageSearchMetadata == null)
            {
                string error = $"Could not find package {package}";
                if (hasVersion)
                    error += $" with version {version}";

                throw new ResolverException(error);
            }

            if (!HasNetCoreAppDependencyGroup(packageSearchMetadata))
                throw new ResolverException($"{package} does not support .NETCoreApp framework!");

            if (!RestoreNuGetPackage(packageSearchMetadata.Identity.Id, packageSearchMetadata.Identity.Version.ToFullString()))
                throw new ResolverException("Package install failed!");

            string netcoreappDirectory = packageSearchMetadata.DependencySets.Select(d => d.TargetFramework).LastOrDefault(t => t.Framework == ".NETCoreApp").GetShortFolderName();
            string dllDirectory = Path.Combine(_nuGetPackagesRoot, BuildPackageDirectoryPath(packageSearchMetadata.Identity.Id, packageSearchMetadata.Identity.Version.ToFullString()), "lib", netcoreappDirectory);

            DirectoryInfo directoryInfo = new DirectoryInfo(dllDirectory);
            FileInfo assembly = directoryInfo.GetFiles().FirstOrDefault(f => f.Extension == ".dll");
            if (assembly == null)
                throw new ResolverException("No assembly found in package!");

            return assembly.FullName;
        }

        public override bool Remove(string source, ILogger logger)
        {
            var options = BuildOptions(source);
            try
            {
                string directory = Path.Combine(_nuGetPackagesRoot, options["package"], options["version"]);
                Directory.Delete(directory, true);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private string[] SplitPath(string path)
        {
            path = path.Replace(_nuGetPackagesRoot, string.Empty);
            path = path.Trim(Path.DirectorySeparatorChar);
            return path.Split(Path.DirectorySeparatorChar);
        }

        public Options BuildOptions(string source)
        {
            Options options = new Options();
            string[] parts = source.Split('@');

            options.Add("package", parts[0]);
            if (parts.Length > 1)
                options.Add("version", parts[1]);

            return options;
        }

        private bool HasNetCoreAppDependencyGroup(IPackageSearchMetadata package)
            => package.DependencySets.Any(d => d.TargetFramework.Framework == ".NETCoreApp");

        private IPackageSearchMetadata GetPackageFromFeed(string packageId, string version)
        {
            PackageMetadataResource packageMetadataResource = _sourceRepository.GetResource<PackageMetadataResource>();
            IEnumerable<IPackageSearchMetadata> searchMetadata = packageMetadataResource
                .GetMetadataAsync(packageId, true, true, _nugetLogger, CancellationToken.None).Result;

            return version == string.Empty
                ? searchMetadata.LastOrDefault() : searchMetadata.FirstOrDefault(s => s.Identity.Version.ToFullString() == version);
        }

        private bool RestoreNuGetPackage(string packageId, string version)
        {
            TargetFrameworkInformation tfi = new TargetFrameworkInformation() { FrameworkName = NuGetFramework.ParseFolder("netcoreapp2.0") };
            LibraryDependency dependency = new LibraryDependency
            {
                LibraryRange = new LibraryRange
                {
                    Name = packageId,
                    VersionRange = VersionRange.Parse(version),
                    TypeConstraint = LibraryDependencyTarget.Package
                }
            };

            PackageSpec spec = new PackageSpec(new List<TargetFrameworkInformation>() { tfi });
            spec.Name = "TempProj";
            spec.Dependencies = new List<LibraryDependency>() { dependency };
            spec.RestoreMetadata = new ProjectRestoreMetadata() { ProjectPath = "TempProj.csproj" };

            SourceCacheContext sourceCacheContext = new SourceCacheContext { DirectDownload = true, IgnoreFailedSources = false };
            RestoreCommandProviders restoreCommandProviders = RestoreCommandProviders.Create
            (
                _nuGetPackagesRoot,
                Enumerable.Empty<string>(),
                new SourceRepository[] { _sourceRepository },
                sourceCacheContext,
                new LocalNuspecCache(),
                _nugetLogger
            );

            RestoreRequest restoreRequest = new RestoreRequest(spec, restoreCommandProviders, sourceCacheContext, _nugetLogger);
            restoreRequest.LockFilePath = Path.Combine(AppContext.BaseDirectory, "project.assets.json");
            restoreRequest.ProjectStyle = ProjectStyle.PackageReference;
            restoreRequest.RestoreOutputPath = _nuGetPackagesRoot;

            RestoreCommand restoreCommand = new RestoreCommand(restoreRequest);
            return restoreCommand.ExecuteAsync().Result.Success;
        }

        private string BuildPackageDirectoryPath(string packageId, string version) => Path.Combine(packageId.ToLower(), version);
    }
}