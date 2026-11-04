# Startup-only integration. Never edits a PowerShell profile or its history policy.
$global:__ArkToken = '__ARK_TOKEN__'
function global:__ArkEmit([string]$kind, [string]$value) {
    [Console]::Write(([char]27) + ']633;Ark;' + $global:__ArkToken + ';' + $kind + ';' + $value + ([char]27) + '\')
}
function global:__ArkBase64([string]$value) { [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($value)) }
$global:__ArkPrompt = $function:prompt
$global:__ArkReading = $false
if (Get-Command PSConsoleHostReadLine -ErrorAction SilentlyContinue) {
    $global:__ArkReadLine = $function:PSConsoleHostReadLine
    function global:PSConsoleHostReadLine {
        $line = & $global:__ArkReadLine
        if ($null -ne $line -and $line.Trim().Length -gt 0) {
            $global:__ArkCommand = $line
            $global:__ArkReading = $true
            __ArkEmit 'start' (__ArkBase64 $line)
        }
        return $line
    }
    __ArkEmit 'cap' '15'
}
else { __ArkEmit 'cap' '9' }
function global:prompt {
    $arkSuccess = $?
    $arkNative = $global:LASTEXITCODE
    if ($global:__ArkReading) {
        $global:__ArkReading = $false
        $arkCode = ''
        # LASTEXITCODE can be stale. Only use it for a single, direct native invocation.
        $arkTokens = $null; $arkErrors = $null
        $arkAst = [Management.Automation.Language.Parser]::ParseInput($global:__ArkCommand, [ref]$arkTokens, [ref]$arkErrors)
        $arkStatements = @($arkAst.EndBlock.Statements)
        if ($arkErrors.Count -eq 0 -and $arkStatements.Count -eq 1 -and
            $arkStatements[0] -is [Management.Automation.Language.PipelineAst] -and $arkStatements[0].PipelineElements.Count -eq 1) {
            $arkElement = $arkStatements[0].PipelineElements[0]
            if ($arkElement -is [Management.Automation.Language.CommandAst]) {
                $arkName = $arkElement.GetCommandName()
                $arkResolved = if ($arkName) { Get-Command -Name $arkName -ErrorAction SilentlyContinue | Select-Object -First 1 }
                if ($arkResolved -and $arkResolved.CommandType -eq 'Application') { $arkCode = [string]$arkNative }
            }
        }
        __ArkEmit 'end' ($arkCode + ',' + $arkSuccess)
    }
    $arkLocation = Get-Location
    if ($arkLocation.Provider.Name -eq 'FileSystem') { __ArkEmit 'cwd' (__ArkBase64 $arkLocation.ProviderPath) }
    else { __ArkEmit 'cwd' '' }
    __ArkEmit 'prompt' ''
    if ($global:__ArkPrompt) { & $global:__ArkPrompt } else { 'PS ' + $arkLocation + '> ' }
}
