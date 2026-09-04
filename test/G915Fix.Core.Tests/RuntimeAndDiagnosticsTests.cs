using G915Fix.Core.Diagnostics;
using G915Fix.Core.Input;
using G915Fix.Core.Runtime;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace G915Fix.Core.Tests;

[TestClass]
public sealed class RuntimeAndDiagnosticsTests
{
    [TestMethod]
    public void RuntimeState_PublishesOnlyRealTransitions()
    {
        var state = new InputFilterRuntimeState();
        var changes = new List<InputFilterRuntimeSnapshot>();
        state.Changed += (_, snapshot) => changes.Add(snapshot);
        var active = new InputFilterRuntimeSnapshot(InputFilterRuntimeStatus.Active, KeyboardFilteringActive: true);

        state.Update(InputFilterRuntimeSnapshot.Inactive);
        state.Update(active);
        state.Update(active);

        Assert.AreEqual(active, state.Current);
        CollectionAssert.AreEqual(new[] { active }, changes);
    }

    [TestMethod]
    public async Task JsonLinesSink_WritesVersionedEventsWithoutBlockingCallers()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "events.jsonl");
        try
        {
            await using (var sink = new JsonLinesFilterDiagnosticSink(path))
            {
                sink.Record(new FilterDiagnosticEvent(
                    FilterDiagnosticEvent.CurrentSchemaVersion,
                    DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
                    FilterDiagnosticEventKind.KeyboardFiltered,
                    HidKeyboardUsage.A,
                    Action: FilterDiagnosticAction.RepressBlocked));
            }

            string content = await File.ReadAllTextAsync(path);
            StringAssert.Contains(content, "\"kind\":0");
            StringAssert.Contains(content, "\"action\":0");
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
