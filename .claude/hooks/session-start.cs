#:property PublishAot=false
// Claude Code SessionStart hook: everything printed here is added to the agent's context.
// On a new session (source startup or clear) it records HEAD in .claude/state/base, the baseline verify.cs diffs
// against at stop. On resume, compact and fork the baseline is kept, so work from before the break is still checked.
using System.Diagnostics;
using System.Text.Json;

var root = Environment.GetEnvironmentVariable("CLAUDE_PROJECT_DIR") ?? Directory.GetCurrentDirectory();
using var input = JsonDocument.Parse(Console.In.ReadToEnd());
var source = input.RootElement.TryGetProperty("source", out var s) ? s.GetString() : null;
var phaseFile = Path.Combine(root, ".claude", "state", "phase");
// Same rule in guard.cs, verify.cs, session-start.cs and tools/dev.cs (file-based apps cannot share code):
// a missing, empty or unknown phase file means spec, the safe default.
var phase = File.Exists(phaseFile) && File.ReadAllText(phaseFile).Trim() == "implement" ? "implement" : "spec";

if (source is "startup" or "clear")
{
    var info = new ProcessStartInfo("git", "rev-parse HEAD") { WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true };
    using var git = Process.Start(info)!;
    var head = git.StandardOutput.ReadToEnd().Trim();
    git.WaitForExit();
    if (git.ExitCode == 0)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(phaseFile)!);
        File.WriteAllText(Path.Combine(root, ".claude", "state", "base"), head);
    }
}

Console.WriteLine($"Current TDD phase: {phase}. " + (phase == "spec"
    ? "Only tests may be written now; src/ is locked."
    : "Implement against the existing tests; do not modify existing test files."));
Console.WriteLine("Follow AGENTS.md. Use the tdd-feature skill for any behaviour change. Run tests with ./dev test.");
return 0;
