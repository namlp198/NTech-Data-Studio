namespace NTech.DataStudio.Trainer.Runs;

public sealed record RunState(RunFolder Run, string State, bool ContainerRunning);

/// <summary>
/// The state of every run in a workspace, read incrementally so a screen can refresh it every few
/// seconds without reading each events.jsonl from the start.
/// </summary>
public sealed class RunStateTracker
{
    private readonly Dictionary<string, (EventLogReader Reader, string? Last)> _runs = new(StringComparer.Ordinal);

    public IReadOnlyList<RunState> Refresh(Workspace workspace, IReadOnlySet<string> running)
    {
        var result = new List<RunState>();
        foreach (var run in workspace.Runs())
        {
            if (!File.Exists(Path.Combine(run.Path_, "run.json"))) continue;
            if (!_runs.TryGetValue(run.Id, out var entry)) entry = (new EventLogReader(run.EventsFile), null);
            foreach (var item in entry.Reader.ReadNew())
                if (item.Type == "state") entry.Last = item.Text("state");
            _runs[run.Id] = entry;

            var live = running.Contains(run.Id);
            result.Add(new RunState(run, RunStates.Derive(entry.Last, live), live));
        }

        return result;
    }
}
