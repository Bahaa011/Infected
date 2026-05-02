using System;

public static class WorldLoadingState
{
    public static bool IsLoading { get; private set; } = false;
    public static bool IsWorldReady { get; private set; } = true;

    public static event Action<bool> OnChanged;

    public static void BeginLoading()
    {
        IsLoading = true;
        IsWorldReady = false;
        UnityEngine.Debug.Log("[WorldLoadingState] BeginLoading -> IsWorldReady = false");
        OnChanged?.Invoke(false);
    }

    public static void MarkWorldReady()
    {
        IsLoading = false;
        IsWorldReady = true;
        UnityEngine.Debug.Log("[WorldLoadingState] MarkWorldReady -> IsWorldReady = true");
        OnChanged?.Invoke(true);
    }
}
