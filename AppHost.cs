using ArkTerminal.Core.Configuration;
using ArkTerminal.Core.Data;
using ArkTerminal.Core.Diagnostics;
using ArkTerminal.Core.Profiles;
using ArkTerminal.Core.Recovery;
using ArkTerminal.Core.Storage;
using ArkTerminal.Core.Validation;
using ArkTerminal.Terminal.Sessions;

namespace ArkTerminal;

public sealed class AppDataInitializationException : Exception
{
    public AppDataInitializationException(string? path, string message) : base(message) => ErrorPath = path;
    public string? ErrorPath { get; }
}

public sealed partial class AppHost : IAsyncDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _draftGate = new(1, 1);
    private readonly SessionManager _sessionManager = new();
    private ISettingsService _settingsService = null!;
    private IProfileService _profileService = null!;
    private ISettingsDraftService _draftService = null!;
    private IRecoveryStateService _recoveryService = null!;
    private IRecentProjectsService _recentProjectsService = null!;
    private readonly SemaphoreSlim _layoutGate = new(1, 1);
    private SettingsDraft? _recoveredDraft;
    private string? _recoveredDraftRevision;
    private bool _shutdown;

    public StorageOptions StorageOptions { get; } = new();
    public ISessionManager Sessions => _sessionManager;
    public IAppDataPaths Paths { get; private set; } = null!;
    public DiagnosticService Diagnostics { get; private set; } = null!;
    public ArkTerminal.Terminal.Mcp.ReadOnlyMcpHost? ReadOnlyMcp { get; internal set; }
    public AppSettings? FormalSettings { get; private set; }
    public ProfileCatalog? Catalog { get; private set; }
    public RecentProjectsDocument RecentProjects { get; private set; } = new();
    public string RecentProjectsRevision { get; private set; } = string.Empty;
    public SettingsDraft? Draft { get; private set; }
    public RecoveryState? Recovery { get; private set; }
    public bool RecoveryAvailable { get; private set; }
    public bool RecoveryAccepted { get; private set; }
    public string SettingsRevision { get; private set; } = string.Empty;
    public string ProfilesRevision { get; private set; } = string.Empty;
    public string DraftRevision { get; private set; } = string.Empty;
    public string RecoveryRevision { get; private set; } = string.Empty;
    public List<string> StartupIssues { get; } = [];
    public DocumentLoadStatus? SettingsLoadStatus { get; private set; }
    public DocumentLoadStatus? ProfilesLoadStatus { get; private set; }
    public bool CanEdit => FormalSettings is not null && Catalog is not null;

    public async Task InitializeAsync(IEnumerable<string> arguments, IPackagingContext packagingContext)
    {
        var dataPaths = await AppDataPathsFactory.CreateAsync(arguments, packagingContext, _lifetime.Token);
        if (!dataPaths.Success)
        {
            throw new AppDataInitializationException(dataPaths.ErrorPath, dataPaths.ErrorMessage ?? "数据目录初始化失败。");
        }

        Paths = dataPaths.Paths!;
        Diagnostics = new DiagnosticService(Paths);
        InitializeShellData();
        await InitializeAgentsAsync();
        _profileService = new ProfileService(Paths, StorageOptions);
        _settingsService = new SettingsService(Paths, StorageOptions);
        _draftService = new SettingsDraftService(Paths, StorageOptions);
        _profilesDraftService = new ProfilesDraftService(Paths, StorageOptions);
        _recoveryService = new RecoveryStateService(Paths, _draftService, StorageOptions);
        _recentProjectsService = new RecentProjectsService(Paths, StorageOptions);

        var recentProjects = await _recentProjectsService.LoadAsync(_lifetime.Token);
        if (recentProjects.Success)
        {
            RecentProjects = recentProjects.Value!;
            RecentProjectsRevision = recentProjects.Revision ?? string.Empty;
        }
        else
        {
            StartupIssues.Add(recentProjects.Error ?? "最近项目加载失败。");
        }

        var profiles = await _profileService.LoadAsync(_lifetime.Token);
        ProfilesLoadStatus = profiles.Status;
        if (profiles.Success)
        {
            Catalog = profiles.Value;
            ProfilesRevision = profiles.Revision!;
        }
        else
        {
            StartupIssues.Add(profiles.Error ?? "Profile 加载失败。");
            Diagnostics.Record(DiagnosticComponent.Profiles, DiagnosticEventKind.ReadFailed, (int)profiles.Status);
        }

        var profileIds = Catalog?.Profiles.Select(profile => profile.Id).ToArray();
        var settings = await _settingsService.LoadAsync(profileIds, _lifetime.Token);
        SettingsLoadStatus = settings.Status;
        if (settings.Success)
        {
            FormalSettings = settings.Value;
            SettingsRevision = settings.Revision!;
        }
        else
        {
            StartupIssues.Add(settings.Error ?? "设置加载失败。");
            Diagnostics.Record(DiagnosticComponent.Configuration, DiagnosticEventKind.ReadFailed, (int)settings.Status);
        }

        var recovery = await _recoveryService.LoadForStartupAsync(_lifetime.Token);
        if (recovery.MarkerError is not null)
        {
            StartupIssues.Add(recovery.MarkerError.Error ?? "异常退出标记写入失败。");
        }
        if (!recovery.AbnormalExit)
        {
            return;
        }

        if (recovery.State?.Success == true)
        {
            Recovery = recovery.State.Value;
            RecoveryRevision = recovery.State.Revision!;
        }
        else if (recovery.State is not null && recovery.State.Status != DocumentLoadStatus.NotFound)
        {
            StartupIssues.Add(recovery.State.Error ?? "恢复布局加载失败。");
        }

        if (recovery.Draft?.Success == true && profileIds is not null)
        {
            var validation = ConfigurationValidator.ValidateSettings(recovery.Draft.Value?.Settings, profileIds);
            if (validation.IsValid)
            {
                if (!(recovery.Draft.Value!.CommitRequested && SameDocument(recovery.Draft.Value.Settings, FormalSettings)))
                {
                    _recoveredDraft = recovery.Draft.Value;
                    _recoveredDraftRevision = recovery.Draft.Revision;
                }
                else await _draftService.DeleteAsync(_lifetime.Token);
            }
            else
            {
                StartupIssues.Add(validation.Summary);
            }
        }
        else if (recovery.Draft is not null && recovery.Draft.Status != DocumentLoadStatus.NotFound)
        {
            StartupIssues.Add(recovery.Draft.Error ?? "设置草稿加载失败。");
        }

        if (recovery.ProfilesDraft?.Success == true)
        {
            var candidate = recovery.ProfilesDraft.Value!;
            if (!(candidate.CommitRequested && SameDocument(candidate.Catalog, Catalog)))
            {
                _recoveredProfilesDraft = candidate;
                _recoveredProfilesDraftRevision = recovery.ProfilesDraft.Revision;
            }
            else await _profilesDraftService.DeleteAsync(_lifetime.Token);
        }
        else if (recovery.ProfilesDraft is not null && recovery.ProfilesDraft.Status != DocumentLoadStatus.NotFound)
            StartupIssues.Add(recovery.ProfilesDraft.Error ?? "Profile 草稿加载失败。");
        RecoveryAvailable = _recoveredDraft is not null || _recoveredProfilesDraft is not null || Recovery?.Tabs.Count > 0;
        StartRecordingMaintenance();
    }

    public async Task<DocumentSaveResult> BeginDraftAsync()
    {
        if (!CanEdit)
        {
            return Failure("配置未成功加载，不能创建草稿。");
        }
        if (Draft is not null)
        {
            return new DocumentSaveResult { Status = DocumentSaveStatus.Success, Revision = DraftRevision };
        }

        Draft = new SettingsDraft
        {
            DraftId = Guid.NewGuid().ToString("N"),
            BaseRevision = SettingsRevision,
            Settings = FormalSettings!.DeepClone()
        };
        DraftRevision = string.Empty;
        return await PersistDraftAsync();
    }

    public void UpdateDraft(AppSettings settings)
    {
        if (Draft is null)
        {
            throw new InvalidOperationException("尚未开始草稿编辑。");
        }
        Draft.Settings = settings;
        Draft.CommitRequested = false;
    }

    public async Task<DocumentSaveResult> PersistDraftAsync()
    {
        if (Draft is null)
        {
            return Failure("没有待保存草稿。");
        }

        await _draftGate.WaitAsync();
        try
        {
            if (Draft is null || _lifetime.IsCancellationRequested)
            {
                return Failure("草稿编辑已结束，取消未完成的自动保存。");
            }
            var saved = await _draftService.SaveAsync(Draft, DraftRevision, _lifetime.Token);
            if (!saved.Success)
            {
                return saved;
            }
            DraftRevision = saved.Revision!;
            return await EnsureDraftRecoveryReferenceAsync();
        }
        finally
        {
            _draftGate.Release();
        }
    }

    public async Task<DocumentSaveResult> SaveFormalAsync()
    {
        if (Draft is null || Catalog is null)
        {
            return Failure("没有可提交的设置草稿。");
        }

        await _draftGate.WaitAsync();
        try
        {
            var committing = Draft.Settings.DeepClone();
            Draft.CommitRequested = true;
            var intent = await _draftService.SaveAsync(Draft, DraftRevision, _lifetime.Token);
            if (!intent.Success) return intent;
            DraftRevision = intent.Revision!;
            var profileIds = Catalog.Profiles.Select(profile => profile.Id).ToArray();
            var saved = await _settingsService.SaveAsync(committing, profileIds, Draft.BaseRevision, _lifetime.Token);
            if (!saved.Success)
            {
                return saved;
            }

            FormalSettings = committing;
            SettingsRevision = saved.Revision!;
            Draft.BaseRevision = saved.Revision!;
            var cleanup = await DeleteDraftAndClearReferenceAsync();
            Draft = null;
            DraftRevision = string.Empty;
            _recoveredDraft = null;
            return new DocumentSaveResult { Status = DocumentSaveStatus.Success, Revision = saved.Revision,
                Warning = cleanup.Success ? null : "正式设置已保存；恢复草稿清理失败，可重试清理。" };
        }
        finally
        {
            _draftGate.Release();
        }
    }

    public async Task<DocumentSaveResult> CancelDraftAsync()
    {
        await _draftGate.WaitAsync();
        try
        {
            return await DeleteDraftAndClearReferenceAsync();
        }
        finally
        {
            _draftGate.Release();
        }
    }

    public Task AcceptRecoveryAsync()
    {
        RecoveryAccepted = true;
        if (_recoveredDraft is not null && Draft is null)
        {
            Draft = _recoveredDraft;
            DraftRevision = _recoveredDraftRevision!;
        }
        if (_recoveredProfilesDraft is not null && ProfilesDraft is null)
        {
            ProfilesDraft = _recoveredProfilesDraft;
            ProfilesDraftRevision = _recoveredProfilesDraftRevision!;
        }
        return Task.CompletedTask;
    }

    public async Task<DocumentSaveResult> DiscardRecoveryAsync()
    {
        var result = await _recoveryService.DiscardAsync(_lifetime.Token);
        if (!result.Success)
        {
            return result;
        }

        RecoveryAvailable = false;
        RecoveryAccepted = false;
        Recovery = null;
        RecoveryRevision = string.Empty;
        _recoveredDraft = null;
        _recoveredDraftRevision = null;
        _recoveredProfilesDraft = null;
        _recoveredProfilesDraftRevision = null;
        ProfilesDraft = null;
        ProfilesDraftRevision = string.Empty;
        if (Draft is not null)
        {
            Draft = null;
            DraftRevision = string.Empty;
        }
        return result;
    }

    public async Task<DocumentSaveResult> SaveLayoutAsync(IReadOnlyList<RecoveryTabState> tabs, string? activeTabId, CancellationToken cancellationToken = default)
    {
        await _layoutGate.WaitAsync(cancellationToken);
        try
        {
            Recovery ??= new RecoveryState();
            Recovery.Tabs = tabs.Select(tab => new RecoveryTabState
            {
                Id = tab.Id,
                Title = tab.Title,
                ProfileId = tab.ProfileId,
                WorkingDirectory = tab.WorkingDirectory,
                WorkingDirectoryIsExplicit = tab.WorkingDirectoryIsExplicit,
                WorkspaceId = tab.WorkspaceId,
                Order = tab.Order
            }).ToList();
            Recovery.ActiveTabId = activeTabId;
            var saved = await _recoveryService.SaveAsync(Recovery, RecoveryRevision, cancellationToken);
            if (saved.Success)
            {
                RecoveryRevision = saved.Revision!;
            }
            return saved;
        }
        finally
        {
            _layoutGate.Release();
        }
    }

    public async Task<DocumentSaveResult> SaveRecentProjectsAsync(RecentProjectsDocument document, CancellationToken cancellationToken = default)
    {
        var saved = await _recentProjectsService.SaveAsync(document, RecentProjectsRevision, cancellationToken);
        if (saved.Success)
        {
            RecentProjects = document;
            RecentProjectsRevision = saved.Revision!;
        }
        return saved;
    }

    public async Task<DocumentSaveResult> ResetInvalidDocumentsAsync()
    {
        if (Catalog is null && ProfilesLoadStatus is DocumentLoadStatus.Corrupted or DocumentLoadStatus.Invalid or DocumentLoadStatus.TooLarge)
        {
            var reset = await _profileService.ResetAsync(_lifetime.Token);
            if (!reset.Success)
            {
                return reset;
            }
            Catalog = ProfileDefaults.CreateCatalog();
            ProfilesRevision = reset.Revision!;
            ProfilesLoadStatus = DocumentLoadStatus.Success;
        }

        if (FormalSettings is null && SettingsLoadStatus is DocumentLoadStatus.Corrupted or DocumentLoadStatus.Invalid or DocumentLoadStatus.TooLarge && Catalog is not null)
        {
            var ids = Catalog.Profiles.Select(profile => profile.Id).ToArray();
            var reset = await _settingsService.ResetAsync(ids, _lifetime.Token);
            if (!reset.Success)
            {
                return reset;
            }
            FormalSettings = ConfigurationDefaults.CreateSettings();
            FormalSettings.DefaultProfileId = ids.Contains("cmd") ? "cmd" : ids.FirstOrDefault();
            SettingsRevision = reset.Revision!;
            SettingsLoadStatus = DocumentLoadStatus.Success;
        }

        StartupIssues.Clear();
        return new DocumentSaveResult { Status = DocumentSaveStatus.Success };
    }

    public bool CanResetInvalidDocuments =>
        Catalog is null && ProfilesLoadStatus is DocumentLoadStatus.Corrupted or DocumentLoadStatus.Invalid or DocumentLoadStatus.TooLarge ||
        FormalSettings is null && SettingsLoadStatus is DocumentLoadStatus.Corrupted or DocumentLoadStatus.Invalid or DocumentLoadStatus.TooLarge;

    public async Task<DocumentSaveResult> ShutdownAsync()
    {
        if (_shutdown)
        {
            return new DocumentSaveResult { Status = DocumentSaveStatus.Success };
        }
        if (Draft is not null || ProfilesDraft is not null)
        {
            return Failure("仍有未保存草稿，不能执行正常关闭清理。");
        }

        await _draftGate.WaitAsync();
        try
        {
            var result = await _recoveryService.CompleteNormalShutdownAsync(CancellationToken.None);
            if (result.Success)
            {
                _shutdown = true;
                _lifetime.Cancel();
            }
            return result;
        }
        finally
        {
            _draftGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel();
        if (ReadOnlyMcp is not null) await ReadOnlyMcp.DisposeAsync().ConfigureAwait(false);
        if (_recordingMaintenance is not null) await _recordingMaintenance.ConfigureAwait(false);
        await _sessionManager.DisposeAsync().ConfigureAwait(false);
        await WaitForRecordingStopsAsync().ConfigureAwait(false);
        if (CommandHistory is not null) await CommandHistory.DisposeAsync().ConfigureAwait(false);
        if (Transcripts is not null) await Transcripts.DisposeAsync().ConfigureAwait(false);
        if (Diagnostics is not null) await Diagnostics.DisposeAsync().ConfigureAwait(false);
        _draftGate.Dispose();
        _layoutGate.Dispose();
        _lifetime.Dispose();
    }

    private async Task<DocumentSaveResult> EnsureDraftRecoveryReferenceAsync()
    {
        if (Recovery is null)
        {
            Recovery = new RecoveryState();
            RecoveryRevision = string.Empty;
        }
        if (Recovery.SettingsDraftId == Draft!.DraftId)
        {
            return new DocumentSaveResult { Status = DocumentSaveStatus.Success, Revision = DraftRevision };
        }

        await _layoutGate.WaitAsync(_lifetime.Token);
        try
        {
            Recovery.SettingsDraftId = Draft.DraftId;
            var saved = await _recoveryService.SaveAsync(Recovery, RecoveryRevision, _lifetime.Token);
            if (saved.Success)
            {
                RecoveryRevision = saved.Revision!;
            }
            return saved;
        }
        finally
        {
            _layoutGate.Release();
        }
    }

    private async Task<DocumentSaveResult> DeleteDraftAndClearReferenceAsync()
    {
        var deleted = await _draftService.DeleteAsync(_lifetime.Token);
        if (!deleted.Success)
        {
            return deleted;
        }

        if (Recovery?.SettingsDraftId is not null)
        {
            await _layoutGate.WaitAsync(_lifetime.Token);
            try
            {
                Recovery.SettingsDraftId = null;
                var saved = await _recoveryService.SaveAsync(Recovery, RecoveryRevision, _lifetime.Token);
                if (!saved.Success)
                {
                    return saved;
                }
                RecoveryRevision = saved.Revision!;
            }
            finally
            {
                _layoutGate.Release();
            }
        }

        Draft = null;
        DraftRevision = string.Empty;
        _recoveredDraft = null;
        _recoveredDraftRevision = null;
        return new DocumentSaveResult { Status = DocumentSaveStatus.Success };
    }

    private static DocumentSaveResult Failure(string message) => new()
    {
        Status = DocumentSaveStatus.Invalid,
        Error = message
    };
}
