namespace Cato.Infrastructure.Steam.Filtering;

public class GameFilterOptions
{
    public const string SectionName = "GameFilter";

    public bool Enabled { get; set; } = false;

    public NameOptions Name { get; set; } = new();
    public LanguageOptions Language { get; set; } = new();
    public AdultContentOptions AdultContent { get; set; } = new();
    public BlocklistOptions Blocklists { get; set; } = new();
    public AllowlistOptions Allowlist { get; set; } = new();

    public class NameOptions
    {
        public bool RejectIfNoLatinLetter { get; set; } = true;
    }

    public class LanguageOptions
    {
        public List<string> LatinScriptLanguages { get; set; } = new();
    }

    public class AdultContentOptions
    {
        public List<int> BlockedDescriptorIds { get; set; } = new();
        public List<string> BlockedTags { get; set; } = new();
    }

    public class BlocklistOptions
    {
        public List<string> Developers { get; set; } = new();
        public List<string> Publishers { get; set; } = new();
    }

    public class AllowlistOptions
    {
        public List<int> AppIds { get; set; } = new();
    }
}
