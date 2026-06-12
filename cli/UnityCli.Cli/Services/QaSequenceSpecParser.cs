#nullable enable
using System.Globalization;
using System.Text.Json;
using UnityCli.Cli.Models;
using UnityCli.Protocol;

namespace UnityCli.Cli.Services;

public static class QaSequenceSpecParser
{
    private static readonly string[] ConditionKinds =
    [
        "active",
        "gone",
        "transform",
        "scene",
        "log",
        "interactable",
        "query",
    ];

    private static readonly string[] ActionKinds =
    [
        "key",
        "tap",
        "swipe",
        "wait",
        "screenshot",
    ];

    private static readonly HashSet<string> ValidOps = new(StringComparer.Ordinal)
    {
        "==",
        "!=",
        ">=",
        "<=",
        "near",
        "changed",
    };

    public static QaRunSequenceArgs Parse(string specJson)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(specJson);
        }
        catch (JsonException exception)
        {
            throw new CliUsageException($"--spec-json 파싱 실패: {exception.Message}");
        }

        using (doc)
        {
            JsonElement root = doc.RootElement;
            if (!root.TryGetProperty("steps", out JsonElement stepsEl)
                || stepsEl.ValueKind != JsonValueKind.Array
                || stepsEl.GetArrayLength() == 0)
            {
                throw new CliUsageException("--spec-json: 비어 있지 않은 `steps` 배열이 필요합니다.");
            }

            var steps = new List<QaSequenceStep>();
            foreach (JsonElement stepEl in stepsEl.EnumerateArray())
            {
                steps.Add(ParseStep(stepEl));
            }

            int timeoutMs = TryGetInt32(root, "timeoutMs", out int tv) ? tv : 0;
            return new QaRunSequenceArgs { steps = steps.ToArray(), timeoutMs = timeoutMs };
        }
    }

    private static QaSequenceStep ParseStep(JsonElement stepEl)
    {
        var wait = new List<QaSequenceCondition>();
        if (stepEl.TryGetProperty("wait", out JsonElement waitEl))
        {
            if (waitEl.ValueKind != JsonValueKind.Array)
            {
                throw new CliUsageException("--spec-json: step.wait는 배열이어야 합니다.");
            }

            foreach (JsonElement conditionEl in waitEl.EnumerateArray())
            {
                wait.Add(ParseCondition(conditionEl));
            }
        }

        var actions = new List<QaSequenceAction>();
        if (stepEl.TryGetProperty("actions", out JsonElement actionsEl))
        {
            if (actionsEl.ValueKind != JsonValueKind.Array)
            {
                throw new CliUsageException("--spec-json: step.actions는 배열이어야 합니다.");
            }

            foreach (JsonElement actionEl in actionsEl.EnumerateArray())
            {
                actions.Add(ParseAction(actionEl));
            }
        }

        return new QaSequenceStep
        {
            name = TryGetString(stepEl, "name") ?? string.Empty,
            wait = wait.ToArray(),
            actions = actions.ToArray(),
            timeoutMs = TryGetInt32(stepEl, "timeoutMs", out int timeoutMs) ? timeoutMs : 0,
        };
    }

    private static QaSequenceCondition ParseCondition(JsonElement conditionEl)
    {
        string kind = GetSinglePresentKey(conditionEl, ConditionKinds, "condition");
        var condition = new QaSequenceCondition
        {
            kind = kind,
            target = TryGetString(conditionEl, "target") ?? string.Empty,
        };

        switch (kind)
        {
            case "active":
            case "gone":
            case "interactable":
                EnsureTarget(condition, kind);
                condition.value = NormalizeValue(conditionEl.GetProperty(kind));
                break;
            case "scene":
            case "log":
                condition.value = NormalizeValue(conditionEl.GetProperty(kind));
                break;
            case "transform":
                EnsureTarget(condition, kind);
                condition.key = RequireString(conditionEl.GetProperty(kind), kind);
                condition.op = RequireValidOp(conditionEl);
                condition.value = condition.op == "changed" && !conditionEl.TryGetProperty("value", out _)
                    ? string.Empty
                    : NormalizeRequiredValue(conditionEl, kind);
                condition.epsilon = TryGetSingle(conditionEl, "epsilon", out float epsilon) ? epsilon : 0f;
                break;
            case "query":
                EnsureTarget(condition, kind);
                condition.key = RequireString(conditionEl.GetProperty(kind), kind);
                condition.op = RequireValidOp(conditionEl);
                condition.value = condition.op == "changed" && !conditionEl.TryGetProperty("value", out _)
                    ? string.Empty
                    : NormalizeRequiredValue(conditionEl, kind);
                condition.epsilon = TryGetSingle(conditionEl, "epsilon", out float queryEpsilon) ? queryEpsilon : 0f;
                break;
        }

        return condition;
    }

    private static QaSequenceAction ParseAction(JsonElement actionEl)
    {
        string kind = GetSinglePresentKey(actionEl, ActionKinds, "action");
        var action = new QaSequenceAction { kind = kind };

        switch (kind)
        {
            case "key":
                action.key = RequireString(actionEl.GetProperty("key"), "key");
                break;
            case "tap":
                ParseTapAction(actionEl.GetProperty("tap"), action);
                break;
            case "swipe":
                ParseSwipeAction(actionEl.GetProperty("swipe"), action);
                break;
            case "wait":
                action.waitMs = RequireInt32(actionEl.GetProperty("wait"), "wait");
                break;
            case "screenshot":
                break;
        }

        return action;
    }

    private static void ParseTapAction(JsonElement tapEl, QaSequenceAction action)
    {
        if (tapEl.ValueKind != JsonValueKind.Object)
        {
            throw new CliUsageException("--spec-json: tap action은 {x,y} 또는 {target} 객체여야 합니다.");
        }

        bool hasX = TryGetInt32(tapEl, "x", out int x);
        bool hasY = TryGetInt32(tapEl, "y", out int y);
        string? target = TryGetString(tapEl, "target");
        if (hasX || hasY)
        {
            if (!hasX || !hasY || !string.IsNullOrEmpty(target))
            {
                throw new CliUsageException("--spec-json: tap action은 x/y 좌표 쌍 또는 target 중 하나만 필요합니다.");
            }

            action.hasTapCoords = true;
            action.x = x;
            action.y = y;
            return;
        }

        if (string.IsNullOrWhiteSpace(target))
        {
            throw new CliUsageException("--spec-json: tap action에는 x/y 또는 target이 필요합니다.");
        }

        action.target = target;
    }

    private static void ParseSwipeAction(JsonElement swipeEl, QaSequenceAction action)
    {
        if (swipeEl.ValueKind != JsonValueKind.Object)
        {
            throw new CliUsageException("--spec-json: swipe action은 객체여야 합니다.");
        }

        if (swipeEl.TryGetProperty("from", out JsonElement fromEl)
            && swipeEl.TryGetProperty("to", out JsonElement toEl))
        {
            (action.fromX, action.fromY) = ParsePoint(fromEl, "swipe.from");
            (action.toX, action.toY) = ParsePoint(toEl, "swipe.to");
        }
        else
        {
            action.fromX = RequireObjectInt32(swipeEl, "fromX", "swipe");
            action.fromY = RequireObjectInt32(swipeEl, "fromY", "swipe");
            action.toX = RequireObjectInt32(swipeEl, "toX", "swipe");
            action.toY = RequireObjectInt32(swipeEl, "toY", "swipe");
        }

        action.durationMs = TryGetInt32(swipeEl, "durationMs", out int durationMs) ? durationMs : 0;
    }

    private static (int X, int Y) ParsePoint(JsonElement pointEl, string label)
    {
        if (pointEl.ValueKind == JsonValueKind.Array && pointEl.GetArrayLength() == 2)
        {
            JsonElement[] values = pointEl.EnumerateArray().ToArray();
            return (RequireInt32(values[0], label), RequireInt32(values[1], label));
        }

        if (pointEl.ValueKind == JsonValueKind.String)
        {
            string[] parts = (pointEl.GetString() ?? string.Empty).Split(',');
            if (parts.Length == 2
                && int.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int x)
                && int.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int y))
            {
                return (x, y);
            }
        }

        throw new CliUsageException($"--spec-json: {label} 값은 [x,y] 또는 \"x,y\" 형식이어야 합니다.");
    }

    private static string GetSinglePresentKey(JsonElement element, string[] keys, string label)
    {
        string? present = null;
        foreach (string key in keys)
        {
            if (!element.TryGetProperty(key, out _))
            {
                continue;
            }

            if (present != null)
            {
                throw new CliUsageException($"--spec-json: {label}에는 kind 키를 하나만 지정할 수 있습니다.");
            }

            present = key;
        }

        return present ?? throw new CliUsageException($"--spec-json: {label} kind를 찾지 못했습니다.");
    }

    private static string RequireValidOp(JsonElement element)
    {
        string op = TryGetString(element, "op") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(op) || !ValidOps.Contains(op))
        {
            throw new CliUsageException("--spec-json: op는 ==, !=, >=, <=, near, changed 중 하나여야 합니다.");
        }

        return op;
    }

    private static string NormalizeRequiredValue(JsonElement element, string label)
    {
        if (!element.TryGetProperty("value", out JsonElement valueEl))
        {
            throw new CliUsageException($"--spec-json: {label} condition에는 value가 필요합니다.");
        }

        return NormalizeValue(valueEl);
    }

    private static string NormalizeValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Number => NormalizeNumber(element),
            JsonValueKind.Array => string.Join(",", element.EnumerateArray().Select(NormalizeValue)),
            JsonValueKind.String => element.GetString() ?? string.Empty,
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => throw new CliUsageException("--spec-json: value는 number, number array, string, bool 중 하나여야 합니다."),
        };
    }

    private static string NormalizeNumber(JsonElement element)
    {
        string raw = element.GetRawText();
        if (decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal decimalValue))
        {
            return decimalValue.ToString("G29", CultureInfo.InvariantCulture);
        }

        return element.GetDouble().ToString("G17", CultureInfo.InvariantCulture);
    }

    private static void EnsureTarget(QaSequenceCondition condition, string kind)
    {
        if (string.IsNullOrWhiteSpace(condition.target))
        {
            throw new CliUsageException($"--spec-json: {kind} condition에는 target이 필요합니다.");
        }
    }

    private static string RequireString(JsonElement element, string label)
    {
        if (element.ValueKind != JsonValueKind.String)
        {
            throw new CliUsageException($"--spec-json: {label} 값은 문자열이어야 합니다.");
        }

        string? value = element.GetString();
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new CliUsageException($"--spec-json: {label} 값이 비어 있습니다.");
        }

        return value;
    }

    private static int RequireObjectInt32(JsonElement element, string propertyName, string label)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement valueEl))
        {
            throw new CliUsageException($"--spec-json: {label}.{propertyName} 값이 필요합니다.");
        }

        return RequireInt32(valueEl, $"{label}.{propertyName}");
    }

    private static int RequireInt32(JsonElement element, string label)
    {
        if (!element.TryGetInt32(out int value))
        {
            throw new CliUsageException($"--spec-json: {label} 값은 정수여야 합니다.");
        }

        return value;
    }

    private static bool TryGetInt32(JsonElement element, string propertyName, out int value)
    {
        value = 0;
        return element.TryGetProperty(propertyName, out JsonElement valueEl) && valueEl.TryGetInt32(out value);
    }

    private static bool TryGetSingle(JsonElement element, string propertyName, out float value)
    {
        value = 0f;
        return element.TryGetProperty(propertyName, out JsonElement valueEl) && valueEl.TryGetSingle(out value);
    }

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement valueEl)
            || valueEl.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return valueEl.GetString();
    }
}
