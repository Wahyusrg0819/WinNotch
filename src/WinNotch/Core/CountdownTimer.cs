using System;

namespace WinNotch.Core;

public enum TimerStatus { Idle, Running, Paused, Completed }
public enum TimerMode { Custom, Focus, ShortBreak, LongBreak }

public sealed class CountdownTimer
{
    private readonly Func<long> milliseconds;
    private long startedAt;
    private TimeSpan remainingAtStart;
    public TimerStatus Status { get; private set; }
    public TimerMode Mode { get; private set; }
    public TimeSpan Remaining { get; private set; }
    public int FocusSessions { get; private set; }
    public bool IsActive => Status != TimerStatus.Idle;
    public string Label => Mode switch { TimerMode.Focus => "Focus", TimerMode.ShortBreak => "Short break", TimerMode.LongBreak => "Long break", _ => "Timer" };
    public TimerMode NextMode => Mode == TimerMode.Focus ? (FocusSessions % 4 == 0 ? TimerMode.LongBreak : TimerMode.ShortBreak) : TimerMode.Focus;
    public event Action? Changed;
    public event Action? Completed;

    // On our .NET 10 Windows target TickCount64 includes sleep and ignores wall-clock edits.
    public CountdownTimer(Func<long>? clock = null) => milliseconds = clock ?? (() => Environment.TickCount64);

    public void Start(TimeSpan duration, TimerMode mode = TimerMode.Custom)
    {
        if (duration < TimeSpan.FromSeconds(1) || duration > TimeSpan.FromMinutes(180)) throw new ArgumentOutOfRangeException(nameof(duration));
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        if (Status is TimerStatus.Running or TimerStatus.Paused) return;
        if (mode == TimerMode.Custom) FocusSessions = 0;
        Mode = mode;
        Remaining = remainingAtStart = duration;
        startedAt = milliseconds();
        Status = TimerStatus.Running;
        Changed?.Invoke();
    }

    public void StartPomodoro(TimerMode mode = TimerMode.Focus) => Start(TimeSpan.FromMinutes(mode switch
        { TimerMode.Focus => 25, TimerMode.ShortBreak => 5, TimerMode.LongBreak => 15, _ => throw new ArgumentOutOfRangeException(nameof(mode)) }), mode);

    public void NextPomodoro()
    {
        if (Status == TimerStatus.Completed && Mode != TimerMode.Custom) StartPomodoro(NextMode);
    }

    public void Tick()
    {
        if (Status != TimerStatus.Running) return;
        Remaining = remainingAtStart - TimeSpan.FromMilliseconds(Math.Max(0, milliseconds() - startedAt));
        if (Remaining <= TimeSpan.Zero)
        {
            Remaining = TimeSpan.Zero;
            Status = TimerStatus.Completed;
            if (Mode == TimerMode.Focus) FocusSessions++;
            Changed?.Invoke();
            Completed?.Invoke();
        }
        else Changed?.Invoke();
    }

    public void TogglePause()
    {
        if (Status == TimerStatus.Running)
        {
            Tick();
            if (Status != TimerStatus.Running) return;
            remainingAtStart = Remaining;
            Status = TimerStatus.Paused;
        }
        else if (Status == TimerStatus.Paused) { startedAt = milliseconds(); Status = TimerStatus.Running; }
        else return;
        Changed?.Invoke();
    }

    public void Cancel()
    {
        Status = TimerStatus.Idle;
        Remaining = remainingAtStart = TimeSpan.Zero;
        FocusSessions = 0;
        Mode = TimerMode.Custom;
        Changed?.Invoke();
    }
}
