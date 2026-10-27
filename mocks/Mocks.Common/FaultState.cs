namespace Mocks.Common;

/// <summary>Current fault switch of one mock process. Settings are immutable; the reference is swapped atomically.</summary>
public sealed class FaultState
{
    private FaultSettings _current = FaultSettings.Healthy;

    public FaultSettings Current => Volatile.Read(ref _current);

    public void Set(FaultSettings settings) => Volatile.Write(ref _current, settings);
}
