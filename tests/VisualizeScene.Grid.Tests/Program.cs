using System.Xml.Linq;
using VisualizeScene.Core;

var results = new List<(string Name,string? Error)>();
void Test(string name,Action body)
{
    try { body(); results.Add((name,null)); Console.WriteLine("PASS " + name); }
    catch(Exception error) { results.Add((name,error.ToString())); Console.WriteLine("FAIL " + name + "\n" + error); }
}
GridTests.Run(Test); GridRenderingTests.Run(Test);
Directory.CreateDirectory("artifacts/validation");
File.WriteAllText("artifacts/validation/grid.xml",new XElement("testsuite",new XAttribute("name","Grid geometry, input and rendering"),
    new XAttribute("tests",results.Count),new XAttribute("failures",results.Count(r=>r.Error is not null)),results.Select(r=>
        new XElement("testcase",new XAttribute("name",r.Name),r.Error is null ? null : new XElement("failure",r.Error)))).ToString());
Console.WriteLine($"{results.Count(r=>r.Error is null)}/{results.Count} passed");
return results.Any(r=>r.Error is not null) ? 1 : 0;

static class Cases
{
    public static void Check(bool condition) { if(!condition) throw new Exception("Grid rendering assertion failed."); }
    public static void Throws<T>(Action action) where T:Exception { try { action(); } catch(T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    public static GraphSnapshot Graph() => new([new("n",new(35,45,90,50),"Grid test")],[],[],[]);
}
