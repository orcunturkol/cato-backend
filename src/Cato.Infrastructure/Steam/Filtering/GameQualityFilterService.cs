using System.Text.Json;
using Cato.Domain.Entities;
using Microsoft.Extensions.Options;

namespace Cato.Infrastructure.Steam.Filtering;

public class GameQualityFilterService : IGameQualityFilter
{
    private readonly IOptionsMonitor<GameFilterOptions> _options;

    public GameQualityFilterService(IOptionsMonitor<GameFilterOptions> options)
    {
        _options = options;
    }

    public bool ShouldRejectByName(string name)
    {
        var opts = _options.CurrentValue;
        if (!opts.Enabled) return false;
        if (!opts.Name.RejectIfNoLatinLetter) return false;
        return !ScriptHeuristics.HasLatinLetter(name);
    }

    public FilterDecision Evaluate(Game game, FilterStage stage)
    {
        var opts = _options.CurrentValue;

        // Enabled flag only gates live ingestion; Backfill is already an explicit admin opt-in.
        if (!opts.Enabled && stage != FilterStage.Backfill)
            return FilterDecision.Accept();

        if (opts.Allowlist.AppIds.Contains(game.AppId))
            return FilterDecision.Accept();

        if (stage == FilterStage.PreEnrichment)
        {
            if (opts.Name.RejectIfNoLatinLetter && !ScriptHeuristics.HasLatinLetter(game.Name))
                return FilterDecision.Reject("name:no-latin-letter");
            return FilterDecision.Accept();
        }

        // PostEnrichment and Backfill apply the full rule set

        if (opts.Name.RejectIfNoLatinLetter && !ScriptHeuristics.HasLatinLetter(game.Name))
            return FilterDecision.Reject("name:no-latin-letter");

        if (opts.Language.LatinScriptLanguages.Count > 0 && !string.IsNullOrWhiteSpace(game.SupportedLanguages))
        {
            var languages = ScriptHeuristics.ParseSupportedLanguages(game.SupportedLanguages);
            if (languages.Count > 0 &&
                !ScriptHeuristics.HasAnyLatinScriptLanguage(languages, opts.Language.LatinScriptLanguages))
            {
                return FilterDecision.Reject("language:no-latin-script");
            }
        }

        if (opts.AdultContent.BlockedDescriptorIds.Count > 0 && game.ContentDescriptorIds is not null)
        {
            var matchedId = FindBlockedDescriptor(game.ContentDescriptorIds, opts.AdultContent.BlockedDescriptorIds);
            if (matchedId is not null)
                return FilterDecision.Reject($"content:descriptor-{matchedId}");
        }

        if (opts.AdultContent.BlockedTags.Count > 0 && game.Tags.Count > 0)
        {
            var blockedTagSet = new HashSet<string>(opts.AdultContent.BlockedTags, StringComparer.OrdinalIgnoreCase);
            foreach (var tag in game.Tags)
            {
                if (tag.TagName is not null && blockedTagSet.Contains(tag.TagName))
                    return FilterDecision.Reject($"tag:{tag.TagName.ToLowerInvariant()}");
            }
        }

        if (opts.Blocklists.Developers.Count > 0 && game.Developer is not null)
        {
            if (opts.Blocklists.Developers.Any(d => string.Equals(d, game.Developer.Name, StringComparison.OrdinalIgnoreCase)))
                return FilterDecision.Reject("developer:blocklist");
        }

        if (opts.Blocklists.Publishers.Count > 0 && game.Publisher is not null)
        {
            if (opts.Blocklists.Publishers.Any(p => string.Equals(p, game.Publisher.Name, StringComparison.OrdinalIgnoreCase)))
                return FilterDecision.Reject("publisher:blocklist");
        }

        return FilterDecision.Accept();
    }

    private static int? FindBlockedDescriptor(JsonDocument doc, IReadOnlyCollection<int> blocked)
    {
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;
        foreach (var element in doc.RootElement.EnumerateArray())
        {
            if (element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var id) && blocked.Contains(id))
                return id;
        }
        return null;
    }
}
