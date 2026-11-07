using System.Globalization;
using System.Text;
using System.Text.Json;
using NTech.DataStudio.Trainer.Catalog;
using NTech.DataStudio.Trainer.Cli;
using NTech.DataStudio.Trainer.Cli.Bootstrap;
using NTech.DataStudio.Trainer.Containers;
using NTech.DataStudio.Trainer.Hardware;
using NTech.DataStudio.Trainer.Runs;

Console.OutputEncoding = Encoding.UTF8;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

var options = Arguments.Parse(args);
if (options.Command is null or "help")
{
    Output.Help();
    return options.Command is null ? 2 : 0;
}

try
{
    var app = new AppComposition();
    return options.Command switch
    {
        "probe" => await Commands.ProbeAsync(app, options),
        "models" => Commands.Models(app),
        "init" => Commands.Init(options),
        "validate" => await Commands.ValidateAsync(app, options),
        "train" => await Commands.TrainAsync(app, options),
        "resume" => await Commands.ResumeAsync(app, options),
        "status" => await Commands.StatusAsync(app, options),
        "follow" => await Commands.FollowAsync(app, options),
        "stop" => await Commands.StopAsync(app, options),
        "export" => await Commands.ExportAsync(app, options),
        "compare" => await Commands.CompareAsync(app, options),
        _ => Output.Unknown(options.Command)
    };
}
catch (TrainerException problem)
{
    Output.Error(problem.Message);
    return 2;
}
catch (ContainerException problem)
{
    Output.Error(problem.Message);
    return 3;
}

namespace NTech.DataStudio.Trainer.Cli
{
    internal sealed record Arguments(string? Command, Dictionary<string, string> Values, HashSet<string> Flags)
    {
        public static Arguments Parse(string[] args)
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            var flags = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 1; index < args.Length; index++)
            {
                var name = args[index];
                if (!name.StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException($"Tham số không hợp lệ: {name}");
                if (index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal)) values[name[2..]] = args[++index];
                else flags.Add(name[2..]);
            }

