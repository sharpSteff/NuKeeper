using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NuKeeper.Abstractions.Logging;
using NuKeeper.Abstractions.NuGet;
using NuKeeper.Abstractions.RepositoryInspection;
using NuKeeper.Inspection.Sort;
using NuKeeper.Update.Process;

namespace NuKeeper.Update
{
    public class UpdateRunner : IUpdateRunner
    {
        private readonly INuKeeperLogger _logger;
        private readonly IFileRestoreCommand _fileRestoreCommand;
        private readonly INuGetUpdatePackageCommand _nuGetUpdatePackageCommand;
        private readonly IDotNetUpdatePackageCommand _dotNetUpdatePackageCommand;
        private readonly IUpdateProjectImportsCommand _updateProjectImportsCommand;
        private readonly IUpdateNuspecCommand _updateNuspecCommand;
        private readonly IUpdateDirectoryBuildTargetsCommand _updateDirectoryBuildTargetsCommand;

        public UpdateRunner(
            INuKeeperLogger logger,
            IFileRestoreCommand fileRestoreCommand,
            INuGetUpdatePackageCommand nuGetUpdatePackageCommand,
            IDotNetUpdatePackageCommand dotNetUpdatePackageCommand,
            IUpdateProjectImportsCommand updateProjectImportsCommand,
            IUpdateNuspecCommand updateNuspecCommand,
            IUpdateDirectoryBuildTargetsCommand updateDirectoryBuildTargetsCommand)
        {
            _logger = logger;
            _fileRestoreCommand = fileRestoreCommand;
            _nuGetUpdatePackageCommand = nuGetUpdatePackageCommand;
            _dotNetUpdatePackageCommand = dotNetUpdatePackageCommand;
            _updateProjectImportsCommand = updateProjectImportsCommand;
            _updateNuspecCommand = updateNuspecCommand;
            _updateDirectoryBuildTargetsCommand = updateDirectoryBuildTargetsCommand;
        }

        public async Task Update(PackageUpdateSet updateSet, NuGetSources sources)
        {
            if (updateSet == null)
            {
                throw new ArgumentNullException(nameof(updateSet));
            }

            var sortedUpdates = Sort(updateSet.CurrentPackages);

            _logger.Detailed($"Updating '{updateSet.SelectedId}' to {updateSet.SelectedVersion} in {sortedUpdates.Count} projects");



            var updateTasks = sortedUpdates.ToList().Select(current =>
                {
                    return Task.Factory.StartNew(async () =>
                    {
                        var updateCommands = GetUpdateCommands(current.Path.PackageReferenceType);
                        foreach (var updateCommand in updateCommands)
                        {
                            await updateCommand.Invoke(current,
                                updateSet.SelectedVersion, updateSet.Selected.Source,
                                sources);
                        }
                    }, CancellationToken.None, TaskCreationOptions.None, TaskScheduler.Default);
                });

            await Task.WhenAll(updateTasks);
        }

        private IReadOnlyCollection<PackageInProject> Sort(IReadOnlyCollection<PackageInProject> packages)
        {
            var sorter = new PackageInProjectTopologicalSort(_logger);
            return sorter.Sort(packages)
                .ToList();
        }

        private IReadOnlyCollection<IPackageCommand> GetUpdateCommands(
            PackageReferenceType packageReferenceType)
        {
            switch (packageReferenceType)
            {
                case PackageReferenceType.PackagesConfig:
                    return new IPackageCommand[]
                    {
                        _fileRestoreCommand,
                        _nuGetUpdatePackageCommand
                    };

                case PackageReferenceType.ProjectFileOldStyle:
                    return new IPackageCommand[]
                    {
                        _updateProjectImportsCommand,
                        //_fileRestoreCommand,
                        _dotNetUpdatePackageCommand
                    };

                case PackageReferenceType.ProjectFile:
                    return new[] { _dotNetUpdatePackageCommand };

                case PackageReferenceType.Nuspec:
                    return new[] { _updateNuspecCommand };

                case PackageReferenceType.DirectoryBuildTargets:
                    return new[] { _updateDirectoryBuildTargetsCommand };

                default:
                    throw new ArgumentOutOfRangeException(nameof(packageReferenceType));
            }
        }
    }
}
