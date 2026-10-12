namespace BreakReminder.Core;

/// <summary>То, что движок спрашивает у системы. Реализации — в BreakReminder.Platform (по ОС).</summary>
public interface IEnvironmentProbe
{
    /// <summary>Сколько нет ввода с клавиатуры/мыши. TimeSpan.Zero, если ОС не даёт узнать.</summary>
    TimeSpan IdleTime();

    /// <summary>Похоже на созвон: занят микрофон (или камера).</summary>
    bool IsCallActive();

    /// <summary>На переднем плане полноэкранная программа.</summary>
    bool IsFullscreenAppActive();

    /// <summary>Экран заблокирован.</summary>
    bool IsScreenLocked();
}

/// <summary>Все системные возможности, нужные приложению.</summary>
public interface IPlatform : IEnvironmentProbe, IDisposable
{
    string Name { get; }

    /// <summary>Вызывается один раз, когда UI-поток готов (Windows создаёт скрытое окно для сообщений).</summary>
    void Initialize();

    // ---- автозапуск ----
    bool IsAutostartEnabled();
    void SetAutostart(bool enabled);

    // ---- звук ----
    void PlayWav(byte[] wav);
    void PlayFile(string path);
    void PlaySystemSound();
    void StopSound();

    // ---- горячая клавиша паузы ----
    /// <summary>Как называется комбинация на этой ОС, или null — глобальные клавиши не поддерживаются.</summary>
    string? PauseHotkeyName { get; }
    bool RegisterPauseHotkey(Action onPressed);
    void UnregisterPauseHotkey();

    // ---- что поддерживается (для подсказок в настройках) ----
    bool SupportsFullscreenDetection { get; }

    // ---- окна ----
    /// <summary>Мигание кнопки окна на панели задач (где есть).</summary>
    void FlashWindow(IntPtr handle, bool on);
    /// <summary>Скруглённые углы у окна без рамки (где есть).</summary>
    void RoundCorners(IntPtr handle);

    /// <summary>Открыть файл или папку в системной программе.</summary>
    void OpenPath(string path);
}
