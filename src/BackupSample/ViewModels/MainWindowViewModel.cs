using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Security.AccessControl;
using System.Windows.Forms;
using BackupSample.Models;
using BackupSample.Services;
﻿using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace BackupSample.ViewModels
{
    public partial class MainWindowViewModel : ObservableObject
    {
        #region Fields and Properties
        private readonly string _connectionString = "AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;DefaultEndpointsProtocol=http;BlobEndpoint=http://127.0.0.1:10000/devstoreaccount1;QueueEndpoint=http://127.0.0.1:10001/devstoreaccount1;TableEndpoint=http://127.0.0.1:10002/devstoreaccount1;";
        private readonly BackupService _backupService;
        private readonly RecoveryService _recoveryService;
        public bool IsFileListVisible { get; set; } = false;
        public ObservableCollection<VolumeInfo> VolumeList { get; set; } = [];

        [ObservableProperty]
        private VolumeInfo? selectedVolume = null;

        [ObservableProperty]
        private VolumeInfo? volumeRecoveryLocation = null;

        [ObservableProperty]
        private RecoveryOption selectedRecoveryOption = RecoveryOption.Overwrite; // default

        [ObservableProperty]
        private ObservableCollection<string> selectedPaths = new();

        [ObservableProperty]
        private string? selectedPath;

        [ObservableProperty]
        private ObservableCollection<BackupManifest> availableBackups = new();

        [ObservableProperty]
        private BackupManifest? selectedBackup;

        [ObservableProperty]
        private string recoveryLocation = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

        [ObservableProperty]
        private ObservableCollection<FileMetadata>? selectedBackupFiles = new();

        public bool CanStartBackup => SelectedPaths.Count > 0;
        public bool CanStartVolumeBackup => SelectedVolume != null && SelectedVolume.Path != "";
        public bool CanRecovery => SelectedBackup != null && !string.IsNullOrEmpty(RecoveryLocation);
        public bool CanRecoveryFile =>
            SelectedBackupFiles != null &&
            SelectedBackupFiles.Any(f => f.IsSelected) &&
            !string.IsNullOrEmpty(RecoveryLocation);
        public bool CanRecoveryFolder =>
            SelectedBackup != null &&
            SelectedBackup.Target == BackupTargetType.Folder &&
            !string.IsNullOrEmpty(RecoveryLocation);
        public bool CanRecoveryVolume =>
            SelectedBackup != null &&
            SelectedBackup.Target == BackupTargetType.Volume &&
            VolumeRecoveryLocation != null &&
            VolumeRecoveryLocation.Path != "" &&
            !string.IsNullOrEmpty(RecoveryLocation);

        #endregion

        #region Constructor

        public MainWindowViewModel()
        {
            // For design-time support
            if (DesignerProperties.GetIsInDesignMode(new System.Windows.DependencyObject()))
            {
                _backupService = null!;
                _recoveryService = null!;
                return;
            }

            _backupService = new BackupService(_connectionString);
            _recoveryService = new RecoveryService(_connectionString);

            _ = LoadAvailableBackupsAsync();
            VolumeList = new ObservableCollection<VolumeInfo>();
            LoadVolumes();
        }

        #endregion

        #region Commands

        [RelayCommand]
        private void AddFiles()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Multiselect = true,
                Title = "Select files to backup"
            };

            if (dialog.ShowDialog() == true)
            {
                foreach (var file in dialog.FileNames)
                {
                    if (!SelectedPaths.Contains(file))
                    {
                        SelectedPaths.Add(file);
                    }
                }
                OnPropertyChanged(nameof(CanStartBackup));
            }
        }

        [RelayCommand]
        private void AddFolder()
        {
            using var dialog = new FolderBrowserDialog { Description = "Select folder to backup" };
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                string folderPath = dialog.SelectedPath;
                // --- Check if folder is empty ---
                bool isEmpty =
                    !Directory.EnumerateFiles(folderPath).Any() &&
                    !Directory.EnumerateDirectories(folderPath).Any();

                if (isEmpty)
                {
                    System.Windows.MessageBox.Show(
                        "The selected folder is empty.\nPlease choose a folder that contains files or subfolders.",
                        "Empty Folder",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Warning
                    );
                    return;
                }
                if (!SelectedPaths.Contains(folderPath))
                    SelectedPaths.Add(folderPath);
                _backupService.SetBackupRoot(Path.GetDirectoryName(folderPath)!);

                OnPropertyChanged(nameof(CanStartBackup));
            }
        }

        [RelayCommand]
        private void RemoveSelected()
        {
            if (SelectedPath != null)
            {
                SelectedPaths.Remove(SelectedPath);
                OnPropertyChanged(nameof(CanStartBackup));
            }
        }

        [RelayCommand]
        private async Task StartBackup()
        {
            if (!CanStartBackup) return;

            try
            {
                await _backupService.StartBackupAsync(SelectedPaths.ToList(), BackupTargetType.File);

                await LoadAvailableBackupsAsync();

                // Show success message
                System.Windows.MessageBox.Show(
                    $"Backup completed successfully!",
                    "Backup Success",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(ex.Message, "Backup Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        private async Task StartFolderBackup()
        {
            if (!CanStartBackup) return;

            try
            {
                await _backupService.StartBackupAsync(SelectedPaths.ToList(), BackupTargetType.Folder);

                await LoadAvailableBackupsAsync();

                // Show success message
                System.Windows.MessageBox.Show(
                    $"Backup completed successfully!",
                    "Backup Success",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(ex.Message, "Backup Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }


        [RelayCommand]
        private async Task StartVolumeBackup()
        {
            if (!CanStartVolumeBackup || SelectedVolume == null || SelectedVolume.Path == "") return;

            try
            {
                _backupService.SetBackupRoot(SelectedVolume.Path);
                await _backupService.StartVolumeBackupAsync(SelectedVolume.Path);

                await LoadAvailableBackupsAsync();

                // Show success message
                System.Windows.MessageBox.Show(
                    $"Backup completed successfully!",
                    "Backup Success",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(ex.Message, "Backup Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        private void BrowseRecoveryLocation()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select recovery destination folder",
                CheckFileExists = false,
                CheckPathExists = true,
                ValidateNames = false,
                FileName = "Select Folder",
                Multiselect = false,
                Filter = "All files (*.*)|*.*",
                InitialDirectory = RecoveryLocation
            };

            if (dialog.ShowDialog() == true)
            {
                var directory = System.IO.Path.GetDirectoryName(dialog.FileName);
                if (!string.IsNullOrEmpty(directory))
                {
                    RecoveryLocation = directory;
                }
            }
        }

        [RelayCommand]
        private async Task RecoveryBackup()
        {
            if (!CanRecovery) return;
            if (SelectedBackup == null) return;

            try
            {
                await _recoveryService.RecoveryFilesAsync(SelectedBackup, RecoveryLocation, option: SelectedRecoveryOption);

                // Show success message
                System.Windows.MessageBox.Show(
                    $"Recovery completed successfully!",
                    "Recovery Success",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(ex.Message, "Recovery Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        private void ViewFiles(BackupManifest manifest)
        {
            if (manifest == null) return;

            SelectedBackupFiles = new ObservableCollection<FileMetadata>(manifest.Files);

            IsFileListVisible = true;
        }

        [RelayCommand]
        private async Task RestoreSelectedFiles()
        {
            if (SelectedBackup == null) return;
            if (SelectedBackupFiles == null) return;

            var filesToRestore = SelectedBackupFiles
                .Where(f => f.IsSelected)
                .ToList();

            if (!filesToRestore.Any()) return;

            await _recoveryService.RecoveryFilesAsync(
                SelectedBackup,
                RecoveryLocation,
                filesToRestore.Select(f => f.FilePath).ToList(),
                SelectedRecoveryOption
            );

            System.Windows.MessageBox.Show("Restore completed!");
        }

        [RelayCommand]
        private async Task RestoreSelectedVolume()
        {
            if (!CanRecoveryVolume) return;
            if (SelectedBackup == null || VolumeRecoveryLocation == null || VolumeRecoveryLocation.Path == "") return;

            try
            {
                await _recoveryService.RecoveryFilesAsync(SelectedBackup, VolumeRecoveryLocation.Path, option: SelectedRecoveryOption);

                // Show success message
                System.Windows.MessageBox.Show(
                    $"Recovery completed successfully!",
                    "Recovery Success",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(ex.Message, "Recovery Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        private async Task RefreshAvailableBackups()
        {
            await LoadAvailableBackupsAsync();
        }

        #endregion

        #region Private Methods

        private async Task LoadAvailableBackupsAsync()
        {
            try
            {
                SelectedBackupFiles = new ObservableCollection<FileMetadata>();
                var backups = await _recoveryService.GetAvailableBackupsAsync();
                AvailableBackups.Clear();
                foreach (var backup in backups)
                {
                    AvailableBackups.Add(backup);
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(ex.Message, "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        partial void OnSelectedBackupChanged(BackupManifest? value)
        {
            OnPropertyChanged(nameof(CanRecovery));
            OnPropertyChanged(nameof(CanRecoveryFile));
            OnPropertyChanged(nameof(CanRecoveryFolder));
            OnPropertyChanged(nameof(CanRecoveryVolume));
        }

        partial void OnRecoveryLocationChanged(string value)
        {
            OnPropertyChanged(nameof(CanRecovery));
            OnPropertyChanged(nameof(CanRecoveryFile));
            OnPropertyChanged(nameof(CanRecoveryFolder));
            OnPropertyChanged(nameof(CanRecoveryVolume));
        }

        partial void OnVolumeRecoveryLocationChanged(VolumeInfo? value)
        {
            OnPropertyChanged(nameof(CanRecoveryVolume));
        }

        partial void OnSelectedVolumeChanged(VolumeInfo? value)
        {
            OnPropertyChanged(nameof(CanStartVolumeBackup));
        }

        partial void OnSelectedBackupFilesChanged(
            ObservableCollection<FileMetadata>? oldValue,
            ObservableCollection<FileMetadata>? newValue)
        {
            if (oldValue != null)
            {
                oldValue.CollectionChanged -= SelectedBackupFiles_CollectionChanged;
                foreach (var item in oldValue)
                    item.PropertyChanged -= BackupFile_PropertyChanged;
            }

            if (newValue != null)
            {
                newValue.CollectionChanged += SelectedBackupFiles_CollectionChanged;
                foreach (var item in newValue)
                    item.PropertyChanged += BackupFile_PropertyChanged;
            }

            OnPropertyChanged(nameof(CanRecoveryFile));
        }
        private void SelectedBackupFiles_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
            {
                foreach (FileMetadata item in e.NewItems)
                    item.PropertyChanged += BackupFile_PropertyChanged;
            }

            if (e.OldItems != null)
            {
                foreach (FileMetadata item in e.OldItems)
                    item.PropertyChanged -= BackupFile_PropertyChanged;
            }

            OnPropertyChanged(nameof(CanRecoveryFile));
        }

        private void BackupFile_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(FileMetadata.IsSelected))
            {
                OnPropertyChanged(nameof(CanRecoveryFile));
            }
        }

        private void LoadVolumes()
        {
            VolumeList.Clear();

            VolumeList.Add(new VolumeInfo
            {
                Name = "　",
                Path = ""
            });
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (!drive.IsReady) continue;

                VolumeList.Add(new VolumeInfo
                {
                    Name = drive.Name.Replace("\\", ""),
                    Path = drive.RootDirectory.FullName
                });
            }
        }

        #endregion
    }
}
