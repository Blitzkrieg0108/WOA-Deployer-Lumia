using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Deployer.Lumia.Gui.Properties;
using Deployer.Tasks;
using Deployer.UI;
using Grace.DependencyInjection;
using Grace.DependencyInjection.Attributes;
using ReactiveUI;

namespace Deployer.Lumia.Gui.ViewModels
{
    [Metadata("Name", "Advanced")]
    [Metadata("Order", 2)]
    public class AdvancedViewModel : ReactiveObject, ISection, IDisposable
    {
        private const string LogsZipName = "PhoneLogs.zip";
        private readonly IDeploymentContext context;
        private readonly IWindowsDeployer deployer;
        private readonly ILogCollector logCollector;
        private readonly IDisposable preparerUpdater;
        private readonly IOperationProgress progress;
        private readonly ILumiaSettingsService lumiaSettingsService;
        private readonly UIServices uiServices;

        private Meta<IDiskLayoutPreparer> selectedPreparer;

        public AdvancedViewModel(ILumiaSettingsService lumiaSettingsService, IFileSystemOperations fileSystemOperations,
            UIServices uiServices, IDeploymentContext context, IOperationContext operationContext,
            IList<Meta<IDiskLayoutPreparer>> diskPreparers,
            IWindowsDeployer deployer,
            ILogCollector logCollector,
            IOperationProgress progress)
        {
            this.lumiaSettingsService = lumiaSettingsService;
            this.uiServices = uiServices;
            this.context = context;
            this.deployer = deployer;
            this.logCollector = logCollector;
            this.progress = progress;

            DiskPreparers = diskPreparers;

            DeleteDownloadedWrapper = new CommandWrapper<Unit, Unit>(this,
                ReactiveCommand.CreateFromTask(() => DeleteDownloaded(fileSystemOperations)), uiServices.ContextDialog, operationContext);
            ForceDualBootWrapper = new CommandWrapper<Unit, Unit>(this, ReactiveCommand.CreateFromTask(ForceDualBoot),
                uiServices.ContextDialog, operationContext);
            ForceSingleBootWrapper = new CommandWrapper<Unit, Unit>(this,
                ReactiveCommand.CreateFromTask(ForceDisableDualBoot), uiServices.ContextDialog, operationContext);

            BackupCommandWrapper =
                new CommandWrapper<Unit, Unit>(this, ReactiveCommand.CreateFromTask(Backup), uiServices.ContextDialog, operationContext);
            RestoreCommandWrapper =
                new CommandWrapper<Unit, Unit>(this, ReactiveCommand.CreateFromTask(Restore), uiServices.ContextDialog, operationContext);

            CollectLogsCommmandWrapper = new CommandWrapper<Unit, Unit>(this, ReactiveCommand.CreateFromTask(CollectLogs), uiServices.ContextDialog, operationContext);

            IsBusyObservable = Observable.Merge(DeleteDownloadedWrapper.Command.IsExecuting,
                BackupCommandWrapper.Command.IsExecuting, RestoreCommandWrapper.Command.IsExecuting,
                ForceDualBootWrapper.Command.IsExecuting, ForceSingleBootWrapper.Command.IsExecuting,
                CollectLogsCommmandWrapper.Command.IsExecuting);

            preparerUpdater = this.WhenAnyValue(x => x.SelectedPreparer)
                .Where(x => x != null)
                .Subscribe(x =>
                {
                    context.DiskLayoutPreparer = x.Value;
                    lumiaSettingsService.DiskPreparer = (string)x.Metadata["Name"];
                });

            SelectedPreparer = GetInitialDiskPreparer();
        }

        private async Task CollectLogs()
        {
            var path = Path.Combine(Path.GetTempPath(), LogsZipName);
            await logCollector.Collect(context.Device, path);
            var fileInfo = new FileInfo(path);
            ExploreFile(fileInfo.FullName);
        }

        private void ExploreFile(string filePath)
        {
            if (!File.Exists(filePath))
            {
                return;
            }
            //Clean up file path so it can be navigated OK
            Process.Start("explorer.exe", $"/select,\"{filePath}\"");
        }

        private Meta<IDiskLayoutPreparer> GetInitialDiskPreparer()
        {
            var fromSettings = DiskPreparers.FirstOrDefault(x => (string)x.Metadata["Name"] == lumiaSettingsService.DiskPreparer);
            return fromSettings ?? Default;
        }

        private Meta<IDiskLayoutPreparer> Default
        {
            get
            {
                return DiskPreparers
                    .OrderBy(x => (int) x.Metadata["Order"])
                    .First();
            }
        }

