using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Minimatch;
using System.IO;
using System.Security.Cryptography;
using GitHub.DistributedTask.Expressions2.Sdk;
using GitHub.DistributedTask.Pipelines.ContextData;
using GitHub.DistributedTask.Pipelines.ObjectTemplating;
using GitHub.Runner.Common;
using GitHub.Runner.Sdk;
using System.Reflection;
using System.Threading;
using System.Runtime.Serialization;

namespace GitHub.Runner.Worker.Handlers
{
    public class FunctionTrace : ITraceWriter
    {
        private GitHub.DistributedTask.Expressions2.ITraceWriter _trace;

        public FunctionTrace(GitHub.DistributedTask.Expressions2.ITraceWriter trace)
        {
            _trace = trace;
        }
        public void Info(string message)
        {
            _trace.Info(message);
        }

        public void Verbose(string message)
        {
            _trace.Info(message);
        }
    }

    public sealed class HashFiles : Function
    {
        protected sealed override Object EvaluateCore(
            EvaluationContext context,
            out ResultMemory resultMemory)
        {
            resultMemory = null;

            // hashFiles() only works on the runner and only works with files under GITHUB_WORKSPACE
            // Since GITHUB_WORKSPACE is set by runner, I am using that as the fact of this code runs on server or runner.
            if (context.State is DistributedTask.ObjectTemplating.TemplateContext templateContext &&
                templateContext.ExpressionValues.TryGetValue(PipelineTemplateConstants.GitHub, out var githubContextData) &&
                githubContextData is DictionaryContextData githubContext &&
                githubContext.TryGetValue(PipelineTemplateConstants.Workspace, out var workspace) == true &&
                workspace is StringContextData workspaceData)
            {
                string searchRoot = workspaceData.Value;
                bool followSymlink = false;
                string pattern = "";
                if (Parameters.Count == 1)
                {
                    pattern = Parameters[0].Evaluate(context).ConvertToString();
                }
                else
                {
                    if (string.Equals(Parameters[0].Evaluate(context).ConvertToString(), "--followSymbolicLinks", StringComparison.OrdinalIgnoreCase))
                    {
                        followSymlink = true;
                    }

                    pattern = Parameters[1].Evaluate(context).ConvertToString();
                }


                // Convert slashes on Windows
                if (s_isWindows)
                {
                    pattern = pattern.Replace('\\', '/');
                }

                context.Trace.Info($"Search root directory: '{searchRoot}'");
                context.Trace.Info($"Search pattern: '{pattern}'");

                string binDir = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
                string runnerRoot = new DirectoryInfo(binDir).Parent.FullName;

                string node = Path.Combine(runnerRoot, "externals", "node12", "bin", "node");
                if (s_isWindows)
                {
                    node = node + ".exe";
                }
                string hashFilesScript = Path.Combine(binDir, "hashFiles");
                var hashResult = string.Empty;
                var p = new ProcessInvoker(new FunctionTrace(context.Trace));
                p.ErrorDataReceived += ((_, data) =>
                {
                    if (!string.IsNullOrEmpty(data.Data) && data.Data.StartsWith("__OUTPUT__") && data.Data.EndsWith("__OUTPUT__"))
                    {
                        hashResult = data.Data.Substring(10, data.Data.Length - 20);
                        context.Trace.Info($"Hash result: '{hashResult}'");
                    }
                    else
                    {
                        context.Trace.Info(data.Data);
                    }
                });

                p.OutputDataReceived += ((_, data) =>
                {
                    context.Trace.Info(data.Data);
                });

                var hashFilesArgs = $"\"{hashFilesScript.Replace("\"", "\\\"")}\"";
                if (followSymlink)
                {
                    hashFilesArgs = hashFilesArgs + " --followSymbolicLinks";
                }

                hashFilesArgs = hashFilesArgs + $" \"{pattern.Replace("\"", "\\\"")}\"";
                int exitCode = p.ExecuteAsync(workingDirectory: searchRoot,
                                              fileName: node,
                                              arguments: hashFilesArgs,
                                              environment: null,
                                              requireExitCodeZero: false,
                                              cancellationToken: new CancellationTokenSource(TimeSpan.FromMinutes(120)).Token).GetAwaiter().GetResult();

                if (exitCode != 0)
                {
                    throw new InvalidOperationException($"hashFiles('{ExpressionUtility.StringEscape(pattern)}') failed. Fail to discover files under directory '{searchRoot}'");
                }

                return hashResult;
            }
            else
            {
                throw new InvalidOperationException("'hashfiles' expression function is only supported under runner context.");
            }
        }

        private static readonly bool s_isWindows = Environment.OSVersion.Platform != PlatformID.Unix && Environment.OSVersion.Platform != PlatformID.MacOSX;
    }
}