            return new Arguments(args.Length > 0 ? args[0] : null, values, flags);
        }

        public string Require(string name) =>
            Values.TryGetValue(name, out var value) ? value : throw new TrainerException($"Thiếu --{name}.");

        public string Text(string name, string fallback) => Values.GetValueOrDefault(name, fallback);

        public int Number(string name, int fallback)
        {
            if (!Values.TryGetValue(name, out var text)) return fallback;
            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                ? value
                : throw new TrainerException($"--{name} phải là số nguyên.");
        }
    }

    internal static class Output
    {
        public static void Help()
        {
            Console.WriteLine("""
                NTech Trainer

                  probe    [--workspace <thư mục>] [--model yolo26s] [--imgsz 640] [--container]
                  models
                  init     --workspace <thư mục> --data <đường dẫn>/data.yaml
                  validate --workspace <thư mục> [--imgsz 640]
                  train    --workspace <thư mục> --model yolo26s [--imgsz 640] [--epochs 100] [--batch -1]
                           [--workers 8] [--seed 0] [--patience 100] [--no-preflight] [--accept-risk]
                           [--detach] [--image <tên>] [--set fliplr=0,hsv_h=0]
                  status   --workspace <thư mục>
                  follow   --workspace <thư mục> --run <id>
                  stop     --workspace <thư mục> --run <id>
                  resume   --workspace <thư mục> --run <id> [--detach]
                  export   --workspace <thư mục> --run <id> [--detach]
                  compare  --workspace <thư mục>
                """);
        }

        public static int Unknown(string command)
        {
            Error($"Không có lệnh {command}. Chạy help để xem danh sách.");
            return 2;
        }

        public static void Error(string message) => Colored(ConsoleColor.Red, "LỖI: " + message);

        public static void Warn(string message) => Colored(ConsoleColor.Yellow, "CẢNH BÁO: " + message);

        public static void Colored(ConsoleColor color, string text)
        {
            var before = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.WriteLine(text);
            Console.ForegroundColor = before;
        }

        public static ConsoleColor ColorOf(Grade grade) => grade switch
        {
            Grade.Excellent => ConsoleColor.Green,
            Grade.Good => ConsoleColor.Blue,
            Grade.Moderate => ConsoleColor.Yellow,
            Grade.Weak => ConsoleColor.DarkYellow,
            Grade.Inadequate => ConsoleColor.Red,
            _ => ConsoleColor.Gray
        };

        public static void Assessment(string title, AssessmentResult result)
        {
            Console.WriteLine();
            Console.Write(title + ": ");
            Colored(ColorOf(result.Overall), result.Overall.ToString());
            foreach (var criterion in result.Criteria)
            {
                Console.Write("  ");
                Console.ForegroundColor = ColorOf(criterion.Grade);
                Console.Write($"{criterion.Grade,-11}");
                Console.ResetColor();
                Console.WriteLine($" {criterion.Name}: {criterion.Reason} [nguồn: {criterion.Source}, độ tin cậy: {Confidence(criterion.Confidence)}]");
            }
        }

        private static string Confidence(Confidence value) => value switch
        {
            Hardware.Confidence.Measured => "đo thật",
            Hardware.Confidence.Reported => "hệ thống báo",
            _ => "giả định"
        };
    }

    internal static class Commands
    {
        public static async Task<int> ProbeAsync(AppComposition app, Arguments options)
        {
            var path = options.Text("workspace", Directory.GetCurrentDirectory());
            var machine = await app.Probe.ReadAsync(path);
            Console.WriteLine($"Hệ điều hành: {machine.Os}");
            foreach (var gpu in machine.Gpus)
                Console.WriteLine($"GPU: {gpu.Name}, {gpu.VramGb:0.#} GB (trống {gpu.VramFreeGb:0.#} GB), driver {gpu.Driver}, SM {gpu.ComputeCapability:0.0}");
            Output.Assessment("Máy", Assessor.Machine(machine));

            if (options.Values.TryGetValue("model", out var name))
            {
                var model = FindModel(app, name);
                Output.Assessment($"Tác vụ {model.Name}", Assessor.Workload(machine, model, options.Number("imgsz", 640)));
            }

            var tensorRt = Assessor.TensorRt(machine);
            Console.WriteLine();
            Console.WriteLine("Export: ONNX có sẵn trên mọi máy.");
            Console.WriteLine($"Export: TensorRT {(tensorRt.Eligible ? "đủ điều kiện" : "không khả dụng")}. {tensorRt.Reason}");

            if (!options.Flags.Contains("container")) return 0;

            var compute = TrainPlanner.ComputeFor(machine);
            var image = options.Text("image", JobService.ImageFor(compute));
            if (await app.Containers.ImageDigestAsync(image) is null) throw new TrainerException($"Không có image {image}.");
            var result = await app.Containers.RunToEndAsync(JobService.ProbeSpec(image, compute == "cuda"));
            Console.WriteLine();
            Console.WriteLine("Trong container:");
            Console.WriteLine(result.Succeeded ? result.Output.Trim() : result.Error.Trim());
            return result.Succeeded ? 0 : 3;
        }

        public static int Models(AppComposition app)
        {
            foreach (var family in app.Catalog.Visible)
            {
                var status = family.Status == ModelStatus.Supported ? "hỗ trợ" : "thử nghiệm";
                Console.WriteLine($"{family.Id,-8} {status,-11} cỡ {string.Join(",", family.Sizes),-12} license {family.License}");
            }

            Console.WriteLine();
            Output.Warn("Theo Ultralytics, model train bằng gói này thuộc AGPL-3.0 kể cả dùng nội bộ, trừ khi có Enterprise License. Xem mục 10 của thiết kế.");
            return 0;
        }

        public static int Init(Arguments options)
        {
            var workspace = Workspace.Create(options.Require("workspace"), options.Require("data"));
            Console.WriteLine($"Đã tạo workspace {workspace.Root}");
            Console.WriteLine($"Dataset tham chiếu, không chép và không sửa: {workspace.File_.DataYaml}");
            return 0;
        }

        public static async Task<int> ValidateAsync(AppComposition app, Arguments options)
        {
            var workspace = Workspace.Open(options.Require("workspace"));
            var machine = await app.Probe.ReadAsync(workspace.Root);
            var image = options.Text("image", JobService.ImageFor(TrainPlanner.ComputeFor(machine)));
            var report = await app.Datasets.CheckAsync(workspace, image, options.Number("imgsz", TrainPlanner.DefaultImgsz));
            foreach (var item in report.Errors) Output.Error(item.Message);
            foreach (var item in report.Warnings) Output.Warn(item.Message);
            Console.WriteLine(JsonSerializer.Serialize(report.Stats, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
            Console.WriteLine($"Fingerprint: {report.Fingerprint}");
            Console.WriteLine(report.Ok ? "Dataset đạt." : "Dataset chưa đạt.");
            return report.Ok ? 0 : 2;
        }

        public static async Task<int> TrainAsync(AppComposition app, Arguments options)
        {
            var workspace = Workspace.Open(options.Require("workspace"));
            var model = FindModel(app, options.Require("model"));
            var imgsz = options.Number("imgsz", 640);
            var machine = await app.Probe.ReadAsync(workspace.Root);

            Output.Assessment("Máy", Assessor.Machine(machine));
            Output.Assessment($"Tác vụ {model.Name}", Assessor.Workload(machine, model, imgsz));
            Console.WriteLine();
            Output.Warn($"{model.Name} dùng license {model.Family.License}. Cần kết luận license trước khi đưa model vào sản xuất hoặc bán.");

            var gate = Assessor.TrainGate(machine, model, imgsz);
            if (gate.Kind == GateKind.Blocked)
            {
                foreach (var reason in gate.Reasons) Output.Error(reason);
                return 2;
            }

            if (gate.Kind == GateKind.NeedsConfirmation)
            {
                foreach (var reason in gate.Reasons) Output.Warn(reason);
                if (!options.Flags.Contains("accept-risk"))
                {
                    Output.Error("Cần xác nhận: chạy lại với --accept-risk nếu vẫn muốn train.");
                    return 2;
                }
            }

            var compute = TrainPlanner.ComputeFor(machine);
            var image = options.Text("image", JobService.ImageFor(compute));
            var digest = await app.Containers.ImageDigestAsync(image)
                ?? throw new TrainerException($"Không có image {image}. Build bằng docker/trainer/Dockerfile.");
            var train = new TrainOptions(
                imgsz,
                options.Number("epochs", TrainPlanner.DefaultEpochs),
                options.Number("batch", TrainPlanner.DefaultBatch(compute)),
                TrainPlanner.DeviceFor(compute),
                options.Number("workers", TrainPlanner.DefaultWorkers(machine)),
                options.Number("seed", 0),
                options.Number("patience", TrainPlanner.DefaultPatience),
                !options.Flags.Contains("no-preflight"),
                ParseOverrides(options.Text("set", "")));

            var run = workspace.CreateRun(model, train, compute, image, digest, DateTime.UtcNow);
            await app.Jobs.StartTrainAsync(workspace, run, resume: false);
            Console.WriteLine($"Đã bắt đầu run {run.Id}");
            return options.Flags.Contains("detach") ? 0 : await FollowRunAsync(app, run, "train");
        }

        public static async Task<int> ResumeAsync(AppComposition app, Arguments options)
        {
            var workspace = Workspace.Open(options.Require("workspace"));
            var run = workspace.Run(options.Require("run"));
            var state = await app.Jobs.StateAsync(run);
            if (!RunStates.CanResume(state, run.HasCheckpoint))
            {
                var why = run.HasCheckpoint || RunStates.IsFinal(state) ? "" : " Run dừng trước khi có checkpoint, hãy train lại từ đầu.";
                throw new TrainerException($"Run {run.Id} đang ở trạng thái {state}, không train tiếp được.{why}");
            }

            var fromNow = ReaderAtEnd(run);
            await app.Jobs.StartTrainAsync(workspace, run, resume: true);
            Console.WriteLine($"Đang train tiếp {run.Id}");
            return options.Flags.Contains("detach") ? 0 : await FollowRunAsync(app, run, "train", fromNow);
        }

        public static async Task<int> StatusAsync(AppComposition app, Arguments options)
        {
            var workspace = Workspace.Open(options.Require("workspace"));
            foreach (var run in workspace.Runs())
                Console.WriteLine($"{run.Id,-40} {await app.Jobs.StateAsync(run)}");
            return 0;
        }

        public static async Task<int> FollowAsync(AppComposition app, Arguments options)
        {
            var workspace = Workspace.Open(options.Require("workspace"));
            return await FollowRunAsync(app, workspace.Run(options.Require("run")), "train");
        }

        public static async Task<int> StopAsync(AppComposition app, Arguments options)
        {
            var workspace = Workspace.Open(options.Require("workspace"));
            var run = workspace.Run(options.Require("run"));
            if (!await app.Jobs.IsRunningAsync(run)) throw new TrainerException($"Run {run.Id} không chạy.");
            run.RequestStop();
            Console.WriteLine("Đã yêu cầu dừng. Worker sẽ dừng ở cuối epoch hiện tại, sau khi lưu checkpoint.");
            return 0;
        }

        public static async Task<int> ExportAsync(AppComposition app, Arguments options)
        {
            var workspace = Workspace.Open(options.Require("workspace"));
            var run = workspace.Run(options.Require("run"));
            var fromNow = ReaderAtEnd(run);
            await app.Jobs.StartExportAsync(workspace, run);
            Console.WriteLine($"Đang export ONNX cho {run.Id}");
            return options.Flags.Contains("detach") ? 0 : await FollowRunAsync(app, run, "export", fromNow);
        }

        public static async Task<int> CompareAsync(AppComposition app, Arguments options)
        {
            var workspace = Workspace.Open(options.Require("workspace"));
            Console.WriteLine($"{"Run",-36} {"Trạng thái",-11} {"mAP50",6} {"mAP50-95",8} {"P",6} {"R",6} {"ONNX",-12} {"ms",6}");
            var summaries = new List<RunSummary>();
            foreach (var run in workspace.Runs())
            {
                var summary = RunSummary.Read(run, await app.Jobs.StateAsync(run));
                summaries.Add(summary);
                var overall = summary.Overall;
                Console.WriteLine(
                    $"{run.Id,-36} {summary.State,-11} {Metric(overall?.Map50),6} {Metric(overall?.Map50To95),8} {Metric(overall?.Precision),6} {Metric(overall?.Recall),6} " +
                    $"{summary.Export?.Status ?? "-",-12} {Metric(summary.Export?.LatencyMsMedian),6}");
                foreach (var item in summary.Classes)
                    Console.WriteLine($"    {item.Name,-32} {"",-11} {Metric(item.Metrics.Map50),6} {Metric(item.Metrics.Map50To95),8} {Metric(item.Metrics.Precision),6} {Metric(item.Metrics.Recall),6}");
                if (summary.Synthetic) Output.Warn("Dataset tổng hợp: metric không nói lên chất lượng thật.");
            }

            foreach (var reason in RunSummary.Differences(summaries))
                Output.Warn(reason + " Không so sánh trực tiếp được.");
            return 0;
        }

        /// <summary>Taken before starting a job, so the end state of an earlier job is not read as this one's.</summary>
        private static EventLogReader ReaderAtEnd(RunFolder run)
        {
            var reader = new EventLogReader(run.EventsFile);
            reader.ReadNew();
            return reader;
        }

        private static async Task<int> FollowRunAsync(AppComposition app, RunFolder run, string job, EventLogReader? fromNow = null)
        {
            var reader = fromNow ?? new EventLogReader(run.EventsFile);
            var exportStatus = (string?)null;
            var state = (string?)null;
            while (true)
            {
                var running = await app.Jobs.IsRunningAsync(run);
                foreach (var item in reader.ReadNew())
                {
                    Print(item);
                    if (item.Type == "state") state = item.Text("state");
                    if (item.Type == "export_result") exportStatus = item.Text("status");
                }

                if (job == "export" && exportStatus is not null)
                {
                    await WaitForContainerExitAsync(app, run);
                    return exportStatus == "failed" ? 3 : 0;
                }

                if (job == "train" && state is not null && RunStates.IsFinal(state))
                {
                    await WaitForContainerExitAsync(app, run);
                    Console.WriteLine($"Kết thúc: {state}");
                    return state == RunStates.Failed ? 3 : 0;
                }

                if (!running)
                {
                    foreach (var item in reader.ReadNew()) Print(item);
                    Output.Error($"Container đã dừng mà chưa báo kết thúc. Xem {Path.Combine(run.Path_, "train.log")}; nếu chưa có file đó, chạy probe --container để kiểm tra image.");
                    return 3;
                }

                await Task.Delay(1000);
            }
        }

        private static void Print(TrainerEvent item)
        {
            if (item.Type == "progress")
            {
                Console.WriteLine($"[{item.Timestamp}] Epoch {item.Number("epoch")}/{item.Number("epochs")}, batch {item.Number("batch")}/{item.Number("batches")}");
                return;
            }

            switch (EventText.Describe(item))
            {
                case { Level: LogLevel.Warning } line:
                    Output.Warn(line.Text);
                    break;
                case { Level: LogLevel.Error } line:
                    Output.Error(line.Text);
                    break;
                case { } line:
                    Console.WriteLine($"[{line.Timestamp}] {line.Text}");
                    break;
            }
        }

        /// <summary>The worker reports the end a moment before its container is gone; a next command must not race it.</summary>
        private static async Task WaitForContainerExitAsync(AppComposition app, RunFolder run)
        {
            for (var attempt = 0; attempt < 30 && await app.Jobs.IsRunningAsync(run); attempt++)
                await Task.Delay(1000);
        }

        private static ModelVariant FindModel(AppComposition app, string name) =>
            app.Catalog.Find(name) ?? throw new TrainerException($"Không có model {name}. Chạy models để xem danh sách.");

        /// <summary>Numbers stay numbers, because the engine rejects a string for an augmentation amount.</summary>
        private static IReadOnlyDictionary<string, object>? ParseOverrides(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var result = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var pair in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var parts = pair.Split('=', 2);
                if (parts.Length != 2) throw new TrainerException($"--set cần dạng tên=giá_trị, nhận được {pair}.");
                result[parts[0]] = double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : parts[1];
            }

            return result;
        }

        private static string Metric(double? value) => value?.ToString("0.###", CultureInfo.InvariantCulture) ?? "-";
    }
}
