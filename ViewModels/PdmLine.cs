using System.ComponentModel;

namespace TreeSizeViewNG.ViewModels;

/// <summary>Fortschrittszeile für die Abfrage eines PDM-Tresors.</summary>
public sealed class PdmLine(string title) : INotifyPropertyChanged
{
    private string _text = string.Empty;

    public string Title { get; } = title;

    /// <summary>Bisher gelesene Dateien; wird vom Abfrage-Thread gesetzt, die Oberfläche liest zyklisch.</summary>
    public volatile int Count;

    /// <summary>Abfrage fertig (danach wird nur noch eingetragen).</summary>
    public bool IsDone { get; set; }

    public string Text
    {
        get => _text;
        set
        {
            if (_text == value)
                return;
            _text = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Text)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
