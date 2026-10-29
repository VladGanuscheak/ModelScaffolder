using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ModelScaffolder.Core.Templating;

namespace ModelScaffolder.Core.Output
{
    public enum FileAction
    {
        Add,
        Update,
        Unchanged
    }

    public sealed class PlannedFile
    {
        public PlannedFile(GeneratedFile file, string fullPath, FileAction action)
        {
            File = file;
            FullPath = fullPath;
            Action = action;
        }

        public GeneratedFile File { get; }
        public string FullPath { get; }
        public FileAction Action { get; }
    }

    /// <summary>
    ///     Plans and writes output. Existing files with different content are never overwritten unless
    ///     <c>overwrite</c> is set, so the generator cannot silently destroy user edits.
    ///     This is the seam where smarter regeneration (hash headers, 3-way merge) will plug in later.
    /// </summary>
    public static class FileWriter
    {
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

        public static List<PlannedFile> Plan(string outputDir, IEnumerable<GeneratedFile> files) =>
            files.Select(f =>
            {
                var full = Path.GetFullPath(Path.Combine(outputDir, f.RelativePath));
                var action = !System.IO.File.Exists(full) ? FileAction.Add
                    : ReadOrNull(full)?.Replace("\r\n", "\n") == f.Content ? FileAction.Unchanged
                    : FileAction.Update;
                return new PlannedFile(f, full, action);
            }).ToList();

        public static void Write(IEnumerable<PlannedFile> plan, bool overwrite, DiagnosticBag d)
        {
            var list = plan.ToList();
            var conflicts = list.Where(p => p.Action == FileAction.Update).ToList();
            if (conflicts.Count > 0 && !overwrite)
            {
                foreach (var c in conflicts)
                    d.Error("MS090", c.File.RelativePath, "File exists with different content. Re-run with --force to overwrite.");
                return;
            }

            var written = 0;
            foreach (var p in list.Where(p => p.Action != FileAction.Unchanged))
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(p.FullPath)!);
                    System.IO.File.WriteAllText(p.FullPath, p.File.Content, Utf8NoBom);
                    written++;
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    // Stop at the first failure (disk full, locked file, no permission): report what happened so far.
                    d.Error("MS091", p.File.RelativePath,
                        $"Cannot write the file ({ex.Message}). {written} file(s) were written before this one; fix the problem and run again.");
                    return;
                }
            }
        }

        /// <summary>Content of an existing file, or null when it cannot be read (then it is treated as changed).</summary>
        private static string? ReadOrNull(string path)
        {
            try
            {
                return System.IO.File.ReadAllText(path);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return null;
            }
        }
    }
}
