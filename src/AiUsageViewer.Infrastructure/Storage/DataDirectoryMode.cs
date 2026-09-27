namespace AiUsageViewer.Infrastructure.Storage;

public static class DataDirectoryMode
{
    private const string Marker="AIUsageViewer synthetic fixtures v1";
    public static void Ensure(string directory,bool demo)
    {
        var marker=Path.Combine(directory,"synthetic-data.marker");
        var synthetic=File.Exists(marker)&&File.ReadAllText(marker)==Marker;
        if(!demo&&synthetic) throw new InvalidDataException("Synthetic data cannot be opened as a live profile.");
        if(!demo) return;
        if(File.Exists(Path.Combine(directory,"usage.db"))&&!synthetic)
            throw new InvalidDataException("Demo and screenshots require a new empty data directory or a marked synthetic profile.");
        Directory.CreateDirectory(directory);if(!synthetic) File.WriteAllText(marker,Marker);
    }
}
