using Microsoft.Build.Framework;
using Microsoft.TemplateEngine.TemplateLocalizer.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace TemplateLocalizer.Tasks
{
    public class LocalizeTemplatesTask : Microsoft.Build.Utilities.Task
    {
        [Required]
        public string? TemplateFolder { get; set; }

        public override bool Execute()
        {
            Log.LogMessage(MessageImportance.High, $"Localizing templates in {TemplateFolder}");

            bool failed = false;
            List<string> templateJsonFiles = new();

            if (string.IsNullOrWhiteSpace(TemplateFolder))
            {
                throw new Exception("TemplateFolder property is not set in MSBuild target");
            }

            foreach (string templatePath in new[] { TemplateFolder } )
            {
                int filesBeforeAdd = templateJsonFiles.Count;
                templateJsonFiles.AddRange(GetTemplateJsonFiles(templatePath, true));

                if (filesBeforeAdd == templateJsonFiles.Count)
                {
                    // No new files has been added by this path. This is an indication of a bad input.
                    throw new Exception("No template.jsons were found in the folder " + TemplateFolder);
                }
            }

            List<(string TemplateJsonPath, Task<ExportResult> Task)> runningExportTasks = new(templateJsonFiles.Count);

            foreach (string templateJsonPath in templateJsonFiles)
            {
                ExportOptions exportOptions = new(false, targetDirectory: null, languages: new[] { "en" });
                runningExportTasks.Add(
                    (templateJsonPath,
                    new Microsoft.TemplateEngine.TemplateLocalizer.Core.TemplateLocalizer().ExportLocalizationFilesAsync(templateJsonPath, exportOptions, default))
                );
            }

            try
            {
                System.Threading.Tasks.Task.Run(
                    () => System.Threading.Tasks.Task.WhenAll(runningExportTasks.Select(t => t.Task))).GetAwaiter().GetResult();
            }
            catch (Exception)
            {
                // Task.WhenAll will only throw one of the exceptions. We need to log them all. Handle this outside of catch block.
            }

            foreach ((string TemplateJsonPath, Task<ExportResult> Task) pathTaskPair in runningExportTasks)
            {
                if (pathTaskPair.Task.IsCanceled)
                {
                    Log.LogWarning($"The task was cancelled: {pathTaskPair.TemplateJsonPath}");
                    continue;
                }
                else if (pathTaskPair.Task.IsFaulted)
                {
                    failed = true;
                    Log.LogErrorFromException(pathTaskPair.Task.Exception, true, true, pathTaskPair.TemplateJsonPath);
                }
                else
                {
                    // Tasks is known to have already completed. We can get the result without await.
                    ExportResult result = pathTaskPair.Task.Result;
                    failed |= !result.Succeeded;

                    if (result.Succeeded)
                    {
                        Log.LogMessage(MessageImportance.High, $"Template file {result.TemplateJsonPath} was successfully localized.");
                    }
                    else
                    {
                        if (result.InnerException != null)
                        {
                            Log.LogErrorFromException(pathTaskPair.Task.Exception, true, true, result.TemplateJsonPath);
                        }
                        else
                        {
                            Log.LogError($"Failed to localize {result.TemplateJsonPath}: {result.ErrorMessage}.");
                        }
                    }
                }
            }
            return !failed;
        }

        private IEnumerable<string> GetTemplateJsonFiles(string path, bool searchSubdirectories)
        {
            if (string.IsNullOrEmpty(path))
            {
                yield break;
            }

            if (File.Exists(path))
            {
                yield return path;
                yield break;
            }

            if (!Directory.Exists(path))
            {
                // This path neither points to a file nor to a directory.
                yield break;
            }

            if (!searchSubdirectories)
            {
                string filePath = Path.Combine(path, ".template.config", "template.json");
                if (File.Exists(filePath))
                {
                    yield return filePath;
                }
                else
                {
                    filePath = Path.Combine(path, "template.json");
                    if (File.Exists(filePath))
                    {
                        yield return filePath;
                    }
                }

                yield break;
            }

            foreach (string filePath in Directory.EnumerateFiles(path, "template.json", SearchOption.AllDirectories))
            {
                string? directoryName = Path.GetFileName(Path.GetDirectoryName(filePath));
                if (directoryName == ".template.config")
                {
                    yield return filePath;
                }
            }
        }
    }
}
