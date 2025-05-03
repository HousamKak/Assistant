using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Diagnostics;
using System.Threading.Tasks;

namespace VoiceAssistant.Setup
{
    /// <summary>
    /// Installs whisper.cpp from source with CUDA support into the VoiceAssistant.Service folder,
    /// downloads models, and updates the service's appsettings.json accordingly.
    /// </summary>
    public class InstallWhisper
    {
        private static readonly HttpClient _httpClient = new HttpClient();

        public static async Task Main(string[] args)
        {
            Console.WriteLine(
                "Installing whisper.cpp for Voice Assistant with CUDA Support (build from source)");
            Console.WriteLine(new string('=', 70));

            try
            {
                // Load installer configuration
                const string setupConfig = "whisper-setup.json";
                if (!File.Exists(setupConfig))
                {
                    Console.Error.WriteLine(
                        $"Error: Configuration file '{setupConfig}' not found.");
                    return;
                }

                using var doc = JsonDocument.Parse(File.ReadAllText(setupConfig));
                var cfg = doc.RootElement.GetProperty("WhisperSetup");
                string version = cfg.GetProperty("Version").GetString()!;
                bool cudaEnabled = cfg.GetProperty("CudaEnabled").GetBoolean();
                string modelsBaseUrl = cfg.GetProperty("ModelsBaseUrl").GetString()!;
                var modelEntry = cfg.GetProperty("Models").EnumerateObject().First();
                string modelFile = modelEntry.Value.GetString()!;

                // Determine directories
                string exeDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
                string setupRoot = Path.GetFullPath(Path.Combine(exeDir, "..", "..", "..", ".."));
                string serviceDir = Path.Combine(setupRoot, "VoiceAssistant.Service");
                string whisperDir = Path.Combine(serviceDir, "whisper");
                string modelsDir = Path.Combine(serviceDir, "Models");

                // Create target directories
                Directory.CreateDirectory(whisperDir);
                Directory.CreateDirectory(modelsDir);

                // Check for CUDA toolkit and set environment for CMake
                string cudaPath = string.Empty;
                if (cudaEnabled)
                {
                    Console.WriteLine("Checking for CUDA installation...");
                    cudaPath = Environment.GetEnvironmentVariable("CUDA_PATH") ?? string.Empty;
                    if (string.IsNullOrEmpty(cudaPath) || !Directory.Exists(cudaPath))
                    {
                        Console.WriteLine(
                            "Warning: CUDA_PATH not set or invalid. Ensure CUDA Toolkit is installed.");
                    }
                    else
                    {
                        Console.WriteLine($"CUDA found at: {cudaPath}");
                        // Ensure CMake can find CUDA
                        Environment.SetEnvironmentVariable("CUDA_PATH", cudaPath);
                        Environment.SetEnvironmentVariable("CUDA_TOOLKIT_ROOT_DIR", cudaPath);
                        Environment.SetEnvironmentVariable("CUDAToolkit_ROOT", cudaPath);
                        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
                        Environment.SetEnvironmentVariable(
                            "PATH",
                            Path.Combine(cudaPath, "bin") + ";" + pathEnv);
                        // Specify compiler explicitly
                        Environment.SetEnvironmentVariable(
                            "CMAKE_CUDA_COMPILER",
                            Path.Combine(cudaPath, "bin", "nvcc.exe"));
                    }
                }

                // Download and extract whisper.cpp source
                string srcUrl =
                    $"https://github.com/ggerganov/whisper.cpp/archive/refs/tags/v{version}.zip";
                string zipPath = Path.Combine(Path.GetTempPath(), $"whisper-src-v{version}.zip");

                Console.WriteLine($"Downloading whisper.cpp source v{version}...");
                await DownloadFileAsync(srcUrl, zipPath);
                Console.WriteLine("Extracting source...");
                ZipFile.ExtractToDirectory(zipPath, whisperDir, overwriteFiles: true);
                File.Delete(zipPath);

                // Identify extracted source directory
                var srcSub = Directory.GetDirectories(whisperDir).FirstOrDefault();
                if (srcSub == null)
                {
                    Console.Error.WriteLine("Error: Failed to locate extracted source directory.");
                    return;
                }

                // Prepare build directory
                string buildDir = Path.Combine(srcSub, "build");
                Directory.CreateDirectory(buildDir);

                // Run CMake configure
                Console.WriteLine("Configuring CMake (CUDA support enabled if available)...");
                string cmakeArgs = string.Join(
                    " ",
                    "-DGGML_CUDA=ON",
                    "-DGGML_CUBLAS=ON",
                    "-DWHISPER_CUDA=ON",
                    "-DWHISPER_CUBLAS=ON",
                    "-DCMAKE_BUILD_TYPE=Release",
                    $"\"{srcSub}\"");
                await RunProcessAsync("cmake", cmakeArgs, buildDir);

                // Run CMake build
                Console.WriteLine("Building whisper.cpp (this may take a while)...");
                await RunProcessAsync(
                    "cmake",
                    "--build . --config Release --target ALL_BUILD",
                    buildDir);

                // Locate built executable
                string[] candidates = new[]
                {
                    Path.Combine(buildDir, "main.exe"),
                    Path.Combine(buildDir, "Release", "main.exe"),
                    Path.Combine(buildDir, "bin", "Release", "main.exe")
                };
                string builtExe = candidates.FirstOrDefault(File.Exists)!;
                if (builtExe == null)
                {
                    Console.Error.WriteLine("Error: Built main.exe not found.");
                    return;
                }

                // Locate the folder where your build put the files:
                string builtDir = Path.GetDirectoryName(builtExe)!;

                // Copy every .dll (and any other auxiliary files) from the build output
                foreach (var dllPath in Directory.GetFiles(builtDir, "*.dll"))
                {
                    string targetPath = Path.Combine(whisperDir, Path.GetFileName(dllPath));
                    File.Copy(dllPath, targetPath, overwrite: true);
                }

                // Then copy the main.exe itself
                string destExe = Path.Combine(whisperDir, "main.exe");
                File.Copy(builtExe, destExe, overwrite: true);

                // Verify CUDA support in binary
                if (cudaEnabled)
                {
                    Console.WriteLine("Verifying CUDA support in binary...");
                    bool gpuOk = await CheckCudaSupportAsync(destExe);
                    Console.WriteLine(
                        gpuOk
                            ? "CUDA support detected in whisper.cpp executable!"
                            : "Warning: No CUDA support detected; fallback to CPU.");
                }

                // Download model file
                string modelUrl = $"{modelsBaseUrl}/{modelFile}";
                string modelDest = Path.Combine(modelsDir, modelFile);
                if (!File.Exists(modelDest))
                {
                    Console.WriteLine($"Downloading model {modelFile}...");
                    await DownloadFileWithProgressAsync(modelUrl, modelDest);
                }
                else
                {
                    Console.WriteLine($"Model {modelFile} already exists; skipping.");
                }

                // Update service appsettings.json
                Console.WriteLine("Updating VoiceAssistant.Service/appsettings.json...");
                UpdateAppSettings(serviceDir, modelFile);

                Console.WriteLine("\nInstallation complete!");
                Console.WriteLine($"- Executable in: {whisperDir}");
                Console.WriteLine($"- Model in: {modelsDir}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Installation error: {ex.Message}");
            }
        }

