#:property PublishAot=false
// Claude Code PreToolUse hook: enforces the TDD phases (docs/workflow.md).
//   spec phase:      tests may change, src/ is locked (write the failing test first)
//   implement phase: src/ is open, existing tests are locked (no weakening the spec)
// Always: the paths in .claude/human-only (the harness, build and CI files) are human-only.
// Exit code 2 blocks the tool call and shows the message to the agent.
// This is a quick early warning, not a security boundary: verify.cs checks the diff at stop, and human review
// of tests and CI are the real gates.
using System.Text.Json;
using System.Text.RegularExpressions;

var root = Environment.GetEnvironmentVariable("CLAUDE_PROJECT_DIR") ?? Directory.GetCurrentDirectory();
using var input = JsonDocument.Parse(Console.In.ReadToEnd());
var tool = input.RootElement.GetProperty("tool_name").GetString() ?? string.Empty;
var toolInput = input.RootElement.GetProperty("tool_input");
var phaseFile = Path.Combine(root, ".claude", "state", "phase");
// Same rule in guard.cs, verify.cs, session-start.cs and tools/dev.cs (file-based apps cannot share code):
// a missing, empty or unknown phase file means spec, the safe default.
var phase = File.Exists(phaseFile) && File.ReadAllText(phaseFile).Trim() == "implement" ? "implement" : "spec";
// Same list in verify.cs. Without the file, .claude/ itself stays protected.
var listFile = Path.Combine(root, ".claude", "human-only");
var humanOnly = File.Exists(listFile)
    ? File.ReadAllLines(listFile).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#')).ToList()
    : [".claude/"];

if (tool == "Bash")
{
    var command = toolInput.TryGetProperty("command", out var c) ? c.GetString() ?? string.Empty : string.Empty;
    // Not writes: text in quotes (grep "List<Unit>" src/), redirects to /dev/null, between file descriptors (2>&1),
    // and > or tee into /tmp/. Quotes count when a nested shell (bash -c "...", eval, source) may run them, and a
    // /tmp/ target ends at the first character that is not a plain path character (; & | < > etc.).
    var nestedShell = Regex.IsMatch(command, @"\b(ba|z|da|k)?sh\b|\beval\b|\bsource\b");
    var unquoted = nestedShell ? command : Regex.Replace(command, "'[^']*'|\"[^\"]*\"", "''");
    const string tmpTarget = @"/tmp/(?![^\s;&|<>()'""`$]*\.\.)[^\s;&|<>()'""`$]*";
    var withoutHarmlessRedirects = Regex.Replace(unquoted, $@"&?\d*>>?\s*/dev/null|\d*>&\d+|\d*>>?\s*{tmpTarget}|\btee\s+(-a\s+)?{tmpTarget}", " ");
    var writes = Regex.IsMatch(withoutHarmlessRedirects, @"(>|\bsed\s+-i|\btee\b|\bmv\b|\brm\b|\bcp\b|\bperl\s+-[a-z]*i)");
    // The relaxations above do not apply to commands that mention .claude/: .claude/state/ (phase, base) is
    // gitignored, so verify.cs cannot see a write there at stop, and the guard is the only check.
    var strictWrites = Regex.IsMatch(Regex.Replace(command, @"&?\d*>>?\s*/dev/null|\d*>&\d+", " "),
        @"(>|\bsed\s+-i|\btee\b|\bmv\b|\brm\b|\bcp\b|\bperl\s+-[a-z]*i)");
    if (Regex.IsMatch(command, @"\bphase\s+(spec|implement)\b") ||
        (strictWrites && command.Contains(".claude/", StringComparison.OrdinalIgnoreCase)) ||
        (writes && humanOnly.Any(h => command.Contains(h, StringComparison.OrdinalIgnoreCase))))
    {
        return Block("The AI harness, the paths in .claude/human-only and the TDD phase are changed only by a " +
                     "human (./dev phase ...). Propose the change instead.");
    }

    if (writes && phase == "spec" && Regex.IsMatch(command, @"\bsrc/", RegexOptions.IgnoreCase))
    {
        return Block("Shell commands that modify src/ are blocked in the spec phase. Write the failing test first; " +
                     "src/ opens when a human has reviewed the tests and run ./dev phase implement.");
    }

    if (writes && phase == "implement" && Regex.IsMatch(command, @"\btests/", RegexOptions.IgnoreCase))
    {
        return Block("Shell commands that modify tests/ are blocked in the implement phase. Existing tests are the agreed specification.");
    }

    return 0;
}

var path = toolInput.TryGetProperty("file_path", out var p) ? p.GetString() ?? string.Empty
         : toolInput.TryGetProperty("notebook_path", out var n) ? n.GetString() ?? string.Empty : string.Empty;
if (path.Length == 0)
{
    return 0;
}

var full = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(root, path));
var relative = Path.GetRelativePath(root, full).Replace('\\', '/');

if (humanOnly.Any(h => relative.StartsWith(h, StringComparison.OrdinalIgnoreCase)))
{
    return Block($"'{relative}' is human-only (.claude/human-only). Propose the change in the PR description instead.");
}

if (phase == "spec" && relative.StartsWith("src/", StringComparison.OrdinalIgnoreCase))
{
    return Block("TDD phase is 'spec': write or adjust the failing test first. src/ is locked until a human " +
                 "has reviewed the tests and run ./dev phase implement.");
}

if (phase == "implement" && relative.StartsWith("tests/", StringComparison.OrdinalIgnoreCase) && File.Exists(full))
{
    return Block($"TDD phase is 'implement': '{relative}' is an existing test file and is part of the agreed " +
                 "specification. Make it pass by changing src/. You may add NEW test files. If you believe the " +
                 "test is wrong, stop and explain why to the human; they can switch to ./dev phase spec.");
}

return 0;

static int Block(string reason)
{
    Console.Error.WriteLine(reason);
    return 2;
}
