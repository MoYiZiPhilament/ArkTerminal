namespace ArkTerminal.Terminal.Sessions;

public sealed class TerminalSessionOptions
{
    public int InitialColumns { get; init; } = 120;
    public int InitialRows { get; init; } = 30;
    public int ReadBufferSize { get; init; } = 16 * 1024;
    public int InputQueueCapacity { get; init; } = 256;
    public int InputChunkSize { get; init; } = 16 * 1024;
    public int MaxCsiLength { get; init; } = 4096;
    public int MaxCsiParameters { get; init; } = 32;
    public int MaxOscLength { get; init; } = 16 * 1024;
    public int MaxScrollbackLines { get; init; } = 5000;
    public TimeSpan ProcessExitGrace { get; init; } = TimeSpan.FromSeconds(2);
    public TimeSpan OutputDrainGrace { get; init; } = TimeSpan.FromSeconds(2);

    internal void Validate()
    {
        if (InitialColumns <= 0 || InitialRows <= 0 || ReadBufferSize <= 0 ||
            InputQueueCapacity <= 0 || InputChunkSize <= 0 || MaxCsiLength < 32 ||
            MaxCsiParameters <= 0 || MaxOscLength < 64 || MaxScrollbackLines < 0 ||
            ProcessExitGrace <= TimeSpan.Zero || OutputDrainGrace <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(TerminalSessionOptions), "终端会话限制必须为有效正数。");
        }
    }
}
