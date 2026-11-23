#:property PublishAot=false
// CI gate for test-first commits (docs/workflow.md, ADR 0008). It fails when a commit whose subject does not start
// with "test:" modifies or deletes an existing file under tests/, unless a line of its message reads
// "Test-change-approved-by: <name>". New test files are allowed. A merge is checked only for what was edited by hand
// while merging: its tree is compared with the tree git builds itself (git merge-tree, git 2.38 or later).
//
//   dotnet run tools/check-test-edits.cs -- [base=origin/main] [head=HEAD]     (or ./dev test-edits [base] [head])
//
// Exit codes: 0 nothing flagged, 1 commits flagged, 2 error (unknown ref, git failure, empty range). It fails closed:
// a wrong ref never passes. Only head == base passes as "nothing to check".
using System.Diagnostics;
using System.Text.RegularExpressions;

try
{
    Directory.SetCurrentDirectory(Git(["rev-parse", "--show-toplevel"]).Trim());
    var baseRef = args.Length > 0 ? args[0] : "origin/main";
    var headRef = args.Length > 1 ? args[1] : "HEAD";
    var baseSha = Git(["rev-parse", "--verify", "--end-of-options", baseRef + "^{commit}"]).Trim();
    var headSha = Git(["rev-parse", "--verify", "--end-of-options", headRef + "^{commit}"]).Trim();
    if (baseSha == headSha)
    {
        Console.WriteLine($"Nothing to check: {headRef} is {baseRef}.");
        return 0;
    }

    var commits = Lines(Git(["rev-list", "--reverse", headSha, "^" + baseSha]));
    if (commits.Count == 0)
    {
        throw new GitException($"no commits in {headRef} that are not in {baseRef} ({headRef} is behind it). Check the refs.");
    }

    var flagged = 0;
    foreach (var commit in commits)
    {
        var fields = Git(["show", "-s", "--format=%P%x00%s%x00%B", commit]).Split('\0');
        var parents = fields[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var subject = fields[1];
        if (subject.StartsWith("test:", StringComparison.Ordinal) || Approval().IsMatch(fields[2]) || parents.Length == 0)
        {
            continue;
        }

        var changes = parents.Length switch
        {
            1 => Git(["diff-tree", "-r", "-z", "--no-commit-id", "--name-status", "--no-renames", commit]),
            2 => Git(["diff", "-z", "--name-status", "--no-renames", MergeTree(parents[0], parents[1]), commit, "--", "tests/"]),
            _ => throw new GitException($"{commit[..9]} is an octopus merge ({parents.Length} parents); this check does not handle it."),
        };
        var paths = EditedTests(changes);
        if (paths.Count > 0)
        {
            flagged++;
            Console.WriteLine($"{commit[..9]} {subject}");
            foreach (var path in paths)
            {
                Console.WriteLine($"    {path}");
            }
        }
    }

    if (flagged == 0)
    {
        Console.WriteLine($"OK: {commits.Count} commit(s) checked; no unapproved edits to existing tests.");
        return 0;
    }

    Console.WriteLine($"""

        {flagged} commit(s) above modify (M) or delete (D) existing files under tests/ without approval.
        Tests are the specification; an implementation commit must not change them (docs/workflow.md). Either:
          - move the test edits into a commit whose subject starts with "test:", or
          - if a person approved the change, add this line anywhere in the commit message:
                Test-change-approved-by: <name>
        To reword: git commit --amend if the commit is HEAD, otherwise git rebase -i --rebase-merges <base>
        (a plain rebase -i drops merge commits).
        """);
    return 1;
}
catch (GitException e)
{
    Console.Error.WriteLine($"check-test-edits: {e.Message}");
    return 2;
}

// The tree git produces for a merge of p1 and p2. Exit code 1 means conflicts (the tree then holds conflict markers),
// which is still a valid tree to compare against.
static string MergeTree(string p1, string p2) =>
    Git(["merge-tree", "--write-tree", p1, p2], maxOkExitCode: 1).Split('\n')[0].Trim();

// "M\0path\0D\0path\0..." from -z --name-status: the paths under tests/ whose status is M or D.
static List<string> EditedTests(string nameStatus)
{
    var parts = nameStatus.Split('\0');
    var paths = new List<string>();
    for (var i = 0; i + 1 < parts.Length; i += 2)
    {
        if (parts[i] is "M" or "D" && parts[i + 1].StartsWith("tests/", StringComparison.Ordinal))
        {
            paths.Add($"{parts[i]} {parts[i + 1]}");
        }
    }

    return paths;
}

static List<string> Lines(string text) => [.. text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

static string Git(string[] arguments, int maxOkExitCode = 0)
{
    var info = new ProcessStartInfo("git") { RedirectStandardOutput = true, RedirectStandardError = true };
    foreach (var argument in arguments)
    {
        info.ArgumentList.Add(argument);
    }

    using var process = Process.Start(info) ?? throw new GitException("could not start git");
    var error = process.StandardError.ReadToEndAsync();
    var output = process.StandardOutput.ReadToEnd();
    process.WaitForExit();
    if (process.ExitCode < 0 || process.ExitCode > maxOkExitCode)
    {
        throw new GitException($"git {string.Join(' ', arguments)} failed (exit {process.ExitCode}): {error.Result.Trim()}");
    }

    return output;
}

partial class Program
{
    [GeneratedRegex(@"^Test-change-approved-by:[ \t]*\S", RegexOptions.Multiline)]
    private static partial Regex Approval();
}

sealed class GitException(string message) : Exception(message);