        public Meta<IDiskLayoutPreparer> SelectedPreparer
        {
            get => selectedPreparer;
            set => this.RaiseAndSetIfChanged(ref selectedPreparer, value);
        }

        public CommandWrapper<Unit, Unit> RestoreCommandWrapper { get; set; }

        public CommandWrapper<Unit, Unit> BackupCommandWrapper { get; set; }

        public CommandWrapper<Unit, Unit> DeleteDownloadedWrapper { get; }

        public bool UseCompactDeployment
        {
            get => lumiaSettingsService.UseCompactDeployment;
            set
            {
                lumiaSettingsService.UseCompactDeployment = value;
                this.RaisePropertyChanged(nameof(UseCompactDeployment));
            }
        }

        public bool CleanDownloadedBeforeDeployment
        {
            get => lumiaSettingsService.CleanDownloadedBeforeDeployment;
            set
            {
                lumiaSettingsService.CleanDownloadedBeforeDeployment = value;
                this.RaisePropertyChanged(nameof(CleanDownloadedBeforeDeployment));
            }
        }

        public CommandWrapper<Unit, Unit> CollectLogsCommmandWrapper { get; }

        public CommandWrapper<Unit, Unit> ForceDualBootWrapper { get; }

        public CommandWrapper<Unit, Unit> ForceSingleBootWrapper { get; }

        public IEnumerable<Meta<IDiskLayoutPreparer>> DiskPreparers { get; set; }

        public void Dispose()
        {
            preparerUpdater?.Dispose();
        }

        public IObservable<bool> IsBusyObservable { get; }

        private async Task ForceDualBoot()
        {
            await ((IPhone) context.Device).ToogleDualBoot(true, true);

            await uiServices.ContextDialog.ShowAlert(this, Resources.Done, Resources.DualBootEnabled);
        }

        private async Task ForceDisableDualBoot()
        {
            await ((IPhone) context.Device).ToogleDualBoot(false, true);

            await uiServices.ContextDialog.ShowAlert(this, Resources.Done, Resources.DualBootDisabled);
        }

        private async Task Backup()
        {
            uiServices.SaveFilePicker.DefaultExt = "*.wim";
            uiServices.SaveFilePicker.Filter = "Windows Images (.wim)|*.wim";
            var imagePath = uiServices.SaveFilePicker.PickFile();
            if (imagePath == null)
            {
                return;
            }

            context.DeploymentOptions = new WindowsDeploymentOptions
            {
                ImageIndex = 1,
                ImagePath = imagePath,
                UseCompact = lumiaSettingsService.UseCompactDeployment
            };

            await deployer.Backup(await context.Device.GetWindowsPartition(), imagePath, progress);

            await uiServices.ContextDialog.ShowAlert(this, Resources.Done, Resources.ImageCaptured);
        }

        private async Task Restore()
        {
            var filters = new List<(string, IEnumerable<string>)>
            {
                ("install.wim", new[]
                {
                    "install.wim"
                }),
                ("Windows Images", new[]
                {
                    "*.wim",
                    "*.esd"
                }),
                ("All files", new[]
                {
                    "*.*"
                })
            };

            var fileName = uiServices.OpenFilePicker.Pick(filters, () => lumiaSettingsService.WimFolder,
                x => { lumiaSettingsService.WimFolder = x; });

            if (fileName == null)
            {
                return;
            }

            context.DeploymentOptions = new WindowsDeploymentOptions
            {
                ImageIndex = 1,
                ImagePath = fileName,
                UseCompact = lumiaSettingsService.UseCompactDeployment
            };

            await context.DiskLayoutPreparer.Prepare(await context.Device.GetDeviceDisk());
            await deployer.Deploy(context.DeploymentOptions, context.Device, progress);

            await uiServices.ContextDialog.ShowAlert(this, Resources.Done, Resources.ImageRestored);
        }

        private async Task DeleteDownloaded(IFileSystemOperations fileSystemOperations)
        {
            if (fileSystemOperations.DirectoryExists(AppPaths.ArtifactDownload))
            {
                await fileSystemOperations.DeleteDirectory(AppPaths.ArtifactDownload);
                await uiServices.ContextDialog.ShowAlert(this, Resources.Done, UI.Properties.Resources.DownloadedFolderDeleted);
            }
            else
            {
                await uiServices.ContextDialog.ShowAlert(this, UI.Properties.Resources.DownloadedFolderNotFoundTitle,
                    UI.Properties.Resources.DownloadedFolderNotFound);
            }
        }
    }
}