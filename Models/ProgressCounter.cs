namespace TreeSizeViewNG.Models;

/// <summary>
/// Fortschritt einer Hintergrundarbeit (Kopieren, Sortieren …). Der Hintergrund-Thread zählt hoch,
/// die Oberfläche liest den Stand regelmäßig ab. Einzelne Zähler werden atomar gelesen und geschrieben.
/// </summary>
public sealed class ProgressCounter
{
    private long _done;
    private long _total = 1;
    private volatile string _phase = string.Empty;

    public string Phase => _phase;
    public long Done => Interlocked.Read(ref _done);
    public long Total => Interlocked.Read(ref _total);

    /// <summary>Anteil 0 … 1.</summary>
    public double Fraction => Math.Clamp((double)Done / Math.Max(1, Total), 0, 1);

    public void Start(string phase, long total)
    {
        Interlocked.Exchange(ref _total, Math.Max(1, total));
        Interlocked.Exchange(ref _done, 0);
        _phase = phase;
    }

    public void Step(long count = 1) => Interlocked.Add(ref _done, count);
}
