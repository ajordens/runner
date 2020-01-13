using System;
using System.IO;
using GitHub.DistributedTask.Expressions2.Sdk;
using GitHub.DistributedTask.Pipelines.ContextData;
using GitHub.DistributedTask.Pipelines.ObjectTemplating;
using GitHub.Runner.Sdk;
using System.Reflection;
using System.Threading;
using System.Collections.Generic;

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
            var templateContext = context.State as DistributedTask.ObjectTemplating.TemplateContext;
            ArgUtil.NotNull(templateContext, nameof(templateContext));
            templateContext.ExpressionValues.TryGetValue(PipelineTemplateConstants.GitHub, out var githubContextData);
            ArgUtil.NotNull(githubContextData, nameof(githubContextData));
            var githubContext = githubContextData as DictionaryContextData;
            ArgUtil.NotNull(githubContext, nameof(githubContext));
            githubContext.TryGetValue(PipelineTemplateConstants.Workspace, out var workspace);
            var workspaceData = workspace as StringContextData;
            ArgUtil.NotNull(workspaceData, nameof(workspaceData));

            string githubWorkspace = workspaceData.Value;
            bool followSymlink = false;
            string pattern = "";
            if (Parameters.Count == 1)
            {
                pattern = Parameters[0].Evaluate(context).ConvertToString();
            }
            else
            {
                var option = Parameters[0].Evaluate(context).ConvertToString();
                if (string.Equals(option, "--follow-symbolic-links", StringComparison.OrdinalIgnoreCase))
                {
                    followSymlink = true;
                }
                else
                {
                    throw new ArgumentOutOfRangeException($"Invalid glob option {option}, avaliable option: '--follow-symbolic-links'.");
                }

                pattern = Parameters[1].Evaluate(context).ConvertToString();
            }

            context.Trace.Info($"Search root directory: '{githubWorkspace}'");
            context.Trace.Info($"Search pattern: '{pattern}'");

            string binDir = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
            string runnerRoot = new DirectoryInfo(binDir).Parent.FullName;

            string node = Path.Combine(runnerRoot, "externals", "node12", "bin", $"node{IOUtil.ExeExtension}");
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

            var env = new Dictionary<string, string>();
            if (followSymlink)
            {
                env["followSymbolicLinks"] = "true";
            }
            env["pattern"] = pattern;

            int exitCode = p.ExecuteAsync(workingDirectory: githubWorkspace,
                                          fileName: node,
                                          arguments: $"\"{hashFilesScript.Replace("\"", "\\\"")}\"",
                                          environment: env,
                                          requireExitCodeZero: false,
                                          cancellationToken: new CancellationTokenSource(TimeSpan.FromSeconds(120)).Token).GetAwaiter().GetResult();

            if (exitCode != 0)
            {
                throw new InvalidOperationException($"hashFiles('{ExpressionUtility.StringEscape(pattern)}') failed. Fail to hash files under directory '{githubWorkspace}'");
            }

            return hashResult;
        }
    }
}