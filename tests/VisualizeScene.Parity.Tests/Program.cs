using System.Diagnostics;
using System.Xml.Linq;

var results = new List<(string Name, string? Error, double Time)>();
void Test(string name, Action action)
{
    var timer = Stopwatch.StartNew(); string? error = null;
    try { action(); Console.WriteLine("PASS " + name); }
    catch (Exception ex) { error = ex.ToString(); Console.WriteLine("FAIL " + name + "\n" + ex); }
    results.Add((name, error, timer.Elapsed.TotalSeconds));
}
PlacementTests.Run(Test);
PlacementBudgetTests.Run(Test);
SnappingTests.Run(Test);
SelectionTests.Run(Test);
ReviewTests.Run(Test);
NumericTests.Run(Test);
Directory.CreateDirectory("artifacts/validation");
File.WriteAllText("artifacts/validation/parity.xml", new XElement("testsuite", new XAttribute("name", "Placement, snapping and selection"),
    new XAttribute("tests", results.Count), new XAttribute("failures", results.Count(r => r.Error is not null)), results.Select(r =>
        new XElement("testcase", new XAttribute("name", r.Name), new XAttribute("time", r.Time), r.Error is null ? null : new XElement("failure", r.Error)))).ToString());
Console.WriteLine($"{results.Count(r => r.Error is null)}/{results.Count} passed");
return results.Any(r => r.Error is not null) ? 1 : 0;
