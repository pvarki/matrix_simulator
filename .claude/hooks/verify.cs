#:property PublishAot=false
// Claude Code Stop hook: checks the work since the baseline in .claude/state/base before the agent may finish.
// The baseline is HEAD at session start (session-start.cs) or at the last ./dev phase spec|implement. Checks:
//   1. no path from .claude/human-only changed, and no .csproj gained NoWarn or a warnings-as-errors switch,
//   2. spec phase: nothing under src/ changed; implement phase: no existing test changed (new test files are fine),
//   3. code changed (anything but docs/ and *.md): formatting is clean (dotnet format --verify-no-changes) and the
//      fast suite runs. Implement: red tests block. Spec: red tests are expected; a reminder lists them.
// The diff cannot tell who changed a file, so messages ask the agent to undo only its own changes.
// Output: one JSON object on stdout (decision, reason, systemMessage), written once. A block also writes the reason
// to stderr and exits 2, so stray output from dotnet run cannot turn a block into a pass.
// Gives up after 3 blocks in a session so a stuck agent hands the problem to the human instead of looping.
// Reminders block once per stop chain (not when stop_hook_active). A green or reminded state is fingerprinted in
// .claude/state/verified, so a later stop with nothing changed costs one git diff, not a test run.
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

const int MaxConsecutiveBlocks = 3;
const StringComparison IgnoreCase = StringComparison.OrdinalIgnoreCase;
var root = Environment.GetEnvironmentVariable("CLAUDE_PROJECT_DIR") ?? Directory.GetCurrentDirectory();
var sessionId = string.Empty;
var stopHookActive = false;
try
{
    using var input = JsonDocument.Parse(Console.In.ReadToEnd());
    sessionId = input.RootElement.TryGetProperty("session_id", out var s) ? s.GetString() ?? string.Empty : string.Empty;
    stopHookActive = input.RootElement.TryGetProperty("stop_hook_active", out var a) && a.ValueKind == JsonValueKind.True;
}
catch (JsonException)
{
    // No or bad input: treat as a fresh stop in an unnamed session.
}

var stateDir = Path.Combine(root, ".claude", "state");
var counterFile = Path.Combine(stateDir, "stop-blocks");
var verifiedFile = Path.Combine(stateDir, "verified");
var baseFile = Path.Combine(stateDir, "base");
var phaseFile = Path.Combine(stateDir, "phase");
// Same rule in guard.cs, verify.cs, session-start.cs and tools/dev.cs (file-based apps cannot share code):
// a missing, empty or unknown phase file means spec, the safe default.
var phase = File.Exists(phaseFile) && File.ReadAllText(phaseFile).Trim() == "implement" ? "implement" : "spec";
// Same list in guard.cs. Without the file, .claude/ itself stays protected.
var listFile = Path.Combine(root, ".claude", "human-only");
var humanOnly = File.Exists(listFile)
    ? File.ReadAllLines(listFile).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#')).ToList()
    : [".claude/"];
var notes = new List<string>();

var head = Run("git", "rev-parse", "HEAD").Out.Trim();
var baseSha = File.Exists(baseFile) ? File.ReadAllText(baseFile).Trim() : string.Empty;
if (baseSha.Length == 0)
{
    baseSha = head;
    notes.Add($"Stop hook: no baseline in .claude/state/base, so it checked uncommitted work only. Ask the human to " +
              $"run ./dev phase {phase} to record one.");
}

var shortBase = baseSha[..Math.Min(7, baseSha.Length)];
var untracked = Run("git", "ls-files", "-z", "--others", "--exclude-standard").Out
    .Split('\0', StringSplitOptions.RemoveEmptyEntries);

// Skip everything when nothing changed since the last green or reminded stop of this session.
string fingerprint;
using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
{
    hash.AppendData(Encoding.UTF8.GetBytes(Run("git", "diff", "--binary", "HEAD").Out));
    foreach (var file in untracked)
    {
        hash.AppendData(Encoding.UTF8.GetBytes(file + "\0"));
        var full = Path.Combine(root, file);
        if (File.Exists(full))
        {
            hash.AppendData(File.ReadAllBytes(full));
        }
    }

    fingerprint = $"{sessionId} {phase} {baseSha} {head} {Convert.ToHexString(hash.GetHashAndReset())}";
}

