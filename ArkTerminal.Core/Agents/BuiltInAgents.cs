namespace ArkTerminal.Core.Agents;

public static class BuiltInAgents
{
    // Reviewed 2026-11-02. Candidates are command names, never product display names.
    public static IReadOnlyList<AgentDefinition> Create() =>
    [
        Agent("codex", "Codex", "codex", "https://developers.openai.com/codex/cli/reference/",
            permissions: new() { ["read-only"] = ["--sandbox", "read-only"], ["workspace-write"] = ["--sandbox", "workspace-write"] },
            options: [Flag("--no-alt-screen"), Flag("--add-dir", true, true)]),
        new()
        {
            Id = "deepseek-harness", DisplayName = "DeepSeek Harness", ExecutableCandidates = Candidates("dsh"),
            VersionProbe = new(), Evidence = "https://github.com/deepseek-ai/deepseek-harness/blob/master/apps/cli/README.md",
            UnsupportedReason = Localize.T("Text0591")
        },
        Agent("claude-code", "Claude Code", "claude", "https://code.claude.com/docs/en/cli-reference",
            permissions: new() { ["default"] = ["--permission-mode", "default"], ["plan"] = ["--permission-mode", "plan"], ["acceptEdits"] = ["--permission-mode", "acceptEdits"] },
            options: [Flag("--continue"), Flag("--add-dir", true, true)]),
        Agent("kimi-code", "Kimi Code", "kimi", "https://www.kimi.com/code/docs/en/kimi-code-cli/reference/kimi-command.html",
            permissions: new() { ["plan"] = ["--plan"] }, options: [Flag("--continue"), Flag("--add-dir", true, true)]),
        Agent("opencode", "OpenCode", "opencode", "https://opencode.ai/docs/cli/", options: [Flag("--continue")]),
        Agent("antigravity", "Antigravity CLI", "agy", "https://antigravity.google/docs/cli/reference/",
            permissions: new() { ["plan"] = ["--mode", "plan"], ["accept-edits"] = ["--mode", "accept-edits"] }),
        Agent("codebuddy", "CodeBuddy Code", "codebuddy", "https://www.codebuddy.ai/docs/cli/cli-reference",
            permissions: new() { ["default"] = ["--permission-mode", "default"], ["plan"] = ["--permission-mode", "plan"], ["acceptEdits"] = ["--permission-mode", "acceptEdits"] },
            options: [Flag("--continue"), Flag("--add-dir", true, true)])
    ];

    private static List<string> Candidates(string command) => [command + ".exe", command + ".cmd", command + ".ps1"];
    private static AgentArgumentOption Flag(string flag, bool value = false, bool repeat = false) => new() { Flag = flag, TakesValue = value, Repeatable = repeat };
    private static AgentDefinition Agent(string id, string name, string command, string evidence,
        Dictionary<string, List<string>>? permissions = null, List<AgentArgumentOption>? options = null) => new()
    {
        Id = id, DisplayName = name, ExecutableCandidates = Candidates(command), VersionProbe = new(), Evidence = evidence,
        Variables = ["model", "workingDirectory"],
        Fields = new() { Model = ["--model", "{{model}}"], PermissionModes = permissions ?? [], Arguments = options ?? [] },
        Capabilities = new() { InteractiveInput = true, Resume = options?.Any(option => option.Flag == "--continue") == true }
    };
}
