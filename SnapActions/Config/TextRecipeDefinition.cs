namespace SnapActions.Config;

public sealed class TextRecipeDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public List<string> Steps { get; set; } = [];
    public bool Enabled { get; set; } = true;
}
