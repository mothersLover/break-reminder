using System;
using System.IO;
using System.Media;
using System.Text;

namespace BreakReminder
{
    static class Sounds
    {
        public static readonly string[] Keys = { "chime", "soft", "bell", "asterisk", "exclamation", "beep", "custom" };
        public static readonly string[] Names =
        {
            "Колокольчики", "Мягкий сигнал", "Гонг",
            "Системный: Asterisk", "Системный: Exclamation", "Системный: Beep",
            "Свой файл (.wav)"
        };

        static SoundPlayer player;

        public static void Play(AppSettings s)
        {
            try
            {
                switch (s.Sound)
                {
                    case "asterisk": SystemSounds.Asterisk.Play(); return;
                    case "exclamation": SystemSounds.Exclamation.Play(); return;
                    case "beep": SystemSounds.Beep.Play(); return;
                    case "custom":
                        if (!string.IsNullOrEmpty(s.CustomSoundPath) && File.Exists(s.CustomSoundPath))
                        {
                            Stop();
                            player = new SoundPlayer(s.CustomSoundPath);
                            player.Play();
                            return;
                        }
                        break; // файла нет — играем встроенную мелодию
                }
                Stop();
                player = new SoundPlayer(new MemoryStream(Synth(s.Sound, s.Volume / 100.0)));
                player.Play();
            }
            catch { }
        }

        public static void Stop()
        {
            try { if (player != null) player.Stop(); } catch { }
        }

        // Простой синтезатор: ноты с затухающими обертонами -> WAV в памяти.
        static byte[] Synth(string key, double volume)
        {
            const int rate = 44100;
            double[][] notes; // { частота, старт (с), длительность (с), громкость }
            double[] partials, partialAmps;
            double decay;

            switch (key)
            {
                case "soft":
                    notes = new[] { new[] { 880.0, 0, 1.3, 1 }, new[] { 659.25, 0.3, 1.6, 0.9 } };
                    partials = new[] { 1.0, 2.0 };
                    partialAmps = new[] { 1.0, 0.12 };
                    decay = 3.0;
                    break;
                case "bell":
                    notes = new[] { new[] { 196.0, 0, 3.2, 1 } };
                    partials = new[] { 1.0, 2.0, 2.76, 5.4, 8.93 };
                    partialAmps = new[] { 1.0, 0.5, 0.6, 0.25, 0.1 };
                    decay = 1.4;
                    break;
                default: // chime
                    notes = new[]
                    {
                        new[] { 1046.5, 0, 1.3, 1 }, new[] { 1318.5, 0.16, 1.3, 0.9 },
                        new[] { 1568.0, 0.32, 1.3, 0.85 }, new[] { 2093.0, 0.48, 1.9, 0.8 }
                    };
                    partials = new[] { 1.0, 2.0, 3.0 };
                    partialAmps = new[] { 1.0, 0.3, 0.08 };
                    decay = 3.5;
                    break;
            }

            double total = 0;
            foreach (var n in notes) total = Math.Max(total, n[1] + n[2]);
            int count = (int)(total * rate);
            var buf = new double[count];

            foreach (var n in notes)
            {
                int s0 = (int)(n[1] * rate);
                int len = (int)(n[2] * rate);
                for (int i = 0; i < len && s0 + i < count; i++)
                {
                    double t = (double)i / rate;
                    double attack = t < 0.004 ? t / 0.004 : 1.0;
                    double release = Math.Min(1.0, (len - i) / (0.05 * rate));
                    double sum = 0;
                    for (int p = 0; p < partials.Length; p++)
                        sum += partialAmps[p] * Math.Sin(2 * Math.PI * n[0] * partials[p] * t) * Math.Exp(-t * decay * (1 + p * 0.6));
                    buf[s0 + i] += n[3] * attack * release * sum;
                }
            }

            double peak = 1e-9;
            foreach (double v in buf) peak = Math.Max(peak, Math.Abs(v));
            double k = 0.9 * Math.Max(0.0, Math.Min(1.0, volume)) / peak;

            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                int dataLen = count * 2;
                w.Write(Encoding.ASCII.GetBytes("RIFF"));
                w.Write(36 + dataLen);
                w.Write(Encoding.ASCII.GetBytes("WAVE"));
                w.Write(Encoding.ASCII.GetBytes("fmt "));
                w.Write(16);
                w.Write((short)1);      // PCM
                w.Write((short)1);      // mono
                w.Write(rate);
                w.Write(rate * 2);
                w.Write((short)2);
                w.Write((short)16);
                w.Write(Encoding.ASCII.GetBytes("data"));
                w.Write(dataLen);
                foreach (double v in buf) w.Write((short)(v * k * short.MaxValue));
                w.Flush();
                return ms.ToArray();
            }
        }
    }
}