if (File.Exists(verifiedFile) && File.ReadAllText(verifiedFile).Trim() == fingerprint)
{
    return 0;
}

Directory.CreateDirectory(stateDir);
File.Delete(verifiedFile);

// Pull, merge and rebase must not show up as the agent's work: path checks need a linear history since the baseline.
var rewritten = Run("git", "merge-base", "--is-ancestor", baseSha, head).Code != 0 ||
                Run("git", "rev-list", "--merges", $"{baseSha}..{head}").Out.Trim().Length > 0;

var changes = new List<(string Status, string Path)>();
if (!rewritten)
{
    var parts = Run("git", "diff", "--name-status", "-z", "--no-renames", baseSha).Out.Split('\0');
    for (var i = 0; i + 1 < parts.Length; i += 2)
    {
        changes.Add((parts[i], parts[i + 1]));
    }

    changes.AddRange(untracked.Select(u => ("A", u)));

    var problems = new List<string>();
    foreach (var (status, path) in changes)
    {
        if (humanOnly.Any(h => path.StartsWith(h, IgnoreCase)))
        {
            problems.Add($"`{path}` is human-only (.claude/human-only) and changed since {shortBase}.");
        }
        else if (phase == "spec" && path.StartsWith("src/", IgnoreCase))
        {
            problems.Add($"`{path}` is under src/, which is locked in the spec phase, and changed since {shortBase}.");
        }
        else if (phase == "implement" && path.StartsWith("tests/", IgnoreCase) && status is ("M" or "D" or "T"))
        {
            problems.Add($"`{path}` is an existing test and changed since {shortBase} (implement phase).");
        }
    }

    var addedProjectLines = Run("git", "diff", baseSha, "--", "*.csproj").Out.Split('\n')
        .Where(l => l.StartsWith('+') && !l.StartsWith("+++", StringComparison.Ordinal))
        .Concat(untracked.Where(u => u.EndsWith(".csproj", IgnoreCase) && File.Exists(Path.Combine(root, u)))
            .SelectMany(u => File.ReadAllLines(Path.Combine(root, u))));
    if (addedProjectLines.Any(l => Regex.IsMatch(l, "<NoWarn|TreatWarningsAsErrors|WarningsNotAsErrors")))
    {
        problems.Add($"A .csproj gained NoWarn, TreatWarningsAsErrors or WarningsNotAsErrors since {shortBase}. " +
                     "Warnings are errors in this repository (AGENTS.md); fix the warning instead.");
    }

    if (problems.Count > 0)
    {
        return Block(string.Join('\n', problems) +
                     "\nIf you changed it, undo your change; if not, stop and tell the human.");
    }
}

var codeChanged = rewritten || changes.Any(c => !c.Path.StartsWith("docs/", IgnoreCase) && !c.Path.EndsWith(".md", IgnoreCase));
if (!codeChanged)
{
    return Pass();
}

var format = Run("dotnet", "format", "Twin.Core.slnf", "--verify-no-changes");
if (format.Code != 0)
{
    return Block($"Formatting is not clean: run ./dev format, then stop again.\n{Filter(format.Out + format.Err)}");
}

var test = Run("dotnet", "test", "Twin.Core.slnf", "--nologo");
var failures = Filter(test.Out + test.Err);

if (rewritten)
{
    var result = test.Code == 0 ? "tests green" : "tests red";
    var note = $"History was merged or rewritten since baseline {shortBase}; path checks skipped, format and tests " +
               $"ran ({result}); ask the human to re-run ./dev phase {phase}.";
    return phase == "implement" && test.Code != 0
        ? Block($"{note}\nTests or build are red. Fix src/ (not the tests) until ./dev test passes.\n{failures}")
        : Remind(test.Code == 0 ? note : $"{note}\n{failures}");
}

