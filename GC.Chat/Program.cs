﻿using Newtonsoft.Json;
using Etlx = Microsoft.Diagnostics.Tracing.Etlx;
using Spectre.Console;
using System.Text;
using Microsoft.Diagnostics.Tracing.Analysis;
using Microsoft.Diagnostics.Tracing;
using GC.Chat;

static async Task<string> SendMemDocQuestionRequest(string query)
{
    Dictionary<string, string> input = new();
    input["Query"] = query;

    var json = JsonConvert.SerializeObject(input);
    var data = new StringContent(json, Encoding.UTF8, "application/json");

    var url = "http://mrsharm.pythonanywhere.com/querywithsource";
    using var client = new HttpClient();

    var response = await client.PostAsync(url, data);

    var result = await response.Content.ReadAsStringAsync();
    SourceResponse re = JsonConvert.DeserializeObject<SourceResponse>(result);
    return $"{re.answer}. Sources: [{re.sources}]";
}

static async Task<string> SendModelRequest(string query)
{
    Dictionary<string, string> input = new();
    input["Query"] = query;

    var json = JsonConvert.SerializeObject(input);
    var data = new StringContent(json, Encoding.UTF8, "application/json");

    var url = "http://mrsharm.pythonanywhere.com/gc_modelquery";
    using var client = new HttpClient();

    var response = await client.PostAsync(url, data);

    var result = await response.Content.ReadAsStringAsync();
    return result;
}

