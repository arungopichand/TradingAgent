using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;

namespace KrishAgent.Services
{
    public sealed class NodeBridgeService
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true
        };

        private readonly IHostEnvironment _hostEnvironment;
        private readonly ILogger<NodeBridgeService> _logger;

        public NodeBridgeService(IHostEnvironment hostEnvironment, ILogger<NodeBridgeService> logger)
        {
            _hostEnvironment = hostEnvironment;
            _logger = logger;
        }

        public async Task<NodeBridgeHttpResponse> SendHttpAsync(NodeBridgeHttpRequest request, CancellationToken cancellationToken = default)
        {
            var payload = JsonSerializer.Serialize(request, JsonOptions);
            using var process = StartNodeProcess("http-bridge.mjs");
            await process.StandardInput.WriteAsync(payload.AsMemory(), cancellationToken);
            await process.StandardInput.FlushAsync(cancellationToken);
            process.StandardInput.Close();

            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            var stdout = (await stdoutTask).Trim();
            var stderr = (await stderrTask).Trim();

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Node HTTP bridge failed with exit code {process.ExitCode}. {BuildErrorText(stdout, stderr)}".Trim());
            }

            if (string.IsNullOrWhiteSpace(stdout))
            {
                throw new InvalidOperationException("Node HTTP bridge returned an empty response.");
            }

            var response = JsonSerializer.Deserialize<NodeBridgeHttpResponse>(stdout, JsonOptions);
            if (response == null)
            {
                throw new InvalidOperationException("Node HTTP bridge returned an unreadable response.");
            }

            return response;
        }

        public async Task<Process> StartAlpacaStreamAsync(NodeAlpacaStreamRequest request, CancellationToken cancellationToken = default)
        {
            var payload = JsonSerializer.Serialize(request, JsonOptions);
            var process = StartNodeProcess("alpaca-stream-bridge.mjs");

            try
            {
                await process.StandardInput.WriteAsync(payload.AsMemory(), cancellationToken);
                await process.StandardInput.FlushAsync(cancellationToken);
                process.StandardInput.Close();
                return process;
            }
            catch
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to stop Node Alpaca stream bridge after startup error.");
                }

                process.Dispose();
                throw;
            }
        }

        public static bool IsTlsStackFailure(Exception exception)
        {
            for (Exception? current = exception; current != null; current = current.InnerException)
            {
                var message = current.Message ?? string.Empty;
                if (message.Contains("SEC_E_NO_CREDENTIALS", StringComparison.OrdinalIgnoreCase) ||
                    message.Contains("No credentials are available in the security package", StringComparison.OrdinalIgnoreCase) ||
                    message.Contains("The SSL connection could not be established", StringComparison.OrdinalIgnoreCase) ||
                    message.Contains("Authentication failed, see inner exception", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private Process StartNodeProcess(string scriptName)
        {
            var scriptPath = Path.Combine(_hostEnvironment.ContentRootPath, "NodeBridge", scriptName);
            if (!File.Exists(scriptPath))
            {
                throw new FileNotFoundException($"Node bridge script not found: {scriptPath}", scriptPath);
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = "node",
                Arguments = $"\"{scriptPath}\"",
                WorkingDirectory = _hostEnvironment.ContentRootPath,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            var process = new Process
            {
                StartInfo = startInfo,
                EnableRaisingEvents = true
            };

            if (!process.Start())
            {
                process.Dispose();
                throw new InvalidOperationException($"Failed to start Node bridge script {scriptName}.");
            }

            return process;
        }

        private static string BuildErrorText(string stdout, string stderr)
        {
            if (!string.IsNullOrWhiteSpace(stderr) && !string.IsNullOrWhiteSpace(stdout))
            {
                return $"{stderr} {stdout}";
            }

            return !string.IsNullOrWhiteSpace(stderr) ? stderr : stdout;
        }
    }

    public sealed class NodeBridgeHttpRequest
    {
        public string Url { get; set; } = string.Empty;

        public string Method { get; set; } = "GET";

        public Dictionary<string, string> Headers { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public string Body { get; set; } = string.Empty;

        public int TimeoutMs { get; set; } = 30000;
    }

    public sealed class NodeBridgeHttpResponse
    {
        public int StatusCode { get; set; }

        public Dictionary<string, string> Headers { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public string Body { get; set; } = string.Empty;
    }

    public sealed class NodeAlpacaStreamRequest
    {
        public string Url { get; set; } = string.Empty;

        public string Key { get; set; } = string.Empty;

        public string Secret { get; set; } = string.Empty;

        public string[] Trades { get; set; } = [];

        public string[] Bars { get; set; } = [];

        public string[] UpdatedBars { get; set; } = [];
    }
}