if (phase == "spec")
{
    var testsChanged = changes.Any(c => c.Path.StartsWith("tests/", IgnoreCase));
    var handOver = "Whether each test fails for the expected reason is the human's call (docs/workflow.md step 3): " +
                   "list the new or changed tests and ask the human to review them and run ./dev phase implement.";
    return Remind(test.Code != 0
        ? $"Spec phase: these tests fail or do not compile:\n{failures}\n{handOver}"
        : testsChanged
            ? $"Spec phase: no test fails yet: spec tests must fail. {handOver}"
            : $"Spec phase: tests are green. {handOver}");
}

return test.Code == 0
    ? Pass()
    : Block($"Tests or build are red. Fix src/ (not the tests) until ./dev test passes.\n{failures}");

// A green stop: reset the block counter and remember the state so an unchanged later stop is skipped.
int Pass()
{
    File.Delete(counterFile);
    Remember();
    return Emit(null);
}

// Shown once per stop chain; does not count as a failed attempt. Remembered only once delivered, so a reminder
// held back under stop_hook_active (after a block) is shown at the next fresh stop.
int Remind(string reason)
{
    File.Delete(counterFile);
    if (stopHookActive)
    {
        return Emit(null);
    }

    Remember();
    return Emit(reason);
}

int Block(string reason)
{
    var parts = File.Exists(counterFile) ? File.ReadAllText(counterFile).Split(' ') : [];
    var blocks = (parts.Length == 2 && parts[0] == sessionId && int.TryParse(parts[1], CultureInfo.InvariantCulture, out var b) ? b : 0) + 1;
    if (blocks > MaxConsecutiveBlocks)
    {
        File.Delete(counterFile);
        notes.Add($"Stop hook gave up after {MaxConsecutiveBlocks} blocked attempts; the agent was told to explain " +
                  $"the problem. Last check:\n{reason}");
        return Emit(null);
    }

    Directory.CreateDirectory(stateDir);
    File.WriteAllText(counterFile, $"{sessionId} {blocks.ToString(CultureInfo.InvariantCulture)}");
    var last = blocks == MaxConsecutiveBlocks
        ? "\nThis is the last attempt: if you cannot fix it, stop and explain to the human what fails and why."
        : string.Empty;
    return Emit($"Do not finish yet (attempt {blocks}/{MaxConsecutiveBlocks}). {reason}{last}");
}

void Remember() => File.WriteAllText(verifiedFile, fingerprint);

int Emit(string? blockReason)
{
    var output = new JsonObject();
    if (blockReason is not null)
    {
        output["decision"] = "block";
        output["reason"] = blockReason;
    }

    if (notes.Count > 0)
    {
        output["systemMessage"] = string.Join('\n', notes);
    }

    if (output.Count > 0)
    {
        Console.Out.WriteLine(output.ToJsonString(new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
    }

    if (blockReason is null)
    {
        return 0;
    }

    Console.Error.WriteLine(blockReason);
    return 2;
}

// Keeps what names the problem (compiler errors, failing tests) and the end of the output.
static string Filter(string output)
{
    var lines = output.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Trim().Length > 0).ToList();
    var key = lines.Where(l => Regex.IsMatch(l, @"error [A-Z]+\d+|^\s*Failed |\[FAIL\]")).Distinct().Take(50).ToList();
    var tail = lines.TakeLast(20).ToList();
    return key.Count == 0 ? string.Join('\n', tail) : string.Join('\n', key) + "\n...\n" + string.Join('\n', tail);
}

(int Code, string Out, string Err) Run(string file, params string[] arguments)
{
    var info = new ProcessStartInfo(file)
    {
        WorkingDirectory = root,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
    };
    foreach (var argument in arguments)
    {
        info.ArgumentList.Add(argument);
    }

    using var process = Process.Start(info)!;
    var stdout = process.StandardOutput.ReadToEndAsync();
    var stderr = process.StandardError.ReadToEndAsync();
    process.WaitForExit();
    return (process.ExitCode, stdout.Result, stderr.Result);
}