        private static async Task DownloadFileAsync(string url, string dest)
        {
            using var res = await _httpClient.GetAsync(
                url,
                HttpCompletionOption.ResponseHeadersRead);
            res.EnsureSuccessStatusCode();
            await using var fs = new FileStream(dest, FileMode.Create, FileAccess.Write);
            await res.Content.CopyToAsync(fs);
        }

        private static async Task DownloadFileWithProgressAsync(string url, string dest)
        {
            using var res = await _httpClient.GetAsync(
                url,
                HttpCompletionOption.ResponseHeadersRead);
            res.EnsureSuccessStatusCode();

            var total = res.Content.Headers.ContentLength ?? -1L;
            await using var stream = await res.Content.ReadAsStreamAsync();
            await using var fs = new FileStream(dest, FileMode.Create, FileAccess.Write);

            var buffer = new byte[8192];
            long read = 0;
            int len;
            var sw = Stopwatch.StartNew();

            while ((len = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                await fs.WriteAsync(buffer, 0, len);
                read += len;

                if (sw.ElapsedMilliseconds > 1000)
                {
                    double pct = total > 0 ? read * 100.0 / total : 0;
                    Console.Write($"\r{pct:F1}% ({read / 1024.0 / 1024.0:F1} MB)");
                    sw.Restart();
                }
            }
            Console.WriteLine();
        }

        private static async Task<bool> CheckCudaSupportAsync(string exe)
        {
            var (outp, err) = await RunProcessAsync(exe, "--help", Path.GetDirectoryName(exe)!);
            return (outp + err).Contains("CUDA") || (outp + err).Contains("cuBLAS");
        }

        private static async Task<(string stdout, string stderr)> RunProcessAsync(
            string file,
            string args,
            string workDir)
        {
            var psi = new ProcessStartInfo(file, args)
            {
                WorkingDirectory = workDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi)!;
            var outTask = p.StandardOutput.ReadToEndAsync();
            var errTask = p.StandardError.ReadToEndAsync();
            await p.WaitForExitAsync();
            return (await outTask, await errTask);
        }

        private static void UpdateAppSettings(string serviceDir, string modelFile)
        {
            string path = Path.Combine(serviceDir, "appsettings.json");
            if (!File.Exists(path))
            {
                Console.WriteLine($"Warning: {path} not found, skipping config update.");
                return;
            }

            string json = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(json);
            using var ms = new MemoryStream();
            using var writer = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true });

            writer.WriteStartObject();
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.NameEquals("VoiceAssistant"))
                {
                    writer.WritePropertyName("VoiceAssistant");
                    writer.WriteStartObject();
                    foreach (var sub in prop.Value.EnumerateObject())
                    {
                        if (sub.NameEquals("SpeechRecognition"))
                        {
                            writer.WritePropertyName("SpeechRecognition");
                            writer.WriteStartObject();
                            writer.WriteString(
                                "ModelPath",
                                Path.Combine("Models", modelFile));
                            writer.WriteString(
                                "ModelType",
                                Path.GetFileNameWithoutExtension(modelFile));
                            writer.WriteString("Language", "en");
                            writer.WriteNumber("MaxCommandDurationSeconds", 5);
                            writer.WriteBoolean("UseGpu", true);
                            writer.WriteBoolean("CudaEnabled", true);
                            writer.WriteEndObject();
                        }
                        else
                        {
                            sub.WriteTo(writer);
                        }
                    }
                    writer.WriteEndObject();
                }
                else
                {
                    prop.WriteTo(writer);
                }
            }
            writer.WriteEndObject();
            writer.Flush();

            File.WriteAllText(path, System.Text.Encoding.UTF8.GetString(ms.ToArray()));
        }
    }
}
