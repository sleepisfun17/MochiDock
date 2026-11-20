namespace MochiDock.Services;
public class RubyPiece
{
    public string Text { get; set; } = "";
    public string Furigana { get; set; } = "";
    public string Romaji { get; set; } = "";
    public string BaseText { get => Text; set => Text = value; }
    public string RubyText { get => Furigana; set => Furigana = value; }
    public bool HasReading => !string.IsNullOrEmpty(Romaji) || !string.IsNullOrEmpty(Furigana);
    public bool HasRuby => !string.IsNullOrEmpty(Romaji);
}
