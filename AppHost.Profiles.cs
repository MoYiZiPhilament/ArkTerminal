using System.Text.Json;
using ArkTerminal.Core.Configuration;
using ArkTerminal.Core.Profiles;
using ArkTerminal.Core.Recovery;
using ArkTerminal.Core.Storage;

namespace ArkTerminal;

public sealed partial class AppHost
{
    private ProfilesDraftService _profilesDraftService = null!;
    private ProfilesDraft? _recoveredProfilesDraft;
    private string? _recoveredProfilesDraftRevision;
    public ProfilesDraft? ProfilesDraft { get; private set; }
    public string ProfilesDraftRevision { get; private set; } = string.Empty;

    public void EnsureSettingsDraft()
    {
        if (!CanEdit) throw new InvalidOperationException(Localize.T("Text0022"));
        Draft ??= new() { DraftId = Guid.NewGuid().ToString("N"), BaseRevision = SettingsRevision, Settings = FormalSettings!.DeepClone() };
    }

    public void EnsureProfilesDraft()
    {
        if (!CanEdit) throw new InvalidOperationException(Localize.T("Text0022"));
        ProfilesDraft ??= new() { DraftId = Guid.NewGuid().ToString("N"), BaseRevision = ProfilesRevision, Catalog = Catalog!.DeepClone() };
        ProfilesDraft.CommitRequested = false;
    }

    public async Task<DocumentSaveResult> PersistProfilesDraftAsync()
    {
        await _draftGate.WaitAsync();
        try
        {
            if (ProfilesDraft is null) return Failure(Localize.T("Text0023"));
            if (ValidateAgentProfiles() is { } agentError) return Failure(agentError);
            var saved = await _profilesDraftService.SaveAsync(ProfilesDraft, ProfilesDraftRevision, _lifetime.Token);
            if (!saved.Success) return saved;
            ProfilesDraftRevision = saved.Revision!;
            Recovery ??= new();
            await _layoutGate.WaitAsync(_lifetime.Token);
            try
            {
                Recovery.ProfilesDraftId = ProfilesDraft.DraftId;
                var reference = await _recoveryService.SaveAsync(Recovery, RecoveryRevision, _lifetime.Token);
                if (reference.Success) RecoveryRevision = reference.Revision!;
                return reference;
            }
            finally { _layoutGate.Release(); }
        }
        finally { _draftGate.Release(); }
    }

    public async Task<DocumentSaveResult> SaveProfilesAsync()
    {
        await _draftGate.WaitAsync();
        try
        {
            if (ProfilesDraft is null) return Failure(Localize.T("Text0023"));
            if (ValidateAgentProfiles() is { } agentError) return Failure(agentError);
            var ids = ProfilesDraft.Catalog.Profiles.Select(profile => profile.Id).ToArray();
            if (new[] { FormalSettings?.DefaultProfileId, Draft?.Settings.DefaultProfileId }
                .Any(id => id is not null && !ids.Contains(id, StringComparer.OrdinalIgnoreCase)))
                return Failure(Localize.T("Text0024"));
            ProfilesDraft.CommitRequested = true;
            var committing = ProfilesDraft.Catalog.DeepClone();
            var intent = await _profilesDraftService.SaveAsync(ProfilesDraft, ProfilesDraftRevision, _lifetime.Token);
            if (!intent.Success) return intent;
            ProfilesDraftRevision = intent.Revision!;
            var saved = await _profileService.SaveAsync(committing, ProfilesDraft.BaseRevision, _lifetime.Token);
            if (!saved.Success) return saved;
            Catalog = committing;
            ProfilesRevision = saved.Revision!;
            var cleanup = await ClearProfilesDraftAsync();
            ProfilesDraft = null;
            ProfilesDraftRevision = string.Empty;
            _recoveredProfilesDraft = null;
            return new() { Status = DocumentSaveStatus.Success, Revision = saved.Revision,
                Warning = cleanup.Success ? null : Localize.T("Text0025") };
        }
        finally { _draftGate.Release(); }
    }

    public async Task<DocumentSaveResult> CancelProfilesDraftAsync()
    {
        await _draftGate.WaitAsync();
        try { return await ClearProfilesDraftAsync(); }
        finally { _draftGate.Release(); }
    }

    public async Task<DocumentSaveResult> RetryDraftCleanupAsync()
    {
        if (Draft is not null || ProfilesDraft is not null) return Failure(Localize.T("Text0026"));
        var settings = await CancelDraftAsync();
        return settings.Success ? await CancelProfilesDraftAsync() : settings;
    }

    private async Task<DocumentSaveResult> ClearProfilesDraftAsync()
    {
        var deleted = await _profilesDraftService.DeleteAsync(_lifetime.Token);
        if (!deleted.Success) return deleted;
        if (Recovery?.ProfilesDraftId is not null)
        {
            await _layoutGate.WaitAsync(_lifetime.Token);
            try
            {
                Recovery.ProfilesDraftId = null;
                var saved = await _recoveryService.SaveAsync(Recovery, RecoveryRevision, _lifetime.Token);
                if (!saved.Success) return saved;
                RecoveryRevision = saved.Revision!;
            }
            finally { _layoutGate.Release(); }
        }
        ProfilesDraft = null;
        ProfilesDraftRevision = string.Empty;
        _recoveredProfilesDraft = null;
        _recoveredProfilesDraftRevision = null;
        return new() { Status = DocumentSaveStatus.Success };
    }

    private static bool SameDocument<T>(T? first, T? second) => JsonSerializer.Serialize(first) == JsonSerializer.Serialize(second);

    public async Task<DocumentSaveResult> ReloadFormalAsync()
    {
        var profiles = await _profileService.LoadAsync(_lifetime.Token);
        if (!profiles.Success) return Failure(profiles.Error ?? Localize.T("Text0027"));
        var settings = await _settingsService.LoadAsync(profiles.Value!.Profiles.Select(profile => profile.Id).ToArray(), _lifetime.Token);
        if (!settings.Success) return Failure(settings.Error ?? Localize.T("Text0028"));
        Catalog = profiles.Value;
        ProfilesRevision = profiles.Revision!;
        FormalSettings = settings.Value;
        SettingsRevision = settings.Revision!;
        // Draft base revisions deliberately remain unchanged: reloading must not authorize overwrites.
        return new() { Status = DocumentSaveStatus.Success };
    }
}
