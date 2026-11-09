namespace Chasm.Core.Session;

/// <summary>
/// The day cycle. <see cref="Time"/> runs 0..1 through one day (0 = dawn). Resting skips half a day.
/// Day length and when night starts come from GameConfigDef.
/// </summary>
public sealed class DayClock
{
    public int Day { get; private set; } = 1;
    public float Time { get; private set; }
    public float DayLengthSeconds { get; set; } = 600;
    public float NightStartsAt { get; set; } = 0.65f;

    public bool IsNight => Time >= NightStartsAt;

    /// <summary>Raised when a new day starts (daily dungeons reset on this).</summary>
    public event Action<int>? NewDay;

    public void Advance(float seconds)
    {
        if (DayLengthSeconds <= 0) return;
        AddFraction(seconds / DayLengthSeconds);
    }

    public void Rest() => AddFraction(0.5f);

    private void AddFraction(float f)
    {
        Time += f;
        while (Time >= 1f)
        {
            Time -= 1f;
            Day++;
            NewDay?.Invoke(Day);
        }
    }

    public void Load(int day, float time) { Day = Math.Max(1, day); Time = Math.Clamp(time, 0, 0.9999f); }
}