void HandleAnalyze(string input)
{
    string[] split = input.Split("/analyze", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    if (split.Length != 1)
    {
        AnsiConsole.Markup("[red bold] Please specify a trace path after /analyze. For example: /analyze C:\\Trace1.etl.zip \n[/]");
        return;
    }

    string tracePath = split[0].Replace("\"", "");
    if (!File.Exists(tracePath)) 
    {
        AnsiConsole.Markup("[red bold] Please specify a path to a trace file that exists. \n[/]");
        return;
    }

    AnalyzeTrace(tracePath);
}

static void AnalyzeTrace(string tracePath)
{
    tracePath = tracePath.Replace("\"", "");
    string extension = Path.GetExtension(tracePath);

    if (extension != ".etl" && extension != ".etlx" && !tracePath.EndsWith(".etl.zip"))
    {
        AnsiConsole.Markup("[red bold] A trace file with the following extensions must be provided: .etl, .etlx or .etl.zip \n[/]");
        return;
    }

    if (Path.GetExtension(tracePath).Contains(".zip"))
    {
        ZippedETLReader reader = new(tracePath);
        reader.UnpackArchive();
        tracePath = reader.EtlFileName;
    }

    Etlx.TraceLog traceLog = Etlx.TraceLog.OpenOrConvert(tracePath);
    var eventSource = traceLog.Events.GetSource();
    eventSource.NeedLoadedDotNetRuntimes();
    eventSource.Process();

    var table = new Table();
    table.AddColumn("Process Name");
    table.AddColumn("Total GC Pause Time (MSec)");
    table.AddColumn("% Time Spent in GC");
    table.AddColumn("Total Allocated (MB)");
    table.AddColumn("Mean Heap Size Before (MB)");

    foreach (var p in eventSource.Processes())
    {
        var managedProcess = p.LoadedDotNetRuntime();
        if (managedProcess == null || managedProcess.GC == null || managedProcess.GC.GCs == null || managedProcess.GC.GCs.Count <= 0)
        {
            continue;
        }

        var stats = managedProcess.GC.Stats();
        table.AddRow(
            new Markup[] 
            { 
                new (p.Name), 
                new (stats.TotalPauseTimeMSec.ToString("N2")), 
                new (stats.GetGCPauseTimePercentage().ToString("N2")), 
                new (stats.TotalAllocatedMB.ToString("N2")), 
                new (managedProcess.GC.GCs.Average(gc => gc.HeapSizeBeforeMB).ToString("N2"))
            });
    }

    AnsiConsole.Write(table);
    AnsiConsole.WriteLine();
}

static void AnalyzeProcess(string tracePath, string processName)
{
    tracePath = tracePath.Replace("\"", "");
    processName = processName.Replace("\"", "");

    if (Path.GetExtension(tracePath).Contains(".zip"))
    {
        ZippedETLReader reader = new(tracePath);
        reader.UnpackArchive();
        tracePath = reader.EtlFileName;
    }

    Etlx.TraceLog traceLog = Etlx.TraceLog.OpenOrConvert(tracePath);
    var eventSource = traceLog.Events.GetSource();
    eventSource.NeedLoadedDotNetRuntimes();
    eventSource.Process();

    TraceProcess? processToFind = null;

    foreach (var p in eventSource.Processes())
    {
        var managedProcess = p.LoadedDotNetRuntime();
        if (managedProcess == null || managedProcess.GC == null || managedProcess.GC.GCs == null || managedProcess.GC.GCs.Count <= 0)
        {
            continue;
        }

        if (p.Name.ToLower() == processName.ToLower())
        {
            processToFind = p;
            break;
        }
    }

    if (processToFind == null)
    {
        AnsiConsole.Markup($"[red bold] Process name: {processName} not found in the trace! \n[/]");
        return;
    }

    var managed = processToFind.LoadedDotNetRuntime();
    var stats   = managed.GC.Stats();

    var root = new Tree(processName);

    // Total GC Pause Time (MSec)
    var gcPauseTime = root.AddNode($"[green bold] Total Pause Time: {stats.TotalPauseTimeMSec.ToString("N2")} MSec [/]");
    gcPauseTime.AddNode(new Table()
        .RoundedBorder()
        .AddColumn("GC Type")
        .AddColumn("Total Pause Time (MSec)")
        .AddRow("Gen0", $"{managed.GC.GCs.Where(gc => gc.Generation == 0).Sum(gc => gc.PauseDurationMSec).ToString("N2")}")
        .AddRow("Gen1", $"{managed.GC.GCs.Where(gc => gc.Generation == 1).Sum(gc => gc.PauseDurationMSec).ToString("N2")}")
        .AddRow("Gen2 Blocking", $"{managed.GC.GCs.Where(gc => gc.Generation == 2 && gc.Type != Microsoft.Diagnostics.Tracing.Parsers.Clr.GCType.BackgroundGC).Sum(gc => gc.PauseDurationMSec).ToString("N2")}")
        .AddRow("BGC", $"{managed.GC.GCs.Where(gc => gc.Type == Microsoft.Diagnostics.Tracing.Parsers.Clr.GCType.BackgroundGC).Sum(gc => gc.PauseDurationMSec).ToString("N2")}")
        );

    // % Pause Time In GC
    var gcPauseTimePercent = root.AddNode($"[green bold] % Pause Time In GC: {stats.GetGCPauseTimePercentage().ToString("N2")}% [/]");

    // Heap Size Before (MB)
    var heapSizeBefore = root.AddNode($"[green bold] Mean Heap Size Before: {managed.GC.GCs.Average(gc => gc.HeapSizeBeforeMB).ToString("N2")} MB [/]");

    AnsiConsole.Write(root);
    AnsiConsole.WriteLine();
}

void HandleAnalyzeProcess(string input)
{
    string[] split = input.Split("/analyze-process", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    if (split.Length != 1)
    {
        AnsiConsole.Markup("[red bold] Please specify a trace path and a process name after /analyze-process. For example: /analyze-process C:\\Trace1.etl.zip powershell \n[/]");
    }

    split = split[0].Split(" ", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    if (split.Length != 2)
    {
        AnsiConsole.Markup("[red bold] Please specify a trace path and a process name after /analyze-process. For example: /analyze-process C:\\Trace1.etl.zip powershell \n[/]");
        return;
    }

    string tracePath = split[0].Replace("\"", "");
    string processName = split[1].Replace("\"", "");

    if (Path.GetExtension(tracePath).Contains(".zip"))
    {
        ZippedETLReader reader = new(tracePath);
        reader.UnpackArchive();
        tracePath = reader.EtlFileName;
    }

    Etlx.TraceLog traceLog = Etlx.TraceLog.OpenOrConvert(tracePath);
    var eventSource = traceLog.Events.GetSource();
    eventSource.NeedLoadedDotNetRuntimes();
    eventSource.Process();

    TraceProcess? processToFind = null;

    foreach (var p in eventSource.Processes())
    {
        var managedProcess = p.LoadedDotNetRuntime();
        if (managedProcess == null || managedProcess.GC == null || managedProcess.GC.GCs == null || managedProcess.GC.GCs.Count <= 0)
        {
            continue;
        }

        if (p.Name.ToLower() == processName.ToLower())
        {
            processToFind = p;
            break;
        }
    }

    if (processToFind == null)
    {
        AnsiConsole.Markup($"[red bold] Process name: {processName} not found in the trace! \n[/]");
        return;
    }

    var managed = processToFind.LoadedDotNetRuntime();
    var stats   = managed.GC.Stats();

    var root = new Tree(processName);

    // Total GC Pause Time (MSec)
    var gcPauseTime = root.AddNode($"[green bold] Total Pause Time: {stats.TotalPauseTimeMSec.ToString("N2")} MSec [/]");
    gcPauseTime.AddNode(new Table()
        .RoundedBorder()
        .AddColumn("GC Type")
        .AddColumn("Total Pause Time (MSec)")
        .AddRow("Gen0", $"{managed.GC.GCs.Where(gc => gc.Generation == 0).Sum(gc => gc.PauseDurationMSec).ToString("N2")}")
        .AddRow("Gen1", $"{managed.GC.GCs.Where(gc => gc.Generation == 1).Sum(gc => gc.PauseDurationMSec).ToString("N2")}")
        .AddRow("Gen2 Blocking", $"{managed.GC.GCs.Where(gc => gc.Generation == 2 && gc.Type != Microsoft.Diagnostics.Tracing.Parsers.Clr.GCType.BackgroundGC).Sum(gc => gc.PauseDurationMSec).ToString("N2")}")
        .AddRow("BGC", $"{managed.GC.GCs.Where(gc => gc.Type == Microsoft.Diagnostics.Tracing.Parsers.Clr.GCType.BackgroundGC).Sum(gc => gc.PauseDurationMSec).ToString("N2")}")
        );

    // % Pause Time In GC
    var gcPauseTimePercent = root.AddNode($"[green bold] % Pause Time In GC: {stats.GetGCPauseTimePercentage().ToString("N2")}% [/]");

    // Heap Size Before (MB)
    var heapSizeBefore = root.AddNode($"[green bold] Mean Heap Size Before: {managed.GC.GCs.Average(gc => gc.HeapSizeBeforeMB).ToString("N2")} MB [/]");

    AnsiConsole.Write(root);
    AnsiConsole.WriteLine();
}

AnsiConsole.Markup("[green bold underline] Welcome to the GC Chat! \n[/]");
AnsiConsole.Markup("[green bold] Ask me a question! \n[/]");
//AnsiConsole.Markup("[green bold] To start a GC investigation, take a top level GC trace using PerfView with the following command: `perfview /GCCollectOnly /AcceptEULA /nogui collect` and press 's' after you have collected the trace. \n[/]");
//AnsiConsole.Markup("[green bold] \n Or call one of the following commands: \n[/]"); 
//AnsiConsole.Markup("[green bold] 1. Ask a GC related question to an AI bot. \n[/]");
//AnsiConsole.Markup("[green bold] 2. Enter /analyze and then specify a path to a trace to get a top level GC overview. \n[/]");
//AnsiConsole.Markup("[green bold] 3. Enter /analyze-process and then specify a path to a trace and a process name to get a top level GC overview. \n[/]");
//AnsiConsole.Markup("[green bold] 4. Enter /compare and then specify a path to two traces to get a top level GC comparison. \n[/]");
AnsiConsole.Markup("[green bold] Ctrl+C to exit. \n[/]");

while (true)
{
    AnsiConsole.Write("\n>> ");
    string? input = Console.ReadLine();

    string[] split = input?.Split(" ");

    if (string.IsNullOrEmpty(input) || input.Length == 0)
    {
        continue;
    }

    else if (split[0] == "/analyze")
    {
        HandleAnalyze(input);
    }

    else if (split[0] == "/analyze-process")
    {
        HandleAnalyzeProcess(input);
    }

    else
    {
        await HandleModelRequest(input);
    }
}

static async Task HandleModelRequest(string input)
{
    await AnsiConsole.Status()
        .Spinner(Spinner.Known.Moon)
        .StartAsync("The GC AI Bot is thinking..", async ctx =>
        {
            string result = await SendModelRequest(input);
            Console.WriteLine(result);
            ModelResponse response = JsonConvert.DeserializeObject<ModelResponse>(result);
            AnsiConsole.WriteLine(result.Replace("\"", "").TrimStart());

            if (response.prompt_types.Count == 1)
            {
                string promptType = response.prompt_types.First();
                switch (promptType)
                {
                    case "question": case "unknown":
                        AnsiConsole.WriteLine(response.response_by_model);
                        break;
                    case "performance_issue":
                        AnsiConsole.WriteLine($"I see you want to start a performance investigation for an issue of type: {response.performance_issue_type.Replace("_", " ")}, to get started with this please provide a top-level GC trace. If you are on windows, " +
                            $"call the following perfview command and press 's' when you are done.: ``perfview /GCCollectOnly /AcceptEULA /nogui collect``. " +
                            $"If you are on Linux, call the following command: dotnet trace collect -p <pid> -o <outputpath with .nettrace extension> --profile gc-collect --duration <in hh:mm:ss format>");
                        break;
                    case "analyze":
                        {
                            if (response.files_mentioned.Count == 0)
                            {
                                AnsiConsole.WriteLine(response.response_by_model);
                            }

                            else
                            {
                                if (string.IsNullOrEmpty(response.process_name))
                                {
                                    AnalyzeTrace(response.files_mentioned.First());
                                }

                                else
                                {
                                    AnalyzeProcess(response.files_mentioned.First(), response.process_name);
                                }
                            }

                            break;
                        }
                }
            }

            // if performance_issue but no analyze.. ask for a trace.
            // if question -> display response.
            // if analyze -> call api on the file.

            // If it's a single state => EASY.

            // Multiple states - handle accordingly.

            /*
            foreach (var state in response.prompt_types)
            {
                switch (state)
                {
                    case "question": case "unknown":
                        AnsiConsole.WriteLine(response.response_by_model);
                        break;
                    case "analyze":
                        AnsiConsole.WriteLine($"Analyzing: {string.Join(", ", response.files_mentioned)}");
                        break;
                    case "performance_issue":
                        AnsiConsole.WriteLine(response.response_by_model);
                        break;
                }
            }
            */
        });
}
