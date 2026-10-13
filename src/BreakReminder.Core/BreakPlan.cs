namespace BreakReminder.Core;

/// <summary>Шаг перерыва: что делать и сколько.</summary>
public sealed record Exercise(string Title, string Text, int Seconds);

/// <summary>Шаг в расписании конкретного перерыва.</summary>
public sealed record PlannedStep(Exercise Exercise, TimeSpan Start, TimeSpan Length)
{
    public TimeSpan End => Start + Length;
}

/// <summary>План перерыва: набор коротких упражнений на всю длительность перерыва.</summary>
public static class BreakPlan
{
    public static readonly Exercise[] Short =
    {
        new("Глаза", "Посмотри в окно или на дальнюю стену. Медленно переводи взгляд: вдаль — на кончик носа — снова вдаль.", 40),
        new("Моргание", "Быстро поморгай 20 раз, затем закрой глаза и посиди так 10 секунд.", 30),
        new("Шея", "Медленно наклони голову к правому плечу, задержись на 5 секунд, затем к левому. Повтори 3 раза.", 45),
        new("Плечи", "Подними плечи к ушам, задержи на 3 секунды и опусти. 8 раз. Затем 5 кругов плечами назад.", 40),
        new("Спина", "Встань. Сцепи руки в замок над головой и потянись вверх, затем плавно наклонись вправо и влево.", 45),
        new("Запястья", "Вытяни руку ладонью вперёд и другой рукой мягко потяни пальцы на себя. По 15 секунд на каждую руку.", 40),
        new("Ноги", "10 приседаний или 20 подъёмов на носки — разгони кровь.", 45),
        new("Дыхание", "Медленный вдох на 4 счёта, задержка на 4, выдох на 6. Пять раз.", 50),
        new("Вода", "Выпей стакан воды.", 30),
    };

    public static readonly Exercise Walk = new("Пройдись", "Отойди от компьютера: пройдись по комнате, до кухни или по коридору.", 60);
    public static readonly Exercise Outside = new("На улицу", "Выйди на улицу или хотя бы к открытому окну. Подальше от экрана и телефона.", 300);
    public static readonly Exercise Rest = new("Отдыхай", "Просто отдохни без экрана: посмотри в окно, разомнись, поболтай с кем-нибудь.", 60);

    /// <summary>Разложить перерыв на шаги. Последний шаг растягивается до конца перерыва.</summary>
    public static List<PlannedStep> Build(TimeSpan duration, bool isLong, Random rnd)
    {
        var pool = new List<Exercise>();
        if (isLong) pool.Add(Outside);
        pool.AddRange(Short.OrderBy(_ => rnd.Next()));
        if (!isLong) pool.Add(Walk);

        var steps = new List<PlannedStep>();
        var at = TimeSpan.Zero;
        foreach (var ex in pool)
        {
            if (at >= duration) break;
            var len = TimeSpan.FromSeconds(ex.Seconds);
            if (duration - at < TimeSpan.FromSeconds(20) && steps.Count > 0) break; // хвост < 20 с — допишем к последнему
            if (at + len > duration) len = duration - at;
            steps.Add(new PlannedStep(ex, at, len));
            at += len;
        }
        if (steps.Count == 0) steps.Add(new PlannedStep(Rest, TimeSpan.Zero, duration));
        else if (at < duration)
        {
            // упражнения кончились, а перерыв нет — остаток просто отдыхаем (или растягиваем короткий хвост)
            var rest = duration - at;
            if (rest < TimeSpan.FromSeconds(20))
            {
                var last = steps[^1];
                steps[^1] = last with { Length = last.Length + rest };
            }
            else steps.Add(new PlannedStep(Rest, at, rest));
        }
        return steps;
    }

    /// <summary>Какой шаг идёт в момент elapsed от начала перерыва.</summary>
    public static PlannedStep Current(IReadOnlyList<PlannedStep> plan, TimeSpan elapsed) =>
        plan.FirstOrDefault(s => elapsed < s.End) ?? plan[^1];
}
