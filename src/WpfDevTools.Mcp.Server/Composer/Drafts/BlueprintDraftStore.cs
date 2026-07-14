using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using WpfDevTools.Mcp.Server.Composer.Blueprints;
using WpfDevTools.Shared.Validation;

namespace WpfDevTools.Mcp.Server.Composer.Drafts;

internal sealed class BlueprintDraftStore
{
    public const string ReferencePrefix = "wpfdevtools-blueprint-draft:";
    public const int DefaultMaxDrafts = 32;
    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromMinutes(30);

    private readonly object _gate = new();
    private readonly Dictionary<string, DraftEntry> _entries = new(StringComparer.Ordinal);
    private readonly Queue<string> _insertionOrder = new();
    private readonly int _maxDrafts;
    private readonly int _maxCharacters;
    private readonly TimeSpan _lifetime;
    private readonly Func<DateTimeOffset> _utcNow;

    public BlueprintDraftStore(
        int maxDrafts = DefaultMaxDrafts,
        int maxCharacters = BoundaryStringLimits.MaxStringifiedJsonArgumentLength,
        TimeSpan? lifetime = null,
        Func<DateTimeOffset>? utcNow = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxDrafts, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxCharacters, 2);
        _maxDrafts = maxDrafts;
        _maxCharacters = maxCharacters;
        _lifetime = lifetime ?? DefaultLifetime;
        if (_lifetime <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(lifetime));
        }

        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                PurgeExpired(_utcNow());
                return _entries.Count;
            }
        }
    }

    public BlueprintDraftMutationResult Create(string blueprintJson)
    {
        if (blueprintJson.Length > _maxCharacters)
        {
            return BlueprintDraftMutationResult.Invalid(Issue(
                "BlueprintDraftTooLarge",
                $"Blueprint draft has {blueprintJson.Length} characters; the maximum is {_maxCharacters}.",
                "Reduce the blueprint or use a smaller draft before retrying."));
        }

        try
        {
            if (JsonNode.Parse(blueprintJson) is not JsonObject)
            {
                return BlueprintDraftMutationResult.Invalid(InvalidJsonIssue(
                    "Blueprint draft must be a JSON object."));
            }
        }
        catch (JsonException ex)
        {
            return BlueprintDraftMutationResult.Invalid(InvalidJsonIssue(ex.Message));
        }

        lock (_gate)
        {
            var now = _utcNow();
            PurgeExpired(now);
            while (_entries.Count >= _maxDrafts)
            {
                RemoveOldest();
            }

            string draftRef;
            do
            {
                draftRef = ReferencePrefix + CreateToken();
            }
            while (_entries.ContainsKey(draftRef));

            var expiresAt = now.Add(_lifetime);
            _entries[draftRef] = new DraftEntry(blueprintJson, expiresAt);
            _insertionOrder.Enqueue(draftRef);
            return new BlueprintDraftMutationResult(
                true,
                draftRef,
                blueprintJson.Length,
                expiresAt,
                null);
        }
    }

    public BlueprintDraftResolution Resolve(string draftRef)
    {
        lock (_gate)
        {
            var now = _utcNow();
            PurgeExpired(now);
            if (!_entries.TryGetValue(draftRef, out var entry) || entry.ExpiresAt <= now)
            {
                _entries.Remove(draftRef);
                return BlueprintDraftResolution.Invalid(draftRef, NotFoundIssue());
            }

            return new BlueprintDraftResolution(
                true,
                draftRef,
                entry.BlueprintJson,
                entry.BlueprintJson.Length,
                entry.ExpiresAt,
                null);
        }
    }

    public BlueprintDraftMutationResult ApplyMergePatch(string draftRef, string patchJson)
    {
        var source = Resolve(draftRef);
        if (!source.Success)
        {
            return BlueprintDraftMutationResult.Invalid(source.Error!);
        }

        try
        {
            var target = JsonNode.Parse(source.BlueprintJson) as JsonObject;
            var patch = JsonNode.Parse(patchJson) as JsonObject;
            if (target is null || patch is null)
            {
                return BlueprintDraftMutationResult.Invalid(InvalidJsonIssue(
                    "Blueprint and merge patch roots must both be JSON objects."));
            }

            var before = target.DeepClone();
            ApplyMergePatch(target, patch);
            return CreateDerived(target, before);
        }
        catch (JsonException ex)
        {
            return BlueprintDraftMutationResult.Invalid(InvalidJsonIssue(ex.Message));
        }
    }

    public BlueprintDraftMutationResult ApplyPathUpdate(
        string draftRef,
        string jsonPath,
        JsonElement? value,
        bool remove)
    {
        var source = Resolve(draftRef);
        if (!source.Success)
        {
            return BlueprintDraftMutationResult.Invalid(source.Error!);
        }

        try
        {
            var target = JsonNode.Parse(source.BlueprintJson) as JsonObject;
            if (target is null)
            {
                return BlueprintDraftMutationResult.Invalid(InvalidJsonIssue(
                    "Blueprint draft root must be a JSON object."));
            }

            var resolution = BlueprintNodePathResolver.Resolve(target, jsonPath);
            if (!resolution.Success)
            {
                return BlueprintDraftMutationResult.Invalid(Issue(
                    resolution.Code switch
                    {
                        "ElementAliasNotFound" => "BlueprintDraftElementNotFound",
                        "ElementAliasAmbiguous" => "BlueprintDraftElementAmbiguous",
                        _ => "InvalidBlueprintDraftPath"
                    },
                    resolution.Message!,
                    resolution.RepairSuggestion!));
            }

            jsonPath = resolution.JsonPath;

            var issue = BlueprintDraftPathMutation.Apply(
                target,
                jsonPath,
                value,
                remove,
                out var previousValue,
                out var previousValueExists);
            if (issue is not null)
            {
                return BlueprintDraftMutationResult.Invalid(issue);
            }

            var nextValue = remove ? null : JsonNode.Parse(value!.Value.GetRawText());
            var summary = BlueprintDraftChangeSummaryBuilder.BuildSingle(
                previousValue,
                nextValue,
                jsonPath,
                previousValueExists,
                afterExists: !remove);
            return CreateDerived(target, summary);
        }
        catch (JsonException ex)
        {
            return BlueprintDraftMutationResult.Invalid(InvalidJsonIssue(ex.Message));
        }
    }

    private BlueprintDraftMutationResult CreateDerived(JsonObject target, JsonNode before)
    {
        var derived = Create(target.ToJsonString());
        return derived.Success
            ? derived with { ChangeSummary = BlueprintDraftChangeSummaryBuilder.Build(before, target) }
            : derived;
    }

    private BlueprintDraftMutationResult CreateDerived(
        JsonObject target,
        BlueprintDraftChangeSummary changeSummary)
    {
        var derived = Create(target.ToJsonString());
        return derived.Success
            ? derived with { ChangeSummary = changeSummary }
            : derived;
    }

    private static void ApplyMergePatch(JsonObject target, JsonObject patch)
    {
        foreach (var (name, patchValue) in patch)
        {
            if (patchValue is null)
            {
                target.Remove(name);
                continue;
            }

            if (patchValue is JsonObject patchObject)
            {
                var targetObject = target[name] as JsonObject ?? new JsonObject();
                target[name] = targetObject;
                ApplyMergePatch(targetObject, patchObject);
                continue;
            }

            target[name] = patchValue.DeepClone();
        }
    }

    private void PurgeExpired(DateTimeOffset now)
    {
        while (_insertionOrder.TryPeek(out var draftRef))
        {
            if (!_entries.TryGetValue(draftRef, out var entry))
            {
                _insertionOrder.Dequeue();
                continue;
            }

            if (entry.ExpiresAt > now)
            {
                break;
            }

            _insertionOrder.Dequeue();
            _entries.Remove(draftRef);
        }
    }

    private void RemoveOldest()
    {
        while (_insertionOrder.TryDequeue(out var draftRef))
        {
            if (_entries.Remove(draftRef))
            {
                return;
            }
        }
    }

    private static string CreateToken()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static BlueprintDraftIssue InvalidJsonIssue(string detail)
        => Issue(
            "InvalidBlueprintDraftJson",
            $"Blueprint draft JSON is invalid: {detail}",
            "Pass one valid UI blueprint JSON object or JSON Merge Patch object.");

    private static BlueprintDraftIssue NotFoundIssue()
        => Issue(
            "BlueprintDraftNotFound",
            "The blueprint draft reference is missing, expired, or was evicted by the bounded process-local store.",
            "Call create_ui_blueprint_draft again and use the returned draftRef before it expires.");

    private static BlueprintDraftIssue Issue(string code, string message, string repair)
        => new(code, message, repair);

    private sealed record DraftEntry(string BlueprintJson, DateTimeOffset ExpiresAt);
}
