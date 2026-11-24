using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace EasyBuilder
{
    /// <summary>How much of the changelog notes is written out.</summary>
    public enum ChangelogDetail
    {
        [Tooltip("Features, fixes, breaking changes and untagged notes.")]
        Brief,

        [Tooltip("Adds performance and other changes, and shows scopes.")]
        Normal,

        [Tooltip("Everything, including docs and maintenance, and indented detail lines.")]
        Detailed,
    }

    /// <summary>
    /// Writes the batch's changelog: the notes grouped by type (feat, fix, …) into a file next to the builds, and
    /// optionally as a new entry in a running history file such as CHANGELOG.md.
    /// </summary>
    internal static class Changelog
    {
        private const string HistoryTitle = "# Changelog";
        private const string EntryPrefix = "## ";

        // "feat: text", "fix(ui): text", "feat!: text"; an optional bullet in front.
        private static readonly Regex TypedLinePattern = new(@"^(?<type>[A-Za-z]+)(\((?<scope>[^)]*)\))?(?<breaking>!)?:\s*(?<text>.+)$");
        private static readonly Regex BulletPattern = new(@"^([-*+]|\d+[.)])\s+");
        private static readonly Regex HeadingPattern = new(@"^#{1,6}\s");

        private sealed class Category
        {
            public readonly string Title;
            public readonly ChangelogDetail MinDetail;
            public readonly string[] Types;

            public Category(string title, ChangelogDetail minDetail, params string[] types)
            {
                Title = title;
                MinDetail = minDetail;
                Types = types;
            }
        }

        // In the order they are written.
        private static readonly Category[] Categories =
        {
            new("Features", ChangelogDetail.Brief, "feat", "feature", "features", "add", "new"),
            new("Fixes", ChangelogDetail.Brief, "fix", "fixes", "bugfix", "hotfix", "bug"),
            new("Performance", ChangelogDetail.Normal, "perf", "performance"),
            new("Changes", ChangelogDetail.Normal, "refactor", "change", "changes", "improve", "improvement", "update", "style", "revert"),
            new("Documentation", ChangelogDetail.Detailed, "docs", "doc", "documentation"),
            new("Maintenance", ChangelogDetail.Detailed, "chore", "build", "ci", "test", "tests", "deps", "maintenance"),
            new("Other", ChangelogDetail.Brief),
        };

        private static Category Other => Categories[Categories.Length - 1];

        private sealed class Item
        {
            public Category Category;
            public string Scope;
            public bool Breaking;
            public string Text;
            public readonly List<string> Details = new();
        }

        /// <summary>Line endings normalized and surrounding blank lines removed.</summary>
        public static string Normalize(string notes) =>
            string.IsNullOrWhiteSpace(notes) ? string.Empty : notes.Replace("\r\n", "\n").Replace('\r', '\n').Trim('\n', ' ', '\t');

        public static bool TryParseDetail(string value, out ChangelogDetail detail) =>
            Enum.TryParse(value?.Trim(), true, out detail) && Enum.IsDefined(typeof(ChangelogDetail), detail);

        /// <summary>
        /// The notes grouped by type, in the order Features, Fixes, Performance, Changes, Documentation,
        /// Maintenance, Other, leaving out the groups below <paramref name="detail"/>. Lines are read as
        /// <c>type(scope): text</c>; a markdown heading such as <c>## Fixes</c> sets the group for the untagged
        /// lines under it, and indented lines add detail to the line above. Empty when nothing is left.
        /// </summary>
        public static string Format(string notes, ChangelogDetail detail, string headingPrefix = "## ")
        {
            var items = Parse(Normalize(notes));
            var text = new StringBuilder();

            foreach (var category in Categories)
            {
                // Breaking changes are always listed, whatever the detail level.
                var inCategory = items.Where(item => item.Category == category && (item.Breaking || detail >= category.MinDetail)).ToList();

                if (inCategory.Count == 0)
                    continue;

                if (text.Length > 0)
                    text.Append('\n');

                text.Append(headingPrefix).Append(category.Title).Append("\n\n");

                foreach (var item in inCategory)
                {
                    text.Append("- ");

                    if (item.Breaking)
                        text.Append("**Breaking:** ");

                    if (detail >= ChangelogDetail.Normal && !string.IsNullOrEmpty(item.Scope))
                        text.Append("**").Append(item.Scope).Append(":** ");

                    text.Append(item.Text).Append('\n');

                    if (detail == ChangelogDetail.Detailed)
                        foreach (var line in item.Details)
                            text.Append("  ").Append(line).Append('\n');
                }
            }

            return text.ToString().TrimEnd('\n');
        }

        private static List<Item> Parse(string notes)
        {
            var items = new List<Item>();
            var headingCategory = Other;

            foreach (var rawLine in notes.Split('\n'))
            {
                if (string.IsNullOrWhiteSpace(rawLine))
                    continue;

                var line = rawLine.Trim();

                if (HeadingPattern.IsMatch(line))
                {
                    headingCategory = FindCategory(line.TrimStart('#').Trim()) ?? Other;
                    continue;
                }

                // Indented lines describe the line above.
                if (char.IsWhiteSpace(rawLine[0]) && items.Count > 0)
                {
                    items[items.Count - 1].Details.Add(BulletPattern.IsMatch(line) ? line : "- " + line);
                    continue;
                }

                line = BulletPattern.Replace(line, string.Empty);
                var item = new Item { Category = headingCategory, Text = line };
                var match = TypedLinePattern.Match(line);

                if (match.Success && FindCategory(match.Groups["type"].Value) is { } category)
                {
                    item.Category = category;
                    item.Scope = match.Groups["scope"].Value.Trim();
                    item.Breaking = match.Groups["breaking"].Success;
                    item.Text = match.Groups["text"].Value.Trim();
                }

                if (item.Text.Length > 0)
                    item.Text = char.ToUpperInvariant(item.Text[0]) + item.Text.Substring(1);

                items.Add(item);
            }

            return items;
        }

        /// <summary>True for a line like "fix(ui): text" with a known type, with or without a bullet.</summary>
        public static bool IsTypedLine(string line)
        {
            var match = TypedLinePattern.Match(BulletPattern.Replace(line.Trim(), string.Empty));
            return match.Success && FindCategory(match.Groups["type"].Value) is { } category && category != Other;
        }

        // By type ("fix") or by heading ("Bug Fixes").
        private static Category FindCategory(string name)
        {
            var key = name.Trim().ToLowerInvariant();

            return Categories.FirstOrDefault(category => category.Types.Contains(key) || category.Title.ToLowerInvariant() == key)
                   ?? Categories.FirstOrDefault(category => category.Types.Any(type => type.Length > 3 && key.Contains(type)));
        }

        /// <summary>Resolves a path relative to the project folder (the folder that holds Assets/).</summary>
        public static string ResolvePath(string path) =>
            Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(ProjectFolder, path));

        public static string ProjectFolder => Path.GetDirectoryName(Path.GetFullPath(Application.dataPath));

        /// <summary>A path inside the project folder as a relative path, so it works on every machine.</summary>
        public static string ToProjectRelative(string path)
        {
            var full = Path.GetFullPath(path).Replace('\\', '/');
            var project = ProjectFolder.Replace('\\', '/').TrimEnd('/') + "/";
            return full.StartsWith(project, StringComparison.OrdinalIgnoreCase) ? full.Substring(project.Length) : full;
        }

        public static void Validate(string historyFile, List<string> errors)
        {
            if (string.IsNullOrWhiteSpace(historyFile))
                return;

            try
            {
                if (Directory.Exists(ResolvePath(historyFile)))
                    errors.Add($"The changelog history file '{historyFile}' is a folder. Pick a file such as CHANGELOG.md.");
            }
            catch (Exception e) when (e is ArgumentException || e is NotSupportedException || e is PathTooLongException)
            {
                errors.Add($"The changelog history file '{historyFile}' isn't a valid path.");
            }
        }

        /// <summary>
        /// Writes the formatted notes to <c>Changelog_v&lt;version&gt;_&lt;build&gt;.md</c> in the output folder and
        /// returns its path.
        /// </summary>
        public static string WriteReleaseNotes(string outputFolder, string formattedNotes, BuildTarget target)
        {
            var fileName = BuildRunner.SanitizeFileName($"Changelog_v{PlayerSettings.bundleVersion}_{BuildRunner.GetBuildNumber(target)}.md");
            var path = Path.Combine(outputFolder, fileName);
            File.WriteAllText(path, formattedNotes + "\n", new UTF8Encoding(false));
            return path;
        }

        /// <summary>
        /// Adds an entry for the current version and build number at the top of the history file, creating the
        /// file if needed. An existing entry for the same version and build number is replaced, so rebuilding
        /// doesn't add duplicates. The notes are formatted with level-3 headings.
        /// </summary>
        public static string UpdateHistory(string historyFile, string notes, ChangelogDetail detail, BuildTarget target)
        {
            var path = ResolvePath(historyFile);
            var heading = $"{EntryPrefix}v{PlayerSettings.bundleVersion} (build {BuildRunner.GetBuildNumber(target)})";
            var entry = $"{heading} - {DateTime.Now:yyyy-MM-dd}\n\n{Format(notes, detail, "### ")}\n";

            var lines = File.Exists(path)
                ? File.ReadAllText(path).Replace("\r\n", "\n").Split('\n').ToList()
                : new List<string> { HistoryTitle, "" };

            // Drop the previous entry for this version and build number, up to the next entry.
            var existing = lines.FindIndex(line => line == heading || line.StartsWith(heading + " "));

            if (existing >= 0)
            {
                var end = lines.FindIndex(existing + 1, IsEntryHeading);
                lines.RemoveRange(existing, (end < 0 ? lines.Count : end) - existing);
            }

            // Newest first: insert before the first entry, or append when there is none yet.
            var firstEntry = lines.FindIndex(IsEntryHeading);

            if (firstEntry < 0)
            {
                while (lines.Count > 0 && lines[lines.Count - 1].Length == 0)
                    lines.RemoveAt(lines.Count - 1);

                lines.Add("");
                firstEntry = lines.Count;
            }

            lines.Insert(firstEntry, entry);

            var directory = Path.GetDirectoryName(path);

            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(path, string.Join("\n", lines).TrimEnd('\n') + "\n", new UTF8Encoding(false));
            return path;
        }

        // "## v1.2 …" but not "### Fixes".
        private static bool IsEntryHeading(string line) => line.StartsWith(EntryPrefix) && !line.StartsWith(EntryPrefix.TrimEnd() + "#");
    }
}